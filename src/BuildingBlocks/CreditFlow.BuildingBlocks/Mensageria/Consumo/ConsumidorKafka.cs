using System.Diagnostics;
using System.Globalization;
using System.Text;
using Confluent.Kafka;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace CreditFlow.BuildingBlocks.Mensageria;

/// <summary>
/// Consumidor genérico: um grupo, N tópicos, um mapa de manipuladores. O laço, em uma linha:
/// <b>ler → (inbox + efeito na mesma transação) → guardar offset → commit periódico</b>.
///
/// Garantias:
/// <list type="bullet">
/// <item>"Pelo menos uma vez": o offset só é guardado depois do efeito; se o pod cair antes do commit, a mensagem volta.</item>
/// <item>Ordem por chave: a mensagem que falha bloqueia a partição dela (retry no lugar), nunca é "pulada para depois".</item>
/// <item>Partição nunca trava para sempre: falha permanente vai para a DLT na hora; desconhecida, depois de N tentativas.</item>
/// <item>Nunca é expulso do grupo por retry longo: no máximo <c>JanelaDeRetryEmMemoria</c> sem voltar ao Consume().</item>
/// </list>
/// </summary>
public sealed partial class ConsumidorKafka : BackgroundService
{
    private readonly ConsumidorOpcoes _opcoes;
    private readonly MapaDeManipuladores _mapa;
    private readonly FabricaDeClientesKafka _fabrica;
    private readonly IProducer<string, byte[]> _produtorDlt;
    private readonly IServiceScopeFactory _escopos;
    private readonly TimeProvider _relogio;
    private readonly ILogger _log;

    // Tentativas por offset, sobrevivendo ao Seek (a mesma mensagem volta pelo Consume()).
    private readonly Dictionary<TopicPartitionOffset, int> _tentativas = new();
    private int _naoComitadas;
    private long _ultimoCommit;
    private long _ultimoBatimento; // timestamp do TimeProvider; 0 = laço ainda não começou

    public ConsumidorKafka(ConsumidorOpcoes opcoes, MapaDeManipuladores mapa, FabricaDeClientesKafka fabrica,
        IProducer<string, byte[]> produtorDlt, IServiceScopeFactory escopos, TimeProvider relogio, ILoggerFactory logs)
    {
        _opcoes = opcoes;
        _mapa = mapa;
        _fabrica = fabrica;
        _produtorDlt = produtorDlt;
        _escopos = escopos;
        _relogio = relogio;
        _log = logs.CreateLogger($"CreditFlow.Mensageria.Consumidor.{opcoes.Grupo}");
    }

    public string Grupo => _opcoes.Grupo;

    /// <summary>Quanto tempo desde a última volta do laço. Nulo se o laço ainda não começou. Lido pela liveness.</summary>
    public TimeSpan? TempoDesdeOUltimoBatimento
    {
        get
        {
            var ultimo = Interlocked.Read(ref _ultimoBatimento);
            return ultimo == 0 ? null : _relogio.GetElapsedTime(ultimo);
        }
    }

    private void Bater() => Interlocked.Exchange(ref _ultimoBatimento, _relogio.GetTimestamp());

    protected override Task ExecuteAsync(CancellationToken ct) =>
        // ExecuteAsync roda dentro do StartAsync do host até o primeiro await: um laço que começa com
        // Consume() (bloqueante) seguraria a subida da aplicação. StartNew(LongRunning) tira o laço daí.
        // Depois do primeiro await o laço continua em threads do pool; o Consume() bloqueia uma delas
        // por até 500 ms por volta, custo aceitável para um consumidor por processo.
        Task.Factory.StartNew(() => LacoAsync(ct), ct, TaskCreationOptions.LongRunning, TaskScheduler.Default).Unwrap();

