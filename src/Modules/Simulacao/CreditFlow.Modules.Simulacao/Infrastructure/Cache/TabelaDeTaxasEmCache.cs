using CreditFlow.BuildingBlocks.ValueObjects;
using CreditFlow.Modules.Simulacao.Application;
using CreditFlow.Modules.Simulacao.Domain;
using Microsoft.Extensions.Caching.Hybrid;

namespace CreditFlow.Modules.Simulacao.Infrastructure.Cache;

/// <summary>
/// Decorador de <see cref="ITabelaDeTaxas"/> (a origem é a tabela da configuração): a mesma interface, com HybridCache (L1 memória + L2 Dragonfly)
/// na frente da origem. Quem usa (SimularEmprestimo) não sabe que existe cache.
///
/// Por que a tabela de taxas é um bom candidato: lida em TODA simulação (milhares por minuto no pico),
/// muda raramente (decisão de comitê), é igual para todo mundo (uma chave só) e um atraso de até
/// 1 minuto na mudança é aceitável porque a simulação não é contrato: a proposta copia as condições,
/// e o que vale é o que o cliente viu.
/// </summary>
internal sealed class TabelaDeTaxasEmCache(ITabelaDeTaxas origem, HybridCache cache) : ITabelaDeTaxas
{
    /// <summary>
    /// Versão no nome da chave: se o FORMATO de <see cref="FaixaEmCache"/> mudar, sobe para v2 e as réplicas
    /// novas e antigas não leem o valor uma da outra durante o rolling update.
    /// </summary>
    public const string Chave = "simulacao:taxas:v1";
    public const string Tag = "taxas";

    internal static readonly HybridCacheEntryOptions Validade = new()
    {
        Expiration = TimeSpan.FromMinutes(10),          // L2: depois disso, alguém relê a origem
        LocalCacheExpiration = TimeSpan.FromMinutes(1), // L1: o máximo que uma réplica fica com taxa velha depois de uma invalidação que não chegou a ela
    };

    private static readonly string[] Tags = [Tag];

    public async Task<IReadOnlyList<FaixaDeScore>> ObterFaixasAsync(CancellationToken ct)
    {
        // A fábrica roda UMA vez por chave e por processo, mesmo com 500 simulações chegando juntas
        // no instante em que a chave venceu (proteção contra debandada do HybridCache).
        var emCache = await cache.GetOrCreateAsync(
            Chave,
            origem,
            static async (o, token) => (await o.ObterFaixasAsync(token)).Select(FaixaEmCache.De).ToArray(),
            Validade, Tags, ct);

        if (emCache.Length == 0)
            throw new InvalidOperationException("Tabela de taxas vazia no cache.");
        return [.. emCache.Select(f => f.ParaDominio())];
    }
}

/// <summary>
/// Forma serializável da faixa (System.Text.Json, que é o serializador padrão do HybridCache para o L2).
/// O <see cref="Percentual"/> do domínio não tem construtor público; em vez de ensinar o JSON a criá-lo,
/// o cache guarda números simples e o domínio fica intocado.
/// </summary>
internal sealed record FaixaEmCache(string Nome, int ScoreMinimo, decimal TaxaMensalFracao)
{
    public static FaixaEmCache De(FaixaDeScore f) => new(f.Nome, f.ScoreMinimo, f.TaxaMensal.Fracao);
    public FaixaDeScore ParaDominio() => new(Nome, ScoreMinimo, Percentual.DeFracao(TaxaMensalFracao));
}
