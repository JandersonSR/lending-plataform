using CreditFlow.BuildingBlocks.ValueObjects;
using CreditFlow.Modules.Simulacao.Domain.Calculo;
using Sistema = CreditFlow.Modules.Simulacao.Domain.Calculo.SistemaDeAmortizacao;

namespace CreditFlow.Modules.Simulacao.Tests;

/// <summary>
/// Os valores esperados NÃO foram tirados do próprio C#: vieram de uma implementação independente do
/// mesmo algoritmo em Python (decimal de 28 dígitos, arredondamento bancário), e o CET foi conferido
/// ainda por um terceiro método (bisseção em ponto flutuante). Ver docs do curso, lição 2.6.
/// Data de contratação fixa: 15/01/2026.
/// </summary>
public sealed class MotorDeCalculoTests
{
    private static readonly DateOnly Contratacao = new(2026, 1, 15);

    /// <summary>Um caso conferido em Python. Gerado por gerar_casos.py (ver lição 2.6).</summary>
    private sealed record Caso(
        decimal Principal, int Prazo, decimal Taxa, Sistema Sistema,
        decimal Primeira, decimal Ultima, decimal IofDiario, decimal IofAdicional, decimal Liberado,
        decimal TotalJuros, decimal TotalPago, decimal CetMensal, decimal CetAnual);

    private static readonly Dictionary<string, Caso> Referencia = new()
    {
        ["Price_10mil_12m_2pc"] = new(10000m, 12, 0.02m, Sistema.Price, 945.60m, 945.55m, 167.37m, 38.00m, 9794.63m, 1347.15m, 11347.15m, 0.0233952169m, 0.3198378965m),
        ["Sac_10mil_12m_2pc"] = new(10000m, 12, 0.02m, Sistema.Sac, 1033.33m, 850.04m, 161.47m, 38.00m, 9800.53m, 1300.00m, 11300.00m, 0.0234109302m, 0.3200810970m),
        ["Price_50mil_48m_1_49pc"] = new(50000m, 48, 0.0149m, Sistema.Price, 1465.62m, 1465.37m, 1371.18m, 190.00m, 48438.82m, 20349.51m, 70349.51m, 0.0163951934m, 0.2154895547m),
        ["Sac_50mil_48m_1_49pc"] = new(50000m, 48, 0.0149m, Sistema.Sac, 1786.67m, 1057.03m, 1324.21m, 190.00m, 48485.79m, 18252.44m, 68252.44m, 0.0164965098m, 0.2169443027m),
        ["Price_1mil_6m_4_49pc"] = new(1000m, 6, 0.0449m, Sistema.Price, 193.82m, 193.80m, 8.95m, 3.80m, 987.25m, 162.90m, 1162.90m, 0.0488923562m, 0.7732543986m),
        ["Sac_1mil_6m_4_49pc"] = new(1000m, 6, 0.0449m, Sistema.Sac, 211.57m, 174.13m, 8.64m, 3.80m, 987.56m, 157.15m, 1157.15m, 0.0489277822m, 0.7739732252m),
        ["Price_7mil_24m_3_29pc"] = new(7000m, 24, 0.0329m, Sistema.Price, 426.35m, 426.38m, 173.24m, 26.60m, 6800.16m, 3232.43m, 10232.43m, 0.0356476890m, 0.5224550709m),
    };

    // Só o nome vai para o xUnit: string é serializável, então cada caso aparece separado no Test Explorer.
    public static TheoryData<string> Casos => new(Referencia.Keys);

    private static ResultadoDoCalculo Calcular(decimal principal, int prazo, decimal taxa, Sistema sistema, RegrasDeIof? iof = null) =>
        MotorDeCalculo.Calcular(new ParametrosDoCalculo(
            new Dinheiro(principal), prazo, Percentual.DeFracao(taxa), sistema, Contratacao, iof ?? RegrasDeIof.PessoaFisica));

    [Theory]
    [MemberData(nameof(Casos))]
    public void Bate_com_a_implementacao_de_referencia(string nome)
    {
        var c = Referencia[nome];
        var r = Calcular(c.Principal, c.Prazo, c.Taxa, c.Sistema);

        Assert.Equal(c.Prazo, r.Parcelas.Count);
        Assert.Equal(c.Primeira, r.Parcelas[0].Valor.Valor);
        Assert.Equal(c.Ultima, r.Parcelas[^1].Valor.Valor);
        Assert.Equal(c.IofDiario, r.Iof.Diario.Valor);
        Assert.Equal(c.IofAdicional, r.Iof.Adicional.Valor);
        Assert.Equal(c.Liberado, r.ValorLiberado.Valor);
        Assert.Equal(c.TotalJuros, r.TotalJuros.Valor);
        Assert.Equal(c.TotalPago, r.TotalPago.Valor);
        Proximo(c.CetMensal, r.CetMensal.Fracao, 1e-9m);
        Proximo(c.CetAnual, r.CetAnual.Fracao, 1e-9m);
    }