    private async Task LacoAsync(CancellationToken ct)
    {
        using var consumidor = new ConsumerBuilder<string, byte[]>(_fabrica.ConfigDoConsumidor(_opcoes.Grupo))
            .SetErrorHandler((_, e) => LogErroKafka(_log, e.Code, e.Reason, e.IsFatal))
            .SetPartitionsAssignedHandler((_, particoes) => LogParticoes(_log, "recebidas", string.Join(",", particoes)))
            .SetPartitionsRevokedHandler((c, particoes) =>
            {
                // Antes de entregar as partições a outro membro, confirma o que já foi processado:
                // reduz as reentregas num rebalanceamento (KEDA subindo/descendo réplicas).
                Comitar(c, "revogacao");
                LogParticoes(_log, "revogadas", string.Join(",", particoes));
            })
            .SetPartitionsLostHandler((_, particoes) => LogParticoes(_log, "perdidas", string.Join(",", particoes)))
            .Build();

        consumidor.Subscribe(_opcoes.Topicos);
        LogIniciado(_log, _opcoes.Grupo, string.Join(",", _opcoes.Topicos));

        try
        {
            while (!ct.IsCancellationRequested)
            {
                Bater();
                ConsumeResult<string, byte[]>? resultado;
                try
                {
                    resultado = consumidor.Consume(TimeSpan.FromMilliseconds(500));
                }
                catch (ConsumeException ex) when (!ex.Error.IsFatal)
                {
                    LogErroKafka(_log, ex.Error.Code, ex.Error.Reason, false);
                    continue;
                }

                if (resultado is null || resultado.IsPartitionEOF)
                {
                    ComitarSeVenceu(consumidor);
                    continue;
                }

                var desfecho = await ProcessarComRetryAsync(consumidor, resultado, ct);
                if (desfecho == Desfecho.VoltarAoPoll)
                    continue; // Seek feito: a mesma mensagem volta no próximo Consume()

                _tentativas.Remove(resultado.TopicPartitionOffset);
                GuardarOffset(consumidor, resultado);
                ComitarSeVenceu(consumidor);
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // desligando
        }
        finally
        {
            Comitar(consumidor, "encerramento");
            consumidor.Close(); // sai do grupo na hora: as partições vão para outra réplica sem esperar o session timeout
        }
    }

    private enum Desfecho { Concluida, VoltarAoPoll }

    private async Task<Desfecho> ProcessarComRetryAsync(IConsumer<string, byte[]> consumidor, ConsumeResult<string, byte[]> resultado, CancellationToken ct)
    {
        var esperadoNestaVolta = TimeSpan.Zero;
        while (true)
        {
            var tentativa = _tentativas.GetValueOrDefault(resultado.TopicPartitionOffset) + 1;
            _tentativas[resultado.TopicPartitionOffset] = tentativa;
            var inicio = Stopwatch.GetTimestamp();
            try
            {
                var resultadoDoProcessamento = await ProcessarAsync(resultado, tentativa, ct);
                Rastreamento.ConsumidorProcessadas.Add(1,
                    new("grupo", _opcoes.Grupo), new("resultado", resultadoDoProcessamento));
                Rastreamento.ConsumidorDuracao.Record(Stopwatch.GetElapsedTime(inicio).TotalMilliseconds, new KeyValuePair<string, object?>("grupo", _opcoes.Grupo));
                return Desfecho.Concluida;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw; // não guarda offset: a mensagem será reentregue a quem assumir a partição
            }
            catch (Exception ex)
            {
                var tipo = ClassificadorDeFalhas.Classificar(ex);
                LogFalha(_log, tipo, resultado.TopicPartitionOffset.ToString(), tentativa, ex);

                if (tipo == TipoDeFalha.Permanente ||
                    (tipo == TipoDeFalha.Desconhecida && tentativa >= _opcoes.MaxTentativasAntesDaDlt))
                {
                    if (await TentarEnviarParaDltAsync(resultado, ex, tipo, tentativa, ct))
                        return Desfecho.Concluida;
                    // DLT indisponível: trata como transitória (não perde a mensagem)
                }

                var espera = Recuo(tentativa);
                if (esperadoNestaVolta + espera > _opcoes.JanelaDeRetryEmMemoria)
                {
                    // Volta ao Consume() para manter a participação no grupo; Seek faz a mesma mensagem voltar.
                    consumidor.Seek(resultado.TopicPartitionOffset);
                    return Desfecho.VoltarAoPoll;
                }

                esperadoNestaVolta += espera;
                Bater(); // repetindo com espera ainda é "vivo"; travado num await sem fim, não
                await Task.Delay(espera, _relogio, ct);
            }
        }
    }

    private async Task<string> ProcessarAsync(ConsumeResult<string, byte[]> resultado, int tentativa, CancellationToken ct)
    {
        var headers = resultado.Message.Headers;
        var tipo = Cabecalhos.Ler(headers, Cabecalhos.Tipo);

        using var atividade = Rastreamento.IniciarProcessamento(resultado, _opcoes.Grupo, tipo);

        if (tipo is null)
            throw new ErroPermanenteException($"Mensagem sem o cabeçalho {Cabecalhos.Tipo}.");

        if (!_mapa.Conhece(tipo))
            return "ignorada"; // evento do tópico que não interessa a este grupo

        if (!int.TryParse(Cabecalhos.Ler(headers, Cabecalhos.Versao), NumberStyles.Integer, CultureInfo.InvariantCulture, out var versao))
            throw new ErroPermanenteException($"Mensagem {tipo} sem {Cabecalhos.Versao} válido.");

        var entrada = _mapa.Obter(tipo, versao)
            ?? throw new ErroPermanenteException(
                $"{tipo} v{versao} não é suportado por {_opcoes.Grupo}. Publique a versão nova do consumidor ou mantenha o produtor publicando a anterior.");

        if (!Guid.TryParse(Cabecalhos.Ler(headers, Cabecalhos.EventoId), out var eventoId))
            throw new ErroPermanenteException($"Mensagem {tipo} sem {Cabecalhos.EventoId} válido: sem id não há idempotência.");

        DateTimeOffset? ocorridoEm = DateTimeOffset.TryParse(Cabecalhos.Ler(headers, Cabecalhos.OcorridoEm),
            CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var o) ? o : null;

        var evento = SerializadorDeEventos.Desserializar(resultado.Message.Value, entrada.Descritor.TipoClr);
        var contexto = new ContextoDaMensagem(_opcoes.Grupo, eventoId, tipo, versao, resultado.Topic,
            resultado.Partition.Value, resultado.Offset.Value, resultado.Message.Key, ocorridoEm, tentativa);

        await using var escopo = _escopos.CreateAsyncScope();
        await entrada.Despachar(escopo.ServiceProvider, evento, contexto, ct);
        return "processada";
    }

    private async Task<bool> TentarEnviarParaDltAsync(ConsumeResult<string, byte[]> resultado, Exception ex, TipoDeFalha tipo, int tentativas, CancellationToken ct)
    {
        var topicoDlt = Topicos.DeMensagensMortas(_opcoes.Grupo);
        var headers = new Headers();
        if (resultado.Message.Headers is not null)
            foreach (var h in resultado.Message.Headers)
                headers.Add(h.Key, h.GetValueBytes());

        Cabecalhos.Definir(headers, Cabecalhos.DltMotivo, tipo.ToString());
        Cabecalhos.Definir(headers, Cabecalhos.DltExcecao, ex.GetType().FullName);
        Cabecalhos.Definir(headers, Cabecalhos.DltMensagem, ex.Message.Length > 1000 ? ex.Message[..1000] : ex.Message);
        Cabecalhos.Definir(headers, Cabecalhos.DltTopicoOrigem, resultado.Topic);
        Cabecalhos.Definir(headers, Cabecalhos.DltParticaoOrigem, resultado.Partition.Value.ToString(CultureInfo.InvariantCulture));
        Cabecalhos.Definir(headers, Cabecalhos.DltOffsetOrigem, resultado.Offset.Value.ToString(CultureInfo.InvariantCulture));
        Cabecalhos.Definir(headers, Cabecalhos.DltTentativas, tentativas.ToString(CultureInfo.InvariantCulture));
        Cabecalhos.Definir(headers, Cabecalhos.DltEm, _relogio.GetUtcNow().ToString("O"));

        try
        {
            // Mesma chave e mesmo corpo: dá para reprocessar a DLT republicando no tópico de origem.
            await _produtorDlt.ProduceAsync(topicoDlt,
                new Message<string, byte[]> { Key = resultado.Message.Key, Value = resultado.Message.Value, Headers = headers }, ct);
            Rastreamento.ConsumidorDlt.Add(1, new("grupo", _opcoes.Grupo), new("motivo", tipo.ToString()));
            LogDlt(_log, resultado.TopicPartitionOffset.ToString(), topicoDlt, tipo, tentativas);
            return true;
        }
        catch (Exception erroDlt) when (erroDlt is not OperationCanceledException)
        {
            LogDltFalhou(_log, topicoDlt, erroDlt);
            return false;
        }
    }

    private TimeSpan Recuo(int tentativa)
    {
        var ms = Math.Min(_opcoes.EsperaMaxima.TotalMilliseconds,
            _opcoes.EsperaInicial.TotalMilliseconds * Math.Pow(2, Math.Min(tentativa - 1, 20)));
        return TimeSpan.FromMilliseconds(ms * (0.8 + Random.Shared.NextDouble() * 0.4));
    }

    private void GuardarOffset(IConsumer<string, byte[]> consumidor, ConsumeResult<string, byte[]> resultado)
    {
        try
        {
            consumidor.StoreOffset(resultado); // guarda offset+1 localmente; o Commit() leva ao broker
            _naoComitadas++;
        }
        catch (KafkaException ex) when (ex.Error.Code == ErrorCode.Local_State)
        {
            // A partição foi revogada enquanto processávamos: o novo dono vai reprocessar (inbox descarta).
            LogOffsetDescartado(_log, resultado.TopicPartitionOffset.ToString());
        }
    }

    private void ComitarSeVenceu(IConsumer<string, byte[]> consumidor)
    {
        if (_naoComitadas == 0) return;
        var venceu = _naoComitadas >= _opcoes.CommitACadaMensagens ||
                     _relogio.GetElapsedTime(_ultimoCommit) >= _opcoes.CommitACadaIntervalo;
        if (venceu) Comitar(consumidor, "periodico");
    }

    private void Comitar(IConsumer<string, byte[]> consumidor, string motivo)
    {
        try
        {
            consumidor.Commit(); // envia os offsets guardados por StoreOffset
        }
        catch (KafkaException ex) when (ex.Error.Code == ErrorCode.Local_NoOffset)
        {
            // nada guardado desde o último commit
        }
        catch (KafkaException ex)
        {
            LogCommitFalhou(_log, motivo, ex.Error.Reason);
            return; // os offsets continuam guardados e vão no próximo commit
        }
        _naoComitadas = 0;
        _ultimoCommit = _relogio.GetTimestamp();
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Consumidor {Grupo} iniciado em {Topicos}")]
    private static partial void LogIniciado(ILogger log, string grupo, string topicos);

    [LoggerMessage(Level = LogLevel.Information, Message = "Partições {Evento}: {Particoes}")]
    private static partial void LogParticoes(ILogger log, string evento, string particoes);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Erro do consumidor Kafka {Codigo}: {Motivo} (fatal: {Fatal})")]
    private static partial void LogErroKafka(ILogger log, ErrorCode codigo, string motivo, bool fatal);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Falha {Tipo} em {Posicao}, tentativa {Tentativa}")]
    private static partial void LogFalha(ILogger log, TipoDeFalha tipo, string posicao, int tentativa, Exception ex);

    [LoggerMessage(Level = LogLevel.Error, Message = "Mensagem {Posicao} enviada para {Dlt} ({Tipo}, {Tentativas} tentativas)")]
    private static partial void LogDlt(ILogger log, string posicao, string dlt, TipoDeFalha tipo, int tentativas);

    [LoggerMessage(Level = LogLevel.Error, Message = "Não foi possível publicar na DLT {Dlt}; a mensagem será tentada de novo")]
    private static partial void LogDltFalhou(ILogger log, string dlt, Exception ex);

    [LoggerMessage(Level = LogLevel.Information, Message = "Offset de {Posicao} não guardado: partição revogada durante o processamento")]
    private static partial void LogOffsetDescartado(ILogger log, string posicao);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Commit ({Motivo}) falhou: {Erro}")]
    private static partial void LogCommitFalhou(ILogger log, string motivo, string erro);
}
