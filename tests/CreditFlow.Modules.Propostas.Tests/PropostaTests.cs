using CreditFlow.Modules.Propostas.Domain;
using static CreditFlow.Modules.Propostas.Tests.Construtor;

namespace CreditFlow.Modules.Propostas.Tests;

public sealed class PropostaTests
{
    [Fact]
    public void Nasce_em_analise_e_registra_os_eventos()
    {
        var p = Criar(score: 650);

        Assert.Equal(StatusProposta.EmAnalise, p.Status);
        Assert.Collection(p.EventosDeDominio,
            e => Assert.IsType<PropostaCriada>(e),
            e => Assert.IsType<PropostaEnviadaParaAnalise>(e));
    }

    [Fact]
    public void Nasce_aprovada_pela_analise_automatica_com_validade()
    {
        var p = Criar(score: 850, renda: 10_000m);

        Assert.Equal(StatusProposta.Aprovada, p.Status);
        Assert.True(p.DecisaoAutomatica);
        Assert.Equal(Agora.AddDays(7), p.ValidaAte);
        Assert.Contains(p.EventosDeDominio, e => e is PropostaAprovada { Automatica: true });
    }

    [Fact]
    public void Sem_consentimento_nao_cria()
    {
        var r = Proposta.Criar(Guid.NewGuid(), Cliente, Ana, Simulacao(), 800, new(5_000m), temConsentimento: false, new(), Agora);
        Assert.Equal("propostas.sem-consentimento", r.Erro?.Codigo);
    }

    [Fact]
    public void Simulacao_vencida_nao_vira_proposta()
    {
        var r = Proposta.Criar(Guid.NewGuid(), Cliente, Ana, Simulacao(realizadaEm: Agora.AddDays(-8)), 800, new(5_000m), true, new(), Agora);
        Assert.Equal("propostas.simulacao-vencida", r.Erro?.Codigo);
    }

    [Fact]
    public void Simulacao_de_outro_cliente_nao_vira_proposta()
    {
        var r = Proposta.Criar(Guid.NewGuid(), Cliente, Ana, Simulacao(cliente: Guid.NewGuid()), 800, new(5_000m), true, new(), Agora);
        Assert.Equal("propostas.simulacao-de-outro-cliente", r.Erro?.Codigo);
    }

    [Fact]
    public void Analista_aprova_so_com_justificativa()
    {
        var p = Criar(score: 650);

        Assert.Equal("propostas.motivo", p.AprovarManualmente("carla", "ok", Agora, 7).Erro?.Codigo);
        Assert.True(p.AprovarManualmente("carla", "Renda comprovada por holerite.", Agora, 7).Sucesso);
        Assert.Equal(StatusProposta.Aprovada, p.Status);
        Assert.Equal("carla", p.AnalisadoPor);
    }

    [Fact]
    public void Nao_aprova_duas_vezes()
    {
        var p = Criar(score: 650);
        p.AprovarManualmente("carla", "Renda comprovada por holerite.", Agora, 7);

        var segunda = p.RecusarManualmente("alice", "Mudei de ideia sobre o risco.", Agora);

        Assert.Equal("propostas.transicao-invalida", segunda.Erro?.Codigo);
        Assert.Equal(StatusProposta.Aprovada, p.Status);
    }

    [Fact]
    public void Contratar_gera_cronograma_mensal_a_partir_da_contratacao()
    {
        var p = Criar(score: 850, renda: 10_000m);
        p.LimparEventos();

        var r = p.Contratar(" ana@pix.com ", Agora); // 30/10/2026

        Assert.True(r.Sucesso);
        var contrato = Assert.IsType<ContratoAssinado>(Assert.Single(p.EventosDeDominio));
        Assert.Equal("ana@pix.com", contrato.ChavePix);
        Assert.Equal(12, contrato.Parcelas.Count);
        Assert.Equal(new DateOnly(2026, 11, 30), contrato.Parcelas[0].Vencimento);
        Assert.Equal(new DateOnly(2027, 10, 30), contrato.Parcelas[^1].Vencimento);
        Assert.Equal(990_000, contrato.ValorLiberadoCentavos);
    }

    [Fact]
    public void Contratar_depois_da_validade_expira_a_proposta()
    {
        var p = Criar(score: 850, renda: 10_000m);

        var r = p.Contratar("ana@pix.com", Agora.AddDays(8));

        Assert.Equal("propostas.aprovacao-vencida", r.Erro?.Codigo);
        Assert.Equal(StatusProposta.Expirada, p.Status);
    }

    [Fact]
    public void Saga_e_idempotente()
    {
        var p = Criar(score: 850, renda: 10_000m);
        p.Contratar("ana@pix.com", Agora);
        p.LimparEventos();

        Assert.True(p.RegistrarDesembolsoConcluido(Agora).Sucesso);
        Assert.True(p.RegistrarDesembolsoConcluido(Agora).Sucesso); // evento repetido
        Assert.Single(p.EventosDeDominio);
        Assert.Equal(StatusProposta.Liberada, p.Status);
        Assert.True(p.RegistrarDesembolsoFalhou("tarde demais", Agora).Falhou); // não volta atrás
    }

    [Fact]
    public void Compensacao_desfaz_a_contratacao()
    {
        var p = Criar(score: 850, renda: 10_000m);
        p.Contratar("ana@pix.com", Agora);

        Assert.True(p.RegistrarDesembolsoFalhou("chave PIX inexistente", Agora).Sucesso);
        Assert.Equal(StatusProposta.DesembolsoFalhou, p.Status);
        Assert.True(p.Status.Final());
        Assert.False(p.Status.Ativa());
    }

    [Fact]
    public void Cliente_cancela_antes_de_contratar_mas_nao_depois()
    {
        var emAnalise = Criar(score: 650);
        Assert.True(emAnalise.Cancelar(Agora).Sucesso);

        var contratada = Criar(score: 850, renda: 10_000m);
        contratada.Contratar("ana@pix.com", Agora);
        Assert.True(contratada.Cancelar(Agora).Falhou);
    }
}

public sealed class CronogramaTests
{
    [Fact]
    public void Fim_de_mes_nao_escorrega()
    {
        var p = Construtor.Criar(score: 850, renda: 10_000m);
        p.LimparEventos();
        // Aprovada em 30/10, contratada em 31/10 (dentro da validade de 7 dias).
        p.Contratar("ana@pix.com", new DateTimeOffset(2026, 10, 31, 15, 0, 0, TimeSpan.Zero));

        var parcelas = Assert.IsType<ContratoAssinado>(Assert.Single(p.EventosDeDominio)).Parcelas;
        Assert.Equal(new DateOnly(2026, 11, 30), parcelas[0].Vencimento);
        Assert.Equal(new DateOnly(2026, 12, 31), parcelas[1].Vencimento);
        Assert.Equal(new DateOnly(2027, 2, 28), parcelas[3].Vencimento);
        Assert.Equal(new DateOnly(2027, 3, 31), parcelas[4].Vencimento);
    }
}
