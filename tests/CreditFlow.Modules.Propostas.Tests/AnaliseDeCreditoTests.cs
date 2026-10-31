using CreditFlow.BuildingBlocks.ValueObjects;
using CreditFlow.Modules.Propostas.Domain;

namespace CreditFlow.Modules.Propostas.Tests;

public sealed class AnaliseDeCreditoTests
{
    private static readonly ParametrosDeAnalise P = new();

    [Theory]
    // score, renda, maior parcela, valor → decisão, código
    [InlineData(299, 10_000, 500, 5_000, Decisao.Recusar, "score-insuficiente")]
    [InlineData(300, 10_000, 500, 5_000, Decisao.EncaminharParaAnalista, "analise-manual")]   // limite inclusivo
    [InlineData(800, 3_000, 901, 5_000, Decisao.Recusar, "comprometimento-de-renda")]          // 30,03% > 30%
    [InlineData(800, 3_000, 900, 5_000, Decisao.EncaminharParaAnalista, "analise-manual")]     // 30% exato passa da recusa, mas não é automática
    [InlineData(700, 5_000, 1_000, 20_000, Decisao.Aprovar, "aprovacao-automatica")]          // 20% exato e 20.000 exato
    [InlineData(700, 5_000, 1_000, 20_000.01, Decisao.EncaminharParaAnalista, "analise-manual")]
    [InlineData(699, 5_000, 500, 5_000, Decisao.EncaminharParaAnalista, "analise-manual")]
    public void Decide_pelas_faixas(int score, double renda, double parcela, double valor, Decisao esperada, string codigo)
    {
        var r = AnaliseDeCredito.Avaliar(score, new Dinheiro((decimal)renda), new Dinheiro((decimal)parcela), new Dinheiro((decimal)valor), P);

        Assert.Equal(esperada, r.Decisao);
        Assert.Equal(codigo, r.Codigo);
    }

    [Fact]
    public void Score_baixo_recusa_antes_de_olhar_comprometimento()
    {
        var r = AnaliseDeCredito.Avaliar(100, new Dinheiro(1_000m), new Dinheiro(900m), new Dinheiro(5_000m), P);
        Assert.Equal("score-insuficiente", r.Codigo);
    }

    [Fact]
    public void Renda_zero_nao_divide_por_zero_e_recusa()
    {
        var r = AnaliseDeCredito.Avaliar(900, Dinheiro.Zero, new Dinheiro(100m), new Dinheiro(5_000m), P);
        Assert.Equal(Decisao.Recusar, r.Decisao);
    }
}
