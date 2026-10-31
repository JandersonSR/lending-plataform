namespace CreditFlow.Modules.Simulacao.Domain.Calculo;

/// <summary>
/// CET (Custo Efetivo Total) mensal = a TIR (taxa interna de retorno) do fluxo do cliente:
/// recebe o valor liberado no mês 0 e paga cada parcela no mês k. É a taxa r que zera
///   f(r) = Σ parcela_k / (1+r)^k − valor liberado.
/// Não existe fórmula fechada para r: resolvemos por Newton-Raphson,
///   r_novo = r − f(r) / f'(r),   com   f'(r) = −Σ k · parcela_k / (1+r)^(k+1).
/// Didático: a regra do Bacen (Resolução CMN 3.517/2007 e alterações) usa dias corridos/365
/// no expoente; aqui usamos meses inteiros. Confira a norma vigente.
/// </summary>
public static class CalculadoraDeCet
{
    public const int MaximoDeIteracoes = 100;
    public static readonly decimal Tolerancia = 1e-12m;

    /// <summary>Devolve a taxa mensal (fração) e quantas iterações foram necessárias.</summary>
    public static (decimal TaxaMensal, int Iteracoes) TaxaInterna(
        decimal valorLiberado, IReadOnlyList<decimal> parcelas, decimal chuteInicial)
    {
        ArgumentNullException.ThrowIfNull(parcelas);
        if (valorLiberado <= 0)
            throw new ArgumentOutOfRangeException(nameof(valorLiberado), "O valor liberado deve ser positivo.");
        if (parcelas.Count == 0)
            throw new ArgumentException("Sem parcelas não há CET.", nameof(parcelas));

        var r = chuteInicial;
        for (var iteracao = 1; iteracao <= MaximoDeIteracoes; iteracao++)
        {
            var v = 1m / (1m + r);   // fator de desconto de um mês
            var vk = 1m;             // v^k, acumulado no laço (evita recalcular a potência)
            var f = -valorLiberado;
            var derivada = 0m;
            for (var k = 1; k <= parcelas.Count; k++)
            {
                vk *= v;
                f += parcelas[k - 1] * vk;
                derivada -= k * parcelas[k - 1] * vk * v;
            }

            if (derivada == 0m)
                throw new InvalidOperationException("Derivada zero: Newton-Raphson não pode continuar.");

            var proximo = r - f / derivada;
            if (proximo <= -1m)
                throw new InvalidOperationException("CET divergiu (taxa ≤ −100%). Fluxo inválido?");
            if (Math.Abs(proximo - r) < Tolerancia)
                return (proximo, iteracao);
            r = proximo;
        }
        throw new InvalidOperationException($"CET não convergiu em {MaximoDeIteracoes} iterações.");
    }

    /// <summary>Taxa anual equivalente: (1 + mensal)^12 − 1 (juros compostos, não "mensal × 12").</summary>
    public static decimal Anualizar(decimal taxaMensal) => MatematicaDecimal.Potencia(1m + taxaMensal, 12) - 1m;
}
