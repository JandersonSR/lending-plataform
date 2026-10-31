using CreditFlow.Modules.Pagamentos.Contracts.Eventos;
using CreditFlow.Modules.Pagamentos.Domain.Desembolsos;

namespace CreditFlow.Modules.Pagamentos.Tests.Dominio;

public class DesembolsoTests
{
    private static readonly DateTimeOffset Agora = new(2026, 10, 30, 12, 0, 0, TimeSpan.Zero);

    private static Desembolso Novo() =>
        Desembolso.Solicitar(Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(), "sub-1", 500_000, " cliente@pix.dev ", Agora);

    [Fact]
    public void Solicitar_comeca_em_Solicitado_sem_tentativas()
    {
        var d = Novo();
        Assert.Equal(StatusDesembolso.Solicitado, d.Status);
        Assert.Equal(0, d.Tentativas);
        Assert.Equal("cliente@pix.dev", d.ChavePix);
        Assert.False(d.Terminal);
    }

    [Fact]
    public void Chave_de_idempotencia_e_deterministica_e_derivada_do_id()
    {
        var d = Novo();
        Assert.Equal($"desembolso-{d.Id:N}", d.ChaveIdempotencia);
        Assert.Equal(d.ChaveIdempotencia, d.ChaveIdempotencia);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Valor_nao_positivo_e_recusado(long valor) =>
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            Desembolso.Solicitar(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), null, valor, "x@pix.dev", Agora));

    [Fact]
    public void Tentativas_incrementam_versao_e_status()
    {
        var d = Novo();
        d.IniciarTentativa();
        d.IniciarTentativa();
        Assert.Equal(StatusDesembolso.EmProcessamento, d.Status);
        Assert.Equal(2, d.Tentativas);
        Assert.Equal(2, d.Versao);
    }

    [Fact]
    public void Concluir_e_terminal_e_nao_aceita_segunda_transicao()
    {
        var d = Novo();
        d.IniciarTentativa();
        d.Concluir("E123", Agora);
        Assert.True(d.Terminal);
        Assert.Equal("E123", d.IdTransacaoGateway);
        Assert.Throws<InvalidOperationException>(() => d.Falhar(MotivoFalhaDesembolso.GatewayIndisponivel, "x", Agora));
        Assert.Throws<InvalidOperationException>(() => d.IniciarTentativa());
    }

    [Fact]
    public void Falhar_guarda_motivo_e_corta_detalhe_longo()
    {
        var d = Novo();
        d.Falhar(MotivoFalhaDesembolso.RecusadoPeloGateway, new string('x', 900), Agora);
        Assert.Equal(StatusDesembolso.Falhou, d.Status);
        Assert.Equal(MotivoFalhaDesembolso.RecusadoPeloGateway, d.Motivo);
        Assert.Equal(500, d.DetalheFalha!.Length);
    }
}