    [Fact]
    public void Price_10mil_12m_2pc_tabela_completa()
    {
        (int Numero, string Vencimento, decimal Valor, decimal Juros, decimal Amortizacao, decimal Saldo)[] esperado =
        [
            (1, "2026-02-15", 945.60m, 200.00m, 745.60m, 9254.40m),
            (2, "2026-03-15", 945.60m, 185.09m, 760.51m, 8493.89m),
            (3, "2026-04-15", 945.60m, 169.88m, 775.72m, 7718.17m),
            (4, "2026-05-15", 945.60m, 154.36m, 791.24m, 6926.93m),
            (5, "2026-06-15", 945.60m, 138.54m, 807.06m, 6119.87m),
            (6, "2026-07-15", 945.60m, 122.40m, 823.20m, 5296.67m),
            (7, "2026-08-15", 945.60m, 105.93m, 839.67m, 4457.00m),
            (8, "2026-09-15", 945.60m, 89.14m, 856.46m, 3600.54m),
            (9, "2026-10-15", 945.60m, 72.01m, 873.59m, 2726.95m),
            (10, "2026-11-15", 945.60m, 54.54m, 891.06m, 1835.89m),
            (11, "2026-12-15", 945.60m, 36.72m, 908.88m, 927.01m),
            (12, "2027-01-15", 945.55m, 18.54m, 927.01m, 0.00m),
        ];

        var r = Calcular(10_000m, 12, 0.02m, Sistema.Price);

        for (var k = 0; k < esperado.Length; k++)
        {
            var p = r.Parcelas[k];
            Assert.Equal(esperado[k].Numero, p.Numero);
            Assert.Equal(DateOnly.Parse(esperado[k].Vencimento, System.Globalization.CultureInfo.InvariantCulture), p.Vencimento);
            Assert.Equal(esperado[k].Valor, p.Valor.Valor);
            Assert.Equal(esperado[k].Juros, p.Juros.Valor);
            Assert.Equal(esperado[k].Amortizacao, p.Amortizacao.Valor);
            Assert.Equal(esperado[k].Saldo, p.SaldoDevedor.Valor);
        }
    }

    [Theory]
    [InlineData(1000, 6, 0.0449, Sistema.Price)]
    [InlineData(1000, 6, 0.0449, Sistema.Sac)]
    [InlineData(33333.33, 7, 0.0219, Sistema.Price)]
    [InlineData(33333.33, 7, 0.0219, Sistema.Sac)]
    [InlineData(50000, 48, 0.0149, Sistema.Price)]
    [InlineData(12345.67, 37, 0.0329, Sistema.Sac)]
    public void Amortizacoes_somam_o_principal_ao_centavo_e_o_saldo_termina_em_zero(double principalD, int prazo, double taxaD, Sistema sistema)
    {
        // InlineData não aceita decimal; converter de double aqui é seguro porque os literais têm poucas casas.
        var principal = (decimal)principalD;
        var r = Calcular(principal, prazo, (decimal)taxaD, sistema);

        Assert.Equal(principal, r.Parcelas.Sum(p => p.Amortizacao.Valor));
        Assert.Equal(0m, r.Parcelas[^1].SaldoDevedor.Valor);
        Assert.All(r.Parcelas, p => Assert.Equal(p.Valor.Valor, p.Juros.Valor + p.Amortizacao.Valor));
        Assert.All(r.Parcelas, p => Assert.Equal(decimal.Round(p.Valor.Valor, 2), p.Valor.Valor)); // tudo em centavos
    }

    [Fact]
    public void Price_tem_parcelas_iguais_exceto_o_ajuste_da_ultima()
    {
        var r = Calcular(50_000m, 48, 0.0149m, Sistema.Price);
        Assert.Single(r.Parcelas.Take(47).Select(p => p.Valor).Distinct());
        Assert.True(Math.Abs(r.Parcelas[^1].Valor.Valor - r.Parcelas[0].Valor.Valor) < 1m, "o ajuste da última é de centavos");
    }

