using CreditFlow.BuildingBlocks;
using CreditFlow.BuildingBlocks.ValueObjects;
using CreditFlow.Modules.Clientes.Contracts;
using CreditFlow.Modules.Simulacao.Application;
using CreditFlow.Modules.Simulacao.Domain;
using CreditFlow.Modules.Simulacao.Domain.Calculo;
using CreditFlow.Modules.Simulacao.Infrastructure.Memoria;
using CreditFlow.Modules.Simulacao.Infrastructure.Taxas;

namespace CreditFlow.Modules.Simulacao.Tests;

public sealed class ValoresDaSimulacaoTests
{
    [Theory]
    [InlineData(1000.00, true)]
    [InlineData(50000.00, true)]
    [InlineData(999.99, false)]
    [InlineData(50000.01, false)]
    public void Valor_vai_de_mil_a_cinquenta_mil(double valor, bool valido) =>
        Assert.Equal(valido, ValorDoEmprestimo.Criar((decimal)valor).Sucesso);

    [Fact]
    public void Valor_com_fracao_de_centavo_e_recusado_e_nao_arredondado()
    {
        var r = ValorDoEmprestimo.Criar(1000.001m);
        Assert.True(r.Falhou);
        Assert.Equal("dinheiro.casas", r.Erro.Codigo);
    }

    [Theory]
    [InlineData(5, false)]
    [InlineData(6, true)]
    [InlineData(48, true)]
    [InlineData(49, false)]
    public void Prazo_vai_de_6_a_48_meses(int meses, bool valido) =>
        Assert.Equal(valido, PrazoEmMeses.Criar(meses).Sucesso);

    [Theory]
    [InlineData(1000, "A")]
    [InlineData(800, "A")]
    [InlineData(799, "B")]
    [InlineData(650, "B")]
    [InlineData(500, "C")]
    [InlineData(499, "D")]
    [InlineData(0, "D")]
    public void Faixa_pelo_score_com_minimo_inclusivo(int score, string faixa) =>
        Assert.Equal(faixa, SeletorDeFaixa.Escolher(TabelaDeTaxasEmConfiguracao.Padrao, score).Nome);

    [Fact]
    public void Sem_score_cai_na_faixa_de_maior_risco() =>
        Assert.Equal("D", SeletorDeFaixa.Escolher(TabelaDeTaxasEmConfiguracao.Padrao, null).Nome);

    [Fact]
    public void Valor_e_prazo_invalidos_voltam_juntos_com_os_dois_campos()
    {
        var faixa = new FaixaDeScore("D", 0, Percentual.DePontos(4.49m));
        var r = SimulacaoDeEmprestimo.Criar(10m, 100, SistemaDeAmortizacao.Price, faixa, null, new DateOnly(2026, 1, 15), DateTimeOffset.UnixEpoch);

        Assert.True(r.Falhou);
        Assert.Equal(TipoErro.Validacao, r.Erro.Tipo);
        Assert.NotNull(r.Erro.Campos);
        Assert.Contains("valor", r.Erro.Campos.Keys);
        Assert.Contains("prazoMeses", r.Erro.Campos.Keys);
    }

    [Fact]
    public void Validade_e_de_sete_dias()
    {
        var faixa = new FaixaDeScore("D", 0, Percentual.DePontos(4.49m));
        var criada = new DateTimeOffset(2026, 1, 15, 12, 0, 0, TimeSpan.Zero);
        var s = SimulacaoDeEmprestimo.Criar(10_000m, 12, SistemaDeAmortizacao.Price, faixa, null, new DateOnly(2026, 1, 15), criada).Valor;

        Assert.False(s.Vencida(criada.AddDays(7)));
        Assert.True(s.Vencida(criada.AddDays(7).AddTicks(1)));
    }
}

