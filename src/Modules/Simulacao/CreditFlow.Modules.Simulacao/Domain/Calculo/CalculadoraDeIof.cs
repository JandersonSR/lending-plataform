using CreditFlow.BuildingBlocks.ValueObjects;

namespace CreditFlow.Modules.Simulacao.Domain.Calculo;

/// <summary>
/// IOF de operação de crédito com parcelas (pessoa física), DIDÁTICO:
/// diário = Σ amortização_k × alíquota diária × min(dias até o vencimento k, limite);
/// adicional = principal × alíquota adicional.
/// Cada parte é somada sem arredondar e arredondada uma vez, no fim.
/// </summary>
public static class CalculadoraDeIof
{
    public static IofCalculado Calcular(
        Dinheiro principal, IReadOnlyList<ParcelaCalculada> parcelas, DateOnly dataContratacao, RegrasDeIof regras)
    {
        ArgumentNullException.ThrowIfNull(parcelas);
        ArgumentNullException.ThrowIfNull(regras);

        var diario = 0m;
        foreach (var parcela in parcelas)
        {
            var dias = Math.Min(parcela.Vencimento.DayNumber - dataContratacao.DayNumber, regras.LimiteDeDias);
            diario += parcela.Amortizacao.Valor * regras.AliquotaDiaria * dias;
        }

        return new IofCalculado(
            new Dinheiro(MatematicaDecimal.Centavos(diario)),
            new Dinheiro(MatematicaDecimal.Centavos(principal.Valor * regras.AliquotaAdicional)));
    }
}