    [Fact]
    public void Sac_tem_amortizacao_constante_e_parcelas_decrescentes()
    {
        var r = Calcular(10_000m, 12, 0.02m, Sistema.Sac);
        Assert.Single(r.Parcelas.Take(11).Select(p => p.Amortizacao).Distinct());
        for (var k = 1; k < r.Parcelas.Count; k++)
            Assert.True(r.Parcelas[k].Valor < r.Parcelas[k - 1].Valor);
    }

    [Fact]
    public void Sac_paga_menos_juros_que_Price_no_mesmo_emprestimo()
    {
        var price = Calcular(10_000m, 12, 0.02m, Sistema.Price);
        var sac = Calcular(10_000m, 12, 0.02m, Sistema.Sac);
        Assert.True(sac.TotalJuros < price.TotalJuros);
    }

    [Fact]
    public void Sem_IOF_o_CET_e_a_propria_taxa_de_juros_a_menos_do_arredondamento()
    {
        var r = Calcular(10_000m, 12, 0.02m, Sistema.Price, RegrasDeIof.Isento);
        Assert.Equal(0m, r.Iof.Total.Valor);
        Assert.Equal(10_000m, r.ValorLiberado.Valor);
        // 0,0200000527 (Python): a diferença é só o centavo arredondado da parcela.
        Proximo(0.0200000527m, r.CetMensal.Fracao, 1e-9m);
    }

    [Fact]
    public void Com_IOF_o_CET_e_maior_que_a_taxa()
    {
        var r = Calcular(10_000m, 12, 0.02m, Sistema.Price);
        Assert.True(r.CetMensal.Fracao > 0.02m);
    }

    [Fact]
    public void Taxa_zero_divide_o_principal_igualmente_e_CET_zero_sem_IOF()
    {
        var r = Calcular(1_200m, 12, 0m, Sistema.Price, RegrasDeIof.Isento);
        Assert.All(r.Parcelas, p => Assert.Equal(100m, p.Valor.Valor));
        Proximo(0m, r.CetMensal.Fracao, 1e-12m);
    }

    [Fact]
    public void IOF_diario_conta_no_maximo_365_dias_por_parcela()
    {
        var parcelas = new[]
        {
            new ParcelaCalculada(1, Contratacao.AddDays(100), new Dinheiro(1000m), Dinheiro.Zero, new Dinheiro(1000m), Dinheiro.Zero),
            new ParcelaCalculada(2, Contratacao.AddDays(700), new Dinheiro(1000m), Dinheiro.Zero, new Dinheiro(1000m), Dinheiro.Zero),
        };
        var iof = CalculadoraDeIof.Calcular(new Dinheiro(2000m), parcelas, Contratacao, RegrasDeIof.PessoaFisica);

        // 1000 × 0,000082 × 100 = 8,20  +  1000 × 0,000082 × 365 (e não 700) = 29,93  →  38,13
        Assert.Equal(38.13m, iof.Diario.Valor);
        Assert.Equal(7.60m, iof.Adicional.Valor); // 2000 × 0,38%
    }

    [Fact]
    public void Newton_Raphson_converge_em_poucas_iteracoes()
    {
        var (taxa, iteracoes) = CalculadoraDeCet.TaxaInterna(9794.63m, Enumerable.Repeat(945.60m, 12).ToList(), 0.02m);
        Assert.InRange(iteracoes, 1, 8);
        Assert.InRange(taxa, 0.0233m, 0.0235m);
    }

    [Fact]
    public void CET_anual_e_composto_e_nao_mensal_vezes_doze()
    {
        Proximo(0.2682417946m, CalculadoraDeCet.Anualizar(0.02m), 1e-9m); // 1,02^12 − 1
        Assert.NotEqual(0.24m, CalculadoraDeCet.Anualizar(0.02m));
    }

    [Theory]
    [InlineData(1.02, 0, 1)]
    [InlineData(1.02, 1, 1.02)]
    [InlineData(2, 10, 1024)]
    [InlineData(1.5, 3, 3.375)]
    public void Potencia_inteira_em_decimal(double baseD, int expoente, double esperadoD) =>
        Assert.Equal((decimal)esperadoD, MatematicaDecimal.Potencia((decimal)baseD, expoente));

    private static void Proximo(decimal esperado, decimal real, decimal tolerancia) =>
        Assert.True(Math.Abs(esperado - real) <= tolerancia, $"esperado {esperado}, obtido {real} (tolerância {tolerancia})");
}
