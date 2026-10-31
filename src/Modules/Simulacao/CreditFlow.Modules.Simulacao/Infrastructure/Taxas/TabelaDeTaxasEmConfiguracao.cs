using CreditFlow.BuildingBlocks.ValueObjects;
using CreditFlow.Modules.Simulacao.Application;
using CreditFlow.Modules.Simulacao.Domain;
using Microsoft.Extensions.Options;

namespace CreditFlow.Modules.Simulacao.Infrastructure.Taxas;

/// <summary>
/// Taxas lidas da configuração (appsettings, variável de ambiente, ConfigMap). <see cref="IOptionsMonitor{T}"/>
/// pega mudanças do arquivo sem reiniciar. Valores DIDÁTICOS, não são taxas de mercado.
/// </summary>
internal sealed class TabelaDeTaxasEmConfiguracao(IOptionsMonitor<SimulacaoOpcoes> opcoes) : ITabelaDeTaxas
{
    public static IReadOnlyList<FaixaDeScore> Padrao { get; } =
    [
        new("A", 800, Percentual.DePontos(1.49m)),
        new("B", 650, Percentual.DePontos(2.19m)),
        new("C", 500, Percentual.DePontos(3.29m)),
        new("D", 0, Percentual.DePontos(4.49m)),
    ];

    public Task<IReadOnlyList<FaixaDeScore>> ObterFaixasAsync(CancellationToken ct)
    {
        var configuradas = opcoes.CurrentValue.Faixas;
        IReadOnlyList<FaixaDeScore> faixas = configuradas.Count == 0
            ? Padrao
            : [.. configuradas.Select(f => new FaixaDeScore(f.Nome, f.ScoreMinimo, Percentual.DePontos(f.TaxaMensalPercentual)))];
        return Task.FromResult(faixas);
    }

    /// <summary>Validação na subida: tabela sem faixa para score 0 deixaria alguém sem taxa.</summary>
    internal static bool Valida(SimulacaoOpcoes o) =>
        o.Faixas.Count == 0
        || (o.Faixas.Any(f => f.ScoreMinimo == 0)
            && o.Faixas.All(f => !string.IsNullOrWhiteSpace(f.Nome) && f.TaxaMensalPercentual is > 0 and < 20)
            && o.Faixas.Select(f => f.ScoreMinimo).Distinct().Count() == o.Faixas.Count);
}
