using CreditFlow.BuildingBlocks.ValueObjects;

namespace CreditFlow.Modules.Simulacao.Domain.Calculo;

/// <summary>
/// Junta as peças: tabela → IOF → valor liberado → CET. Função pura: mesma entrada, mesma saída,
/// sem relógio, sem banco, sem configuração. É o que torna o cálculo testável ao centavo.
/// </summary>
public static class MotorDeCalculo
{
    public static ResultadoDoCalculo Calcular(ParametrosDoCalculo p)
    {
        ArgumentNullException.ThrowIfNull(p);

        var parcelas = TabelaDeAmortizacao.Gerar(p.Principal, p.PrazoMeses, p.TaxaMensal, p.Sistema, p.DataContratacao);
        var iof = CalculadoraDeIof.Calcular(p.Principal, parcelas, p.DataContratacao, p.Iof);

        // O IOF é descontado do valor liberado: o cliente pede 10.000 e recebe 10.000 − IOF.
        var liberado = p.Principal - iof.Total;
        var (cetMensal, _) = CalculadoraDeCet.TaxaInterna(
            liberado.Valor, [.. parcelas.Select(x => x.Valor.Valor)], chuteInicial: p.TaxaMensal.Fracao);

        var totalJuros = new Dinheiro(parcelas.Sum(x => x.Juros.Valor));
        var totalPago = new Dinheiro(parcelas.Sum(x => x.Valor.Valor));

        return new ResultadoDoCalculo(
            parcelas, iof, liberado, totalJuros, totalPago,
            Percentual.DeFracao(cetMensal),
            Percentual.DeFracao(CalculadoraDeCet.Anualizar(cetMensal)));
    }
}
