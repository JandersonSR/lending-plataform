using CreditFlow.BuildingBlocks.ValueObjects;

namespace CreditFlow.Modules.Simulacao.Domain.Calculo;

/// <summary>
/// Gera a tabela de parcelas em centavos. Regra de arredondamento (a mesma do script de conferência
/// em Python usado para os testes):
/// - juros do mês = saldo × taxa, arredondado para centavos (bancário);
/// - Price: parcela = PMT arredondado; amortização = parcela − juros;
/// - SAC: amortização = principal ÷ n arredondado; parcela = amortização + juros;
/// - última parcela: amortiza EXATAMENTE o saldo que sobrou (absorve a sobra dos arredondamentos),
///   para o saldo final ser zero e a soma das amortizações ser igual ao principal, ao centavo.
/// </summary>
public static class TabelaDeAmortizacao
{
    public static IReadOnlyList<ParcelaCalculada> Gerar(
        Dinheiro principal, int prazoMeses, Percentual taxaMensal, SistemaDeAmortizacao sistema, DateOnly dataContratacao)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(prazoMeses);
        if (principal.Valor <= 0)
            throw new ArgumentOutOfRangeException(nameof(principal), "O principal deve ser positivo.");
        if (taxaMensal.Fracao < 0)
            throw new ArgumentOutOfRangeException(nameof(taxaMensal), "A taxa não pode ser negativa.");

        var p = principal.Valor;
        var i = taxaMensal.Fracao;
        var parcelaPrice = sistema == SistemaDeAmortizacao.Price ? ParcelaPrice(p, i, prazoMeses) : 0m;
        var amortizacaoSac = sistema == SistemaDeAmortizacao.Sac ? MatematicaDecimal.Centavos(p / prazoMeses) : 0m;

        var saldo = p;
        var linhas = new List<ParcelaCalculada>(prazoMeses);
        for (var k = 1; k <= prazoMeses; k++)
        {
            var juros = MatematicaDecimal.Centavos(saldo * i);
            var ultima = k == prazoMeses;
            var amortizacao = ultima
                ? saldo
                : sistema == SistemaDeAmortizacao.Price ? parcelaPrice - juros : amortizacaoSac;
            saldo -= amortizacao;

            linhas.Add(new ParcelaCalculada(
                k,
                dataContratacao.AddMonths(k),
                new Dinheiro(amortizacao + juros),
                new Dinheiro(juros),
                new Dinheiro(amortizacao),
                new Dinheiro(saldo)));
        }
        return linhas;
    }

    /// <summary>
    /// PMT = P · i · (1+i)^n / ((1+i)^n − 1), arredondado para centavos.
    /// Com taxa zero a fórmula divide por zero; o limite é P/n.
    /// </summary>
    public static decimal ParcelaPrice(decimal principal, decimal taxa, int prazo)
    {
        if (taxa == 0m)
            return MatematicaDecimal.Centavos(principal / prazo);
        var fator = MatematicaDecimal.Potencia(1m + taxa, prazo);
        return MatematicaDecimal.Centavos(principal * taxa * fator / (fator - 1m));
    }
}
