using CreditFlow.BuildingBlocks.Mensageria;
using CreditFlow.Modules.Pagamentos.Application.Gateway;
using CreditFlow.Modules.Pagamentos.Infrastructure.Gateway;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace CreditFlow.Modules.Pagamentos.Tests.Integracao;

public class IdempotenciaNoPostgresTests(PostgresFixture pg) : IClassFixture<PostgresFixture>
{
    private static GatewayPixSimuladoOpcoes SemFalhas() => new()
    {
        LatenciaMinimaMs = 0, LatenciaMaximaMs = 0, TaxaDeIndisponibilidade = 0, TaxaDeRecusa = 0, TaxaDeTimeoutAposProcessar = 0,
    };

    private GatewayPixSimulado Gateway(GatewayPixSimuladoOpcoes o) =>
        new(pg.NovoContexto(), new MonitorFixo<GatewayPixSimuladoOpcoes>(o), TimeProvider.System, NullLogger<GatewayPixSimulado>.Instance);

    [Fact]
    public async Task Mesma_chave_em_dois_pods_gera_uma_transacao_so()
    {
        var pedido = new PedidoPix($"desembolso-{Guid.NewGuid():N}", "x@pix.dev", 100_000, "teste");
        var respostas = await Task.WhenAll(Gateway(SemFalhas()).EnviarAsync(pedido, default), Gateway(SemFalhas()).EnviarAsync(pedido, default));

        Assert.All(respostas, r => Assert.True(r.Aceito));
        Assert.Equal(respostas[0].IdTransacao, respostas[1].IdTransacao);
        await using var db = pg.NovoContexto();
        Assert.Equal(1, await db.TransacoesPixSimuladas.CountAsync(t => t.ChaveIdempotencia == pedido.ChaveIdempotencia,
            TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Timeout_depois_de_processar_e_retry_com_a_mesma_chave_nao_paga_duas_vezes()
    {
        var pedido = new PedidoPix($"desembolso-{Guid.NewGuid():N}", "x@pix.dev", 100_000, "teste");
        var comTimeout = SemFalhas();
        comTimeout.TaxaDeTimeoutAposProcessar = 1; // sempre perde a resposta

        await Assert.ThrowsAsync<GatewayPixIndisponivelException>(() => Gateway(comTimeout).EnviarAsync(pedido, default));
        var retry = await Gateway(SemFalhas()).EnviarAsync(pedido, default);

        Assert.True(retry.Aceito);
        // A consulta confirma: foi processado, e é a mesma transação do retry.
        Assert.Equal(retry.IdTransacao, await Gateway(SemFalhas()).ObterOuCancelarAsync(pedido.ChaveIdempotencia, default));
    }

    [Fact]
    public async Task Consulta_antes_de_processar_cancela_a_chave_e_envio_atrasado_e_recusado()
    {
        var pedido = new PedidoPix($"desembolso-{Guid.NewGuid():N}", "x@pix.dev", 100_000, "teste");
        Assert.Null(await Gateway(SemFalhas()).ObterOuCancelarAsync(pedido.ChaveIdempotencia, default));

        var atrasado = await Gateway(SemFalhas()).EnviarAsync(pedido, default);
        Assert.False(atrasado.Aceito);
        Assert.Equal("CHAVE_CANCELADA", atrasado.CodigoRecusa);
    }

    [Fact]
    public async Task Inbox_aplica_o_efeito_uma_vez_mesmo_com_reentrega()
    {
        var ctx = new ContextoDaMensagem("teste", Guid.CreateVersion7(), "ContratoAssinado", 1, Topicos.Propostas, 0, 42, "k", null, 1);
        var aplicacoes = 0;

        await using var db1 = pg.NovoContexto();
        var primeira = await db1.ProcessarUmaVezAsync(ctx, _ => { aplicacoes++; return Task.CompletedTask; }, TimeProvider.System, default);
        await using var db2 = pg.NovoContexto();
        var segunda = await db2.ProcessarUmaVezAsync(ctx with { Tentativa = 2 }, _ => { aplicacoes++; return Task.CompletedTask; }, TimeProvider.System, default);

        Assert.True(primeira);
        Assert.False(segunda);
        Assert.Equal(1, aplicacoes);
    }

    private sealed class MonitorFixo<T>(T valor) : IOptionsMonitor<T>
    {
        public T CurrentValue => valor;
        public T Get(string? name) => valor;
        public IDisposable? OnChange(Action<T, string?> listener) => null;
    }
}
