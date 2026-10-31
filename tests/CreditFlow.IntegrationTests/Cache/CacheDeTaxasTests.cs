using CreditFlow.BuildingBlocks.ValueObjects;
using CreditFlow.IntegrationTests.Infra;
using CreditFlow.Modules.Simulacao.Application;
using CreditFlow.Modules.Simulacao.Domain;
using CreditFlow.Modules.Simulacao.Infrastructure.Cache;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.Caching.StackExchangeRedis;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using StackExchange.Redis;

namespace CreditFlow.IntegrationTests.Cache;

/// <summary>
/// Cada "pod" é um ServiceProvider próprio: L1 próprio, conexão própria, o mesmo Dragonfly.
/// A origem conta quantas vezes foi chamada: é o número que importa.
/// </summary>
public sealed class CacheDeTaxasTests(DragonflyFixture dragonfly) : IClassFixture<DragonflyFixture>
{
    private sealed class OrigemQueConta : ITabelaDeTaxas
    {
        public int Chamadas;
        public async Task<IReadOnlyList<FaixaDeScore>> ObterFaixasAsync(CancellationToken ct)
        {
            Interlocked.Increment(ref Chamadas);
            await Task.Delay(200, ct); // origem "lenta": alarga a janela da debandada
            return [new("A", 800, Percentual.DePontos(1.49m)), new("D", 0, Percentual.DePontos(4.49m))];
        }
    }

    private sealed record Pod(ServiceProvider Sp, ITabelaDeTaxas Tabela, InvalidadorDeTaxas Invalidador) : IAsyncDisposable
    {
        public ValueTask DisposeAsync() => Sp.DisposeAsync();
    }

    private Pod NovoPod(OrigemQueConta origem, string prefixo, string? conexao = null)
    {
        var s = new ServiceCollection();
        s.AddLogging();
        var opcoes = CacheDeTaxasExtensions.OpcoesDoDragonfly(conexao ?? dragonfly.Conexao);
        s.AddSingleton<IConnectionMultiplexer>(_ => ConnectionMultiplexer.Connect(opcoes));
        s.AddStackExchangeRedisCache(o => o.InstanceName = prefixo);
        s.AddOptions<RedisCacheOptions>().Configure<IServiceProvider>((o, sp) =>
            o.ConnectionMultiplexerFactory = () => Task.FromResult(sp.GetRequiredService<IConnectionMultiplexer>()));
        s.AddHybridCache();
        var sp = s.BuildServiceProvider();
        var cache = sp.GetRequiredService<HybridCache>();
        return new Pod(sp, new TabelaDeTaxasEmCache(origem, cache),
            new InvalidadorDeTaxas(cache, NullLogger<InvalidadorDeTaxas>.Instance, sp.GetRequiredService<IConnectionMultiplexer>()));
    }

    private static string Prefixo() => "teste:" + Guid.NewGuid().ToString("N")[..8] + ":";

    [Fact]
    public async Task Debandada_200_chamadas_juntas_uma_ida_a_origem()
    {
        var origem = new OrigemQueConta();
        await using var pod = NovoPod(origem, Prefixo());

        await Task.WhenAll(Enumerable.Range(0, 200).Select(_ => pod.Tabela.ObterFaixasAsync(CancellationToken.None)));

        Assert.Equal(1, origem.Chamadas);
    }

    [Fact]
    public async Task Dragonfly_fora_fica_mais_lento_nao_fora_do_ar()
    {
        var origem = new OrigemQueConta();
        // Porta onde não há nada: o mesmo que o Dragonfly cair (sem derrubar o contêiner compartilhado).
        await using var pod = NovoPod(origem, Prefixo(), "127.0.0.1:1");

        var faixas = await pod.Tabela.ObterFaixasAsync(CancellationToken.None);
        await pod.Invalidador.InvalidarAsync("teste", CancellationToken.None); // não lança

        Assert.Equal(2, faixas.Count);
        Assert.Equal(1, origem.Chamadas);
    }

    [Fact]
    public async Task Segundo_pod_le_do_L2_sem_ir_a_origem_e_invalidacao_chega_aos_dois()
    {
        var prefixo = Prefixo();
        var origemA = new OrigemQueConta();
        var origemB = new OrigemQueConta();
        await using var podA = NovoPod(origemA, prefixo);
        await using var podB = NovoPod(origemB, prefixo);

        await podA.Tabela.ObterFaixasAsync(CancellationToken.None); // A vai à origem e grava L1(A) + L2
        await podB.Tabela.ObterFaixasAsync(CancellationToken.None); // B acha no L2
        Assert.Equal((1, 0), (origemA.Chamadas, origemB.Chamadas));

        await podA.Invalidador.InvalidarAsync("taxas mudaram", CancellationToken.None);
        // O pub/sub é assíncrono; aqui B apaga o próprio L1 como o ouvinte faria ao receber o aviso.
        await podB.Invalidador.ApagarLocalAsync(CancellationToken.None);

        await podB.Tabela.ObterFaixasAsync(CancellationToken.None);
        Assert.Equal(1, origemB.Chamadas); // L1 e L2 limpos: B foi à origem
    }
}
