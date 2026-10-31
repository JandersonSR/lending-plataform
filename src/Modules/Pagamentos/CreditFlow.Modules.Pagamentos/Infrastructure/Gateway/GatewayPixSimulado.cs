using CreditFlow.Modules.Pagamentos.Application.Gateway;
using CreditFlow.Modules.Pagamentos.Infrastructure.Persistencia;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CreditFlow.Modules.Pagamentos.Infrastructure.Gateway;

/// <summary>
/// PSP de mentira, com os defeitos de um de verdade. A memória de idempotência fica no Postgres
/// (pix_simulado), e não num dicionário em memória: o retry pode cair em OUTRO pod, e um dicionário
/// por pod mandaria o PIX duas vezes, exatamente o bug que o laboratório quer mostrar que não existe.
/// </summary>
internal sealed partial class GatewayPixSimulado(
    PagamentosDbContext db,
    IOptionsMonitor<GatewayPixSimuladoOpcoes> opcoes,
    TimeProvider relogio,
    ILogger<GatewayPixSimulado> log) : IGatewayPix
{
    private static readonly Lock TravaSorteio = new();
    private static Random? _sorteio;

    public async Task<RespostaPix> EnviarAsync(PedidoPix pedido, CancellationToken ct)
    {
        var o = opcoes.CurrentValue;
        await Task.Delay(TimeSpan.FromMilliseconds(Sortear(o, r => r.Next(o.LatenciaMinimaMs, Math.Max(o.LatenciaMinimaMs, o.LatenciaMaximaMs) + 1))), relogio, ct);

        if (o.ChavesSempreIndisponiveis.Contains(pedido.ChavePix, StringComparer.OrdinalIgnoreCase) ||
            Sortear(o, r => r.NextDouble()) < o.TaxaDeIndisponibilidade)
        {
            LogIndisponivel(log, pedido.ChaveIdempotencia);
            throw new GatewayPixIndisponivelException("Gateway PIX respondeu 503 (simulado).");
        }

        var existente = await LerAsync(pedido.ChaveIdempotencia, ct);
        if (existente is not null)
        {
            LogRepetido(log, pedido.ChaveIdempotencia, existente.Situacao);
            return existente.Situacao == TransacaoPixSimulada.Cancelada
                ? RespostaPix.Recusado("CHAVE_CANCELADA", "A chave de idempotência foi cancelada por consulta anterior.")
                : RespostaPix.Aprovado(existente.IdTransacao!);
        }

        if (o.ChavesSempreRecusadas.Contains(pedido.ChavePix, StringComparer.OrdinalIgnoreCase) ||
            Sortear(o, r => r.NextDouble()) < o.TaxaDeRecusa)
            return RespostaPix.Recusado("CONTA_DESTINO_INVALIDA", "Chave PIX sem conta ativa (simulado).");

        // "Processa": grava a transação. ON CONFLICT: dois pods com a mesma chave ao mesmo tempo
        // geram UMA transação; o perdedor lê a do vencedor logo abaixo.
        var idTransacao = $"E{relogio.GetUtcNow():yyyyMMddHHmmss}{Guid.NewGuid():N}"[..32].ToUpperInvariant();
        await db.Database.ExecuteSqlAsync($"""
            INSERT INTO pagamentos.pix_simulado (chave_idempotencia, situacao, id_transacao, chave_pix, valor_centavos, criada_em)
            VALUES ({pedido.ChaveIdempotencia}, {TransacaoPixSimulada.Processada}, {idTransacao}, {pedido.ChavePix}, {pedido.ValorCentavos}, {relogio.GetUtcNow()})
            ON CONFLICT (chave_idempotencia) DO NOTHING
            """, ct);
        var gravada = await LerAsync(pedido.ChaveIdempotencia, ct)
            ?? throw new InvalidOperationException("pix_simulado sem a linha recém-gravada.");

        if (gravada.Situacao == TransacaoPixSimulada.Cancelada)
            return RespostaPix.Recusado("CHAVE_CANCELADA", "A chave de idempotência foi cancelada por consulta anterior.");

        if (Sortear(o, r => r.NextDouble()) < o.TaxaDeTimeoutAposProcessar)
        {
            LogTimeoutAposProcessar(log, pedido.ChaveIdempotencia, gravada.IdTransacao!);
            throw new GatewayPixIndisponivelException("Timeout lendo a resposta do gateway (simulado): o PIX FOI processado.",
                new TimeoutException());
        }

        return RespostaPix.Aprovado(gravada.IdTransacao!);
    }

    public async Task<string?> ObterOuCancelarAsync(string chaveIdempotencia, CancellationToken ct)
    {
        var o = opcoes.CurrentValue;
        if (Sortear(o, r => r.NextDouble()) < o.TaxaDeIndisponibilidade)
            throw new GatewayPixIndisponivelException("Consulta ao gateway PIX respondeu 503 (simulado).");

        // Se não existe, grava a "lápide": a partir daqui, um envio atrasado com essa chave é recusado.
        await db.Database.ExecuteSqlAsync($"""
            INSERT INTO pagamentos.pix_simulado (chave_idempotencia, situacao, id_transacao, chave_pix, valor_centavos, criada_em)
            VALUES ({chaveIdempotencia}, {TransacaoPixSimulada.Cancelada}, NULL, '', 0, {relogio.GetUtcNow()})
            ON CONFLICT (chave_idempotencia) DO NOTHING
            """, ct);
        var linha = await LerAsync(chaveIdempotencia, ct);
        return linha?.Situacao == TransacaoPixSimulada.Processada ? linha.IdTransacao : null;
    }

    private Task<TransacaoPixSimulada?> LerAsync(string chave, CancellationToken ct) =>
        db.TransacoesPixSimuladas.AsNoTracking().SingleOrDefaultAsync(t => t.ChaveIdempotencia == chave, ct);

    private static T Sortear<T>(GatewayPixSimuladoOpcoes o, Func<Random, T> sortear)
    {
        lock (TravaSorteio)
        {
            _sorteio ??= o.Semente is { } s ? new Random(s) : new Random();
            return sortear(_sorteio);
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Gateway PIX simulado: indisponível para {Chave}")]
    private static partial void LogIndisponivel(ILogger log, string chave);

    [LoggerMessage(Level = LogLevel.Information, Message = "Gateway PIX simulado: chave {Chave} repetida ({Situacao}), devolvendo o mesmo resultado")]
    private static partial void LogRepetido(ILogger log, string chave, string situacao);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Gateway PIX simulado: {Chave} PROCESSADA ({Transacao}) mas a resposta vai se perder")]
    private static partial void LogTimeoutAposProcessar(ILogger log, string chave, string transacao);
}
