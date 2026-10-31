using CreditFlow.BuildingBlocks.Mensageria;
using CreditFlow.BuildingBlocks.Observabilidade;
using CreditFlow.Modules.Pagamentos.Application.Gateway;
using CreditFlow.Modules.Pagamentos.Contracts.Eventos;
using CreditFlow.Modules.Pagamentos.Domain.Desembolsos;
using CreditFlow.Modules.Pagamentos.Domain.Parcelas;
using CreditFlow.Modules.Pagamentos.Infrastructure.Persistencia;
using CreditFlow.Modules.Propostas.Contracts.Eventos;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;

namespace CreditFlow.Modules.Pagamentos.Application.Desembolsos;

/// <summary>
/// O passo de Pagamentos na saga: ContratoAssinado → PIX → DesembolsoConcluido | DesembolsoFalhou.
///
/// Três momentos, cada um idempotente por si só, porque o pod pode morrer entre quaisquer dois:
/// <list type="number">
/// <item><b>Transação A</b>: obtém ou cria o Desembolso da proposta (chave única no banco) + outbox DesembolsoSolicitado.</item>
/// <item><b>Gateway</b>: fora de qualquer transação (não segura conexão nem trava esperando rede), com a
///       chave de idempotência do desembolso. Cada tentativa é contada no banco ANTES da chamada.</item>
/// <item><b>Transação B</b>: Concluir/Falhar + parcelas + outbox do desfecho + registro na inbox, juntos.</item>
/// </list>
/// A inbox é gravada só na B: se o pod morrer depois da A, a reentrega não é tratada como duplicata e
/// retoma o desembolso que já existe, com a mesma chave no gateway (nenhum PIX em dobro).
/// </summary>
internal sealed partial class AoContratoAssinado(
    PagamentosDbContext db,
    IGatewayPix gateway,
    IOptions<DesembolsoOpcoes> opcoes,
    MetricasDeCredito metricas,
    TimeProvider relogio,
    ILogger<AoContratoAssinado> log) : IManipuladorDeEvento<ContratoAssinado>
{
    private readonly DesembolsoOpcoes _opcoes = opcoes.Value;

    public async Task ManipularAsync(ContratoAssinado evento, ContextoDaMensagem contexto, CancellationToken ct)
    {
        if (await db.JaProcessadaAsync(contexto, ct))
            return; // reentrega de algo que já terminou

        Validar(evento);

        var desembolso = await ObterOuSolicitarAsync(evento, ct);
        var desfecho = desembolso.Terminal ? null : await ExecutarNoGatewayAsync(desembolso, ct);
        var registrado = await FinalizarAsync(evento, desembolso.Id, desfecho, contexto, ct);

        // SLO do desembolso (contrato assinado → dinheiro decidido), só quando ESTA execução fechou o desembolso.
        if (registrado is not null)
            metricas.RegistrarDesembolso(registrado.IdTransacao is not null ? "concluido" : "falhou",
                relogio.GetUtcNow() - evento.OcorridoEm, registrado.Motivo is { } m ? CodigoDoMotivo(m) : null);
    }

    private static string CodigoDoMotivo(MotivoFalhaDesembolso motivo) => motivo switch
    {
        MotivoFalhaDesembolso.RecusadoPeloGateway => "recusado_pelo_gateway",
        MotivoFalhaDesembolso.GatewayIndisponivel => "gateway_indisponivel",
        MotivoFalhaDesembolso.DadosInvalidos => "dados_invalidos",
        _ => "outro",
    };

    /// <summary>Dados que nunca vão melhorar com nova tentativa: DLT direto (o contrato veio errado).</summary>
    private static void Validar(ContratoAssinado e)
    {
        if (e.ValorLiquidoCentavos <= 0)
            throw new ErroPermanenteException($"ContratoAssinado {e.PropostaId} com valor líquido {e.ValorLiquidoCentavos}.");
        if (string.IsNullOrWhiteSpace(e.ChavePix))
            throw new ErroPermanenteException($"ContratoAssinado {e.PropostaId} sem chave PIX.");
        if (e.Parcelas is null || e.Parcelas.Count == 0)
            throw new ErroPermanenteException($"ContratoAssinado {e.PropostaId} sem cronograma de parcelas.");
        var numeros = e.Parcelas.Select(p => p.Numero).Order().ToList();
        if (!numeros.SequenceEqual(Enumerable.Range(1, numeros.Count)) || e.Parcelas.Any(p => p.ValorCentavos <= 0))
            throw new ErroPermanenteException($"ContratoAssinado {e.PropostaId} com cronograma inválido.");
    }

    // ---------------------------------------------------------------- Transação A
    private async Task<Desembolso> ObterOuSolicitarAsync(ContratoAssinado e, CancellationToken ct)
    {
        var existente = await db.Desembolsos.AsNoTracking().SingleOrDefaultAsync(d => d.PropostaId == e.PropostaId, ct);
        if (existente is not null)
            return existente;

        var agora = relogio.GetUtcNow();
        var novo = Desembolso.Solicitar(Guid.CreateVersion7(), e.PropostaId, e.ClienteId, e.UsuarioId,
            e.ValorLiquidoCentavos, e.ChavePix, agora);
        db.Desembolsos.Add(novo);
        db.AdicionarAoOutbox(new DesembolsoSolicitado(Guid.CreateVersion7(), agora, novo.Id, novo.PropostaId,
            novo.ClienteId, novo.ValorCentavos), novo.PropostaId.ToString(), agora);
        try
        {
            await db.SaveChangesAsync(ct); // uma instrução só: o SaveChanges já abre e fecha a transação
            LogSolicitado(log, novo.Id, novo.PropostaId);
            return novo;
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            // Outro pod criou primeiro (a mesma mensagem em dois consumidores durante um rebalanceamento).
            db.ChangeTracker.Clear();
            return await db.Desembolsos.AsNoTracking().SingleAsync(d => d.PropostaId == e.PropostaId, ct);
        }
    }

    // ---------------------------------------------------------------- Gateway
    private async Task<Desfecho> ExecutarNoGatewayAsync(Desembolso desembolso, CancellationToken ct)
    {
        var pedido = new PedidoPix(desembolso.ChaveIdempotencia, desembolso.ChavePix, desembolso.ValorCentavos,
            $"CreditFlow proposta {desembolso.PropostaId:N}");

        while (true)
        {
            var tentativa = await RegistrarTentativaAsync(desembolso.Id, ct);
            if (tentativa > _opcoes.MaxTentativasNoGateway)
                return await DesistirComSegurancaAsync(desembolso, tentativa - 1, ct);

            try
            {
                var resposta = await gateway.EnviarAsync(pedido, ct);
                return resposta.Aceito
                    ? Desfecho.Concluido(resposta.IdTransacao!)
                    : Desfecho.Falhou(MotivoFalhaDesembolso.RecusadoPeloGateway,
                        $"{resposta.CodigoRecusa}: {resposta.MotivoRecusa}", tentativa);
            }
            catch (GatewayPixIndisponivelException ex)
            {
                // Resultado DESCONHECIDO: o dinheiro pode ter saído. Repetir com a mesma chave é seguro.
                LogGatewayFalhou(log, desembolso.Id, tentativa, _opcoes.MaxTentativasNoGateway, ex.Message);
                if (tentativa >= _opcoes.MaxTentativasNoGateway)
                    return await DesistirComSegurancaAsync(desembolso, tentativa, ct);
                await Task.Delay(Recuo(tentativa), relogio, ct);
            }
        }
    }

    /// <summary>
    /// Antes de declarar falha, transforma "não sei" em fato: pergunta ao gateway pela chave e,
    /// se nada foi processado, cancela a chave. Se nem a consulta responde, NÃO falha: lança
    /// transitória e o consumidor tenta de novo mais tarde (melhor atrasar do que mentir).
    /// </summary>
    private async Task<Desfecho> DesistirComSegurancaAsync(Desembolso desembolso, int tentativas, CancellationToken ct)
    {
        var idTransacao = await gateway.ObterOuCancelarAsync(desembolso.ChaveIdempotencia, ct);
        if (idTransacao is not null)
        {
            LogDescobertoNaConsulta(log, desembolso.Id, idTransacao);
            return Desfecho.Concluido(idTransacao);
        }
        return Desfecho.Falhou(MotivoFalhaDesembolso.GatewayIndisponivel,
            $"Gateway indisponível em {tentativas} tentativas; consulta confirmou que nenhum PIX foi feito.", tentativas);
    }

    private async Task<int> RegistrarTentativaAsync(Guid desembolsoId, CancellationToken ct)
    {
        db.ChangeTracker.Clear();
        var d = await db.Desembolsos.SingleAsync(x => x.Id == desembolsoId, ct);
        if (d.Tentativas >= _opcoes.MaxTentativasNoGateway)
            return d.Tentativas + 1; // esgotado em vidas anteriores (outro pod, reentrega)
        d.IniciarTentativa();
        await db.SaveChangesAsync(ct); // concorrência otimista: dois pods aqui, um perde e o consumidor repete
        return d.Tentativas;
    }

    private TimeSpan Recuo(int tentativa)
    {
        var ms = Math.Min(_opcoes.EsperaMaxima.TotalMilliseconds,
            _opcoes.EsperaInicial.TotalMilliseconds * Math.Pow(2, tentativa - 1));
        return TimeSpan.FromMilliseconds(ms * (0.8 + Random.Shared.NextDouble() * 0.4));
    }

    // ---------------------------------------------------------------- Transação B
    /// <returns>O desfecho gravado por esta execução, ou null se outro pod/mensagem já tinha fechado.</returns>
    private async Task<Desfecho?> FinalizarAsync(ContratoAssinado e, Guid desembolsoId, Desfecho? desfecho,
        ContextoDaMensagem contexto, CancellationToken ct)
    {
        Desfecho? gravado = null;
        await db.ProcessarUmaVezAsync(contexto, async innerCt =>
        {
            gravado = null; // a estratégia de retry pode repetir este bloco
            var d = await db.Desembolsos.SingleAsync(x => x.Id == desembolsoId, innerCt);
            if (d.Terminal || desfecho is null)
                return; // outro pod (ou outra mensagem da mesma proposta) já fechou: só registra a inbox
            gravado = desfecho;

            var agora = relogio.GetUtcNow();
            var chave = d.PropostaId.ToString();
            if (desfecho.IdTransacao is { } idTransacao)
            {
                d.Concluir(idTransacao, agora);
                foreach (var p in e.Parcelas.OrderBy(p => p.Numero))
                    db.Parcelas.Add(Parcela.Criar(d.Id, d.PropostaId, d.ClienteId, d.UsuarioId, p.Numero, p.ValorCentavos, p.Vencimento));
                db.AdicionarAoOutbox(new DesembolsoConcluido(Guid.CreateVersion7(), agora, d.Id, d.PropostaId, d.ClienteId,
                    d.ValorCentavos, idTransacao, agora, e.Parcelas.Count), chave, agora);
                LogConcluido(log, d.Id, idTransacao);
            }
            else
            {
                var motivo = desfecho.Motivo ?? throw new InvalidOperationException("Desfecho de falha sem motivo.");
                var detalhe = desfecho.Detalhe ?? motivo.ToString();
                d.Falhar(motivo, detalhe, agora);
                db.AdicionarAoOutbox(new DesembolsoFalhou(Guid.CreateVersion7(), agora, d.Id, d.PropostaId, d.ClienteId,
                    motivo, d.DetalheFalha ?? detalhe, desfecho.Tentativas), chave, agora);
                LogFalhou(log, d.Id, motivo, detalhe);
            }
        }, relogio, ct);
        return gravado;
    }

    private sealed record Desfecho(string? IdTransacao, MotivoFalhaDesembolso? Motivo, string? Detalhe, int Tentativas)
    {
        public static Desfecho Concluido(string idTransacao) => new(idTransacao, null, null, 0);
        public static Desfecho Falhou(MotivoFalhaDesembolso motivo, string detalhe, int tentativas) => new(null, motivo, detalhe, tentativas);
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Desembolso {DesembolsoId} solicitado para a proposta {PropostaId}")]
    private static partial void LogSolicitado(ILogger log, Guid desembolsoId, Guid propostaId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Gateway falhou no desembolso {DesembolsoId}, tentativa {Tentativa}/{Maximo}: {Motivo}")]
    private static partial void LogGatewayFalhou(ILogger log, Guid desembolsoId, int tentativa, int maximo, string motivo);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Desembolso {DesembolsoId}: a consulta revelou que o PIX {IdTransacao} FOI feito")]
    private static partial void LogDescobertoNaConsulta(ILogger log, Guid desembolsoId, string idTransacao);

    [LoggerMessage(Level = LogLevel.Information, Message = "Desembolso {DesembolsoId} concluído (PIX {IdTransacao})")]
    private static partial void LogConcluido(ILogger log, Guid desembolsoId, string idTransacao);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Desembolso {DesembolsoId} falhou: {Motivo} ({Detalhe})")]
    private static partial void LogFalhou(ILogger log, Guid desembolsoId, MotivoFalhaDesembolso motivo, string detalhe);
}