public sealed class SimularEmprestimoTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    // 15/01/2026 02:00 UTC = 14/01/2026 23:00 em Brasília.
    private readonly RelogioFixo _relogio = new(new DateTimeOffset(2026, 1, 15, 2, 0, 0, TimeSpan.Zero));
    private readonly ClientesFalsos _clientes = new();
    private readonly PublicadorEspiao _publicador = new();
    private readonly SimulacaoRepositorioEmMemoria _repositorio = new();

    private SimularEmprestimo CasoDeUso() => new(new TabelaFixa(), _repositorio, _publicador, _clientes, _relogio);

    [Fact]
    public async Task Anonimo_simula_na_faixa_D_grava_e_publica_uma_vez()
    {
        var r = await CasoDeUso().ExecutarAsync(new SimularEmprestimoComando(10_000m, 12, "price", null), Ct);

        Assert.True(r.Sucesso);
        Assert.Equal("D", r.Valor.Faixa.Nome);
        Assert.Same(r.Valor, await _repositorio.ObterAsync(r.Valor.Id, Ct));
        Assert.Single(_publicador.Publicadas);
    }

    [Fact]
    public async Task Data_de_contratacao_e_o_dia_em_Brasilia_e_nao_em_UTC()
    {
        var r = await CasoDeUso().ExecutarAsync(new SimularEmprestimoComando(10_000m, 12, "sac", null), Ct);
        Assert.Equal(new DateOnly(2026, 1, 14), r.Valor.DataContratacao);
        Assert.Equal(new DateOnly(2026, 2, 14), r.Valor.Resultado.Parcelas[0].Vencimento);
    }

    [Fact]
    public async Task Cliente_com_consentimento_usa_o_score()
    {
        var id = Guid.CreateVersion7();
        _clientes.Perfis[id] = new PerfilDeCreditoDto(id, 820, 1_200_000, ConsentimentoAnaliseDeCredito: true);

        var r = await CasoDeUso().ExecutarAsync(new SimularEmprestimoComando(10_000m, 12, "price", id), Ct);

        Assert.Equal("A", r.Valor.Faixa.Nome);
        Assert.Equal(0.0149m, r.Valor.Faixa.TaxaMensal.Fracao);
    }

    [Fact]
    public async Task Cliente_que_revogou_o_consentimento_simula_como_anonimo()
    {
        var id = Guid.CreateVersion7();
        _clientes.Perfis[id] = new PerfilDeCreditoDto(id, 950, 1_200_000, ConsentimentoAnaliseDeCredito: false);

        var r = await CasoDeUso().ExecutarAsync(new SimularEmprestimoComando(10_000m, 12, "price", id), Ct);

        Assert.Equal("D", r.Valor.Faixa.Nome);
    }

    [Fact]
    public async Task Cliente_inexistente_e_404_e_nada_e_gravado_nem_publicado()
    {
        var r = await CasoDeUso().ExecutarAsync(new SimularEmprestimoComando(10_000m, 12, "price", Guid.CreateVersion7()), Ct);

        Assert.True(r.Falhou);
        Assert.Equal(SimulacaoErros.ClienteNaoEncontrado, r.Erro);
        Assert.Empty(_publicador.Publicadas);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("1")]
    [InlineData("americano")]
    public async Task Sistema_invalido_e_erro_de_validacao(string? sistema)
    {
        var r = await CasoDeUso().ExecutarAsync(new SimularEmprestimoComando(10_000m, 12, sistema, null), Ct);
        Assert.Equal("simulacao.sistema_invalido", r.Erro?.Codigo);
    }

    [Theory]
    [InlineData("PRICE")]
    [InlineData(" Sac ")]
    public async Task Sistema_ignora_maiusculas_e_espacos(string sistema)
    {
        var r = await CasoDeUso().ExecutarAsync(new SimularEmprestimoComando(10_000m, 12, sistema, null), Ct);
        Assert.True(r.Sucesso);
    }

    [Fact]
    public async Task Repositorio_em_memoria_descarta_as_mais_antigas_ao_passar_do_limite()
    {
        var repositorio = new SimulacaoRepositorioEmMemoria(capacidade: 3);
        var casoDeUso = new SimularEmprestimo(new TabelaFixa(), repositorio, _publicador, _clientes, _relogio);
        var ids = new List<SimulacaoId>();
        for (var i = 0; i < 5; i++)
            ids.Add((await casoDeUso.ExecutarAsync(new SimularEmprestimoComando(10_000m, 12, "price", null), Ct)).Valor.Id);

        Assert.Null(await repositorio.ObterAsync(ids[0], Ct));
        Assert.Null(await repositorio.ObterAsync(ids[1], Ct));
        Assert.NotNull(await repositorio.ObterAsync(ids[4], Ct));
    }
}
