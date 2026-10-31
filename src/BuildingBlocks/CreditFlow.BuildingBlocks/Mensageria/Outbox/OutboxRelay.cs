using System.Data;
using System.Data.Common;
using System.Text;
using System.Text.Json;
using Confluent.Kafka;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CreditFlow.BuildingBlocks.Mensageria;

/// <summary>
/// O relay (retransmissor) da outbox de UM DbContext. Um por módulo que publica, em todo pod onde o
/// módulo está carregado. Vários pods = vários relays em paralelo, sem coordenação externa:
///
/// <list type="number">
/// <item>BEGIN; trava até <c>FaixasPorCiclo</c> faixas livres: <c>SELECT faixa FROM outbox_faixas ... FOR UPDATE SKIP LOCKED</c>.
///       Faixa travada por outro relay é pulada, sem esperar.</item>
/// <item>Lê as pendentes dessas faixas em ordem de <c>id</c>. Ninguém mais lê essas faixas até o COMMIT.</item>
/// <item>Publica agrupando por chave: chaves diferentes em paralelo, a mesma chave em sequência.
///       Se uma mensagem falha, as seguintes da MESMA chave ficam para o próximo ciclo (ordem).</item>
/// <item>Marca as publicadas e faz COMMIT. Se o pod morrer antes do COMMIT, tudo volta a pendente
///       e será publicado de novo: "pelo menos uma vez". Quem garante o "efeito uma vez" é a inbox do consumidor.</item>
/// </list>
/// </summary>
public sealed partial class OutboxRelay<TDbContext>(
    IServiceScopeFactory escopos,
    IProducer<string, byte[]> produtor,
    IOptions<OutboxOpcoes> opcoes,
    TimeProvider relogio,
    ILogger<OutboxRelay<TDbContext>> log) : BackgroundService
    where TDbContext : DbContext
{
    private readonly OutboxOpcoes _opcoes = opcoes.Value;

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        await Task.Yield(); // não segura a subida do host
        var falhasSeguidas = 0;
        var proximaLimpeza = relogio.GetUtcNow();

        while (!ct.IsCancellationRequested)
        {
            ResultadoDoCiclo resultado;
            try
            {
                resultado = await ExecutarCicloAsync(ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                LogCicloFalhou(log, typeof(TDbContext).Name, ex);
                resultado = new ResultadoDoCiclo(0, 1);
            }

            falhasSeguidas = resultado.Falhas > 0 ? falhasSeguidas + 1 : 0;

            if (relogio.GetUtcNow() >= proximaLimpeza)
            {
                await LimparAsync(ct);
                proximaLimpeza = relogio.GetUtcNow() + _opcoes.IntervaloDeLimpeza;
            }

            var espera = falhasSeguidas > 0
                ? RecuoExponencial(falhasSeguidas)
                : resultado.Publicadas == 0 ? _opcoes.IntervaloOcioso : TimeSpan.Zero; // lote cheio: volta já

            if (espera > TimeSpan.Zero)
            {
                try { await Task.Delay(espera, relogio, ct); }
                catch (OperationCanceledException) { break; }
            }
        }
    }

    private TimeSpan RecuoExponencial(int falhasSeguidas)
    {
        var segundos = Math.Min(_opcoes.EsperaMaximaAposFalha.TotalSeconds, Math.Pow(2, Math.Min(falhasSeguidas, 10)) * 0.25);
        return TimeSpan.FromSeconds(segundos * (0.8 + Random.Shared.NextDouble() * 0.4)); // jitter: relays não sincronizam
    }

    internal async Task<ResultadoDoCiclo> ExecutarCicloAsync(CancellationToken ct)
    {
        await using var escopo = escopos.CreateAsyncScope();
        var db = escopo.ServiceProvider.GetRequiredService<TDbContext>();
        var tabelaOutbox = db.TabelaDe<MensagemOutbox>();
        var tabelaFaixas = db.TabelaDe<FaixaOutbox>();

        // A estratégia de execução (EnableRetryOnFailure) exige que a transação explícita rode dentro dela.
        // Se ela repetir o bloco, mensagens já publicadas serão publicadas de novo: duplicata aceita.
        var estrategia = db.Database.CreateExecutionStrategy();
        return await estrategia.ExecuteAsync(async () =>
        {
            await using var transacao = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct);
            var conexao = db.Database.GetDbConnection();
            var tx = transacao.GetDbTransaction();

            var faixas = await TravarFaixasAsync(conexao, tx, tabelaFaixas, ct);
            if (faixas.Length == 0)
            {
                await transacao.CommitAsync(ct);
                return new ResultadoDoCiclo(0, 0);
            }

            var lote = await LerPendentesAsync(conexao, tx, tabelaOutbox, faixas, ct);
            if (lote.Count == 0)
            {
                await transacao.CommitAsync(ct);
                return new ResultadoDoCiclo(0, 0);
            }

            var resultados = await Task.WhenAll(lote.GroupBy(m => m.Chave).Select(g => PublicarEmSequenciaAsync(g.ToList(), ct)));

            var publicadas = resultados.SelectMany(r => r.Publicadas).ToArray();
            var falhas = resultados.Where(r => r.Falha is not null).Select(r => r.Falha!.Value).ToList();

            if (publicadas.Length > 0)
                await ExecutarAsync(conexao, tx,
                    $"UPDATE {tabelaOutbox} SET publicada_em = @agora WHERE id = ANY(@ids)", ct,
                    ("agora", relogio.GetUtcNow()), ("ids", publicadas));

            foreach (var (id, erro) in falhas)
                await ExecutarAsync(conexao, tx,
                    $"UPDATE {tabelaOutbox} SET tentativas = tentativas + 1, ultimo_erro = @erro WHERE id = @id", ct,
                    ("erro", erro.Length > 2000 ? erro[..2000] : erro), ("id", id));

            await transacao.CommitAsync(ct);

            Rastreamento.OutboxPublicadas.Add(publicadas.Length, new KeyValuePair<string, object?>("modulo", typeof(TDbContext).Name));
            if (falhas.Count > 0)
                Rastreamento.OutboxFalhas.Add(falhas.Count, new KeyValuePair<string, object?>("modulo", typeof(TDbContext).Name));

            return new ResultadoDoCiclo(publicadas.Length, falhas.Count);
        });
    }

    private async Task<ResultadoDaChave> PublicarEmSequenciaAsync(List<LinhaOutbox> mensagensDaChave, CancellationToken ct)
    {
        var publicadas = new List<long>(mensagensDaChave.Count);
        foreach (var linha in mensagensDaChave)
        {
            try
            {
                await PublicarAsync(linha, ct);
                publicadas.Add(linha.Id);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                LogPublicacaoFalhou(log, linha.Topico, linha.Chave, linha.EventoId, linha.Tentativas + 1, ex);
                // Para nesta chave: publicar a próxima agora inverteria a ordem.
                return new ResultadoDaChave(publicadas, (linha.Id, ex.Message));
            }
        }
        return new ResultadoDaChave(publicadas, null);
    }

    private async Task PublicarAsync(LinhaOutbox linha, CancellationToken ct)
    {
        var guardados = JsonSerializer.Deserialize<Dictionary<string, string>>(linha.Cabecalhos) ?? [];
        guardados.TryGetValue(Cabecalhos.TraceParent, out var traceParent);
        guardados.TryGetValue(Cabecalhos.TraceState, out var traceState);

        using var atividade = Rastreamento.IniciarPublicacao(linha.Topico, linha.Chave, traceParent, traceState);
        atividade?.SetTag("creditflow.evento.tipo", linha.Tipo);

        var headers = new Headers();
        foreach (var (nome, valor) in guardados)
            Cabecalhos.Definir(headers, nome, valor);
        Cabecalhos.Definir(headers, Cabecalhos.EventoId, linha.EventoId.ToString());
        Cabecalhos.Definir(headers, Cabecalhos.Tipo, linha.Tipo);
        Cabecalhos.Definir(headers, Cabecalhos.Versao, linha.Versao.ToString(System.Globalization.CultureInfo.InvariantCulture));
        Cabecalhos.Definir(headers, Cabecalhos.OcorridoEm, linha.OcorridoEm.ToString("O"));
        Cabecalhos.Definir(headers, Cabecalhos.ContentType, "application/json");
        Rastreamento.Injetar(headers, atividade, traceParent, traceState);

        // await: o relay só marca como publicada depois do ack do broker (acks=all).
        await produtor.ProduceAsync(linha.Topico,
            new Message<string, byte[]> { Key = linha.Chave, Value = Encoding.UTF8.GetBytes(linha.Conteudo), Headers = headers }, ct);
    }

    private async Task<short[]> TravarFaixasAsync(DbConnection conexao, DbTransaction tx, string tabelaFaixas, CancellationToken ct)
    {
        // ORDER BY random(): relays diferentes começam por faixas diferentes, em vez de todos brigarem pela 0.
        await using var cmd = Comando(conexao, tx,
            $"SELECT faixa FROM {tabelaFaixas} ORDER BY random() LIMIT @limite FOR UPDATE SKIP LOCKED",
            ("limite", _opcoes.FaixasPorCiclo));
        var faixas = new List<short>(_opcoes.FaixasPorCiclo);
        await using var leitor = await cmd.ExecuteReaderAsync(ct);
        while (await leitor.ReadAsync(ct))
            faixas.Add(leitor.GetInt16(0));
        return [.. faixas];
    }

    private async Task<List<LinhaOutbox>> LerPendentesAsync(
        DbConnection conexao, DbTransaction tx, string tabelaOutbox, short[] faixas, CancellationToken ct)
    {
        await using var cmd = Comando(conexao, tx,
            $"""
            SELECT id, evento_id, tipo, versao, topico, chave, conteudo::text, cabecalhos::text, ocorrido_em, tentativas
            FROM {tabelaOutbox}
            WHERE publicada_em IS NULL AND faixa = ANY(@faixas)
            ORDER BY id
            LIMIT @lote
            """,
            ("faixas", faixas), ("lote", _opcoes.TamanhoDoLote));

        var linhas = new List<LinhaOutbox>();
        await using var leitor = await cmd.ExecuteReaderAsync(ct);
        while (await leitor.ReadAsync(ct))
        {
            linhas.Add(new LinhaOutbox(
                leitor.GetInt64(0), leitor.GetGuid(1), leitor.GetString(2), leitor.GetInt32(3), leitor.GetString(4),
                leitor.GetString(5), leitor.GetString(6), leitor.GetString(7),
                leitor.GetFieldValue<DateTimeOffset>(8), leitor.GetInt32(9)));
        }
        return linhas;
    }

    private async Task LimparAsync(CancellationToken ct)
    {
        try
        {
            await using var escopo = escopos.CreateAsyncScope();
            var db = escopo.ServiceProvider.GetRequiredService<TDbContext>();
            var agora = relogio.GetUtcNow();
            // Em lotes, para não segurar travas nem gerar um WAL gigante de uma vez.
            var apagadas = await db.Database.ExecuteSqlRawAsync(
                $"DELETE FROM {db.TabelaDe<MensagemOutbox>()} WHERE id IN (SELECT id FROM {db.TabelaDe<MensagemOutbox>()} " +
                "WHERE publicada_em < {0} LIMIT 5000)", [agora - _opcoes.RetencaoDasPublicadas], ct);
            var inbox = await db.Database.ExecuteSqlRawAsync(
                $"DELETE FROM {db.TabelaDe<MensagemInbox>()} WHERE ctid IN (SELECT ctid FROM {db.TabelaDe<MensagemInbox>()} " +
                "WHERE processada_em < {0} LIMIT 5000)", [agora - _opcoes.RetencaoDaInbox], ct);
            if (apagadas + inbox > 0)
                LogLimpeza(log, typeof(TDbContext).Name, apagadas, inbox);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LogLimpezaFalhou(log, typeof(TDbContext).Name, ex);
        }
    }

    private static DbCommand Comando(DbConnection conexao, DbTransaction tx, string sql, params (string Nome, object Valor)[] parametros)
    {
        var cmd = conexao.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = sql;
        foreach (var (nome, valor) in parametros)
        {
            var p = cmd.CreateParameter();
            p.ParameterName = nome;
            p.Value = valor;
            cmd.Parameters.Add(p);
        }
        return cmd;
    }

    private static async Task ExecutarAsync(DbConnection conexao, DbTransaction tx, string sql, CancellationToken ct,
        params (string Nome, object Valor)[] parametros)
    {
        await using var cmd = Comando(conexao, tx, sql, parametros);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    internal readonly record struct ResultadoDoCiclo(int Publicadas, int Falhas);

    private sealed record ResultadoDaChave(List<long> Publicadas, (long Id, string Erro)? Falha);

    private sealed record LinhaOutbox(
        long Id, Guid EventoId, string Tipo, int Versao, string Topico, string Chave,
        string Conteudo, string Cabecalhos, DateTimeOffset OcorridoEm, int Tentativas);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Ciclo do relay da outbox de {Modulo} falhou; nova tentativa com recuo")]
    private static partial void LogCicloFalhou(ILogger log, string modulo, Exception ex);

    [LoggerMessage(Level = LogLevel.Warning,
        Message = "Falha ao publicar {Topico} chave {Chave} evento {EventoId} (tentativa {Tentativa}); a chave espera o próximo ciclo")]
    private static partial void LogPublicacaoFalhou(ILogger log, string topico, string chave, Guid eventoId, int tentativa, Exception ex);

    [LoggerMessage(Level = LogLevel.Information, Message = "Limpeza de {Modulo}: {Outbox} da outbox e {Inbox} da inbox apagadas")]
    private static partial void LogLimpeza(ILogger log, string modulo, int outbox, int inbox);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Limpeza da outbox/inbox de {Modulo} falhou")]
    private static partial void LogLimpezaFalhou(ILogger log, string modulo, Exception ex);
}
