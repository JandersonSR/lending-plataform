using CreditFlow.BuildingBlocks;
using CreditFlow.Modules.Pagamentos.Domain.Parcelas;

namespace CreditFlow.Modules.Pagamentos.Tests.Dominio;

public class ParcelaTests
{
    private static readonly DateTimeOffset Agora = new(2026, 10, 30, 12, 0, 0, TimeSpan.Zero);

    private static Parcela Nova() =>
        Parcela.Criar(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "sub-1", 1, 45_678, new DateOnly(2026, 11, 30));

    [Fact]
    public void Pagar_com_o_valor_exato_baixa_a_parcela()
    {
        var p = Nova();
        var r = p.Pagar(45_678, Agora);
        Assert.True(r.Sucesso);
        Assert.Equal(StatusParcela.Paga, p.Status);
        Assert.Equal(45_678, p.ValorPagoCentavos);
    }

    [Fact]
    public void Pagar_duas_vezes_e_conflito_e_nao_muda_nada()
    {
        var p = Nova();
        p.Pagar(45_678, Agora);
        var versao = p.Versao;
        var r = p.Pagar(45_678, Agora.AddMinutes(1));
        Assert.True(r.Falhou);
        Assert.Equal(TipoErro.Conflito, r.Erro.Tipo);
        Assert.Equal(versao, p.Versao);
        Assert.Equal(Agora, p.PagaEm);
    }

    [Fact]
    public void Valor_diferente_e_regra_de_negocio()
    {
        var r = Nova().Pagar(45_000, Agora);
        Assert.True(r.Falhou);
        Assert.Equal("parcela.valor_diferente", r.Erro.Codigo);
    }
}
