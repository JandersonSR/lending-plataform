using CreditFlow.BuildingBlocks;
using CreditFlow.BuildingBlocks.ValueObjects;
using CreditFlow.Modules.Clientes.Application;
using CreditFlow.Modules.Clientes.Domain;
using CreditFlow.Modules.Clientes.Infrastructure.Memoria;

namespace CreditFlow.Modules.Clientes.Tests;

internal sealed class RelogioFixo(DateTimeOffset agora) : TimeProvider
{
    public override DateTimeOffset GetUtcNow() => agora;
}

public sealed class CpfTests
{
    [Theory]
    [InlineData("529.982.247-25")]
    [InlineData("52998224725")]
    [InlineData("123.456.789-09")]
    [InlineData(" 987.654.321-00 ")]
    public void Aceita_cpf_valido_com_ou_sem_pontuacao(string texto)
    {
        var cpf = Cpf.Criar(texto);
        Assert.True(cpf.Sucesso);
        Assert.Equal(11, cpf.Valor.Digitos.Length);
    }

    [Theory]
    [InlineData(null, "cpf.obrigatorio")]
    [InlineData("", "cpf.obrigatorio")]
    [InlineData("529.982.247-2", "cpf.formato")]
    [InlineData("529.982.247-2a", "cpf.formato")]
    [InlineData("529.982.247-26", "cpf.invalido")]   // dígito verificador errado
    [InlineData("111.111.111-11", "cpf.invalido")]   // passa na conta, mas não é CPF
    public void Recusa_cpf_invalido_com_codigo_estavel(string? texto, string codigo) =>
        Assert.Equal(codigo, Cpf.Criar(texto).Erro?.Codigo);

    [Fact]
    public void ToString_mascara_para_nao_vazar_em_log()
    {
        var cpf = Cpf.Criar("529.982.247-25").Valor;
        Assert.Equal("***.982.247-**", cpf.ToString());
        Assert.Equal("***.982.247-**", $"{cpf}");
        Assert.Equal("529.982.247-25", cpf.Formatado);
    }
}

public sealed class ClienteTests
{
    private static readonly DateTimeOffset Agora = new(2026, 10, 30, 12, 0, 0, TimeSpan.Zero);
    private static readonly Cpf CpfValido = Cpf.Criar("52998224725").Valor;

    private static Result<Cliente> Cadastrar(string? nome = "Maria Silva", decimal renda = 4_000m, bool consente = true,
        string? termo = "2026-10", bool ofertas = false) =>
        Cliente.Cadastrar(nome, CpfValido, renda, consente, termo, ofertas, Agora);

    [Fact]
    public void Cadastro_valido_nasce_com_consentimento_ativo_para_analise()
    {
        var c = Cadastrar().Valor;
        Assert.True(c.PodeUsarParaAnaliseDeCredito);
        Assert.Single(c.Consentimentos);
        Assert.Equal(Agora, c.Consentimentos[0].ConcedidoEm);
    }

    [Fact]
    public void Sem_consentimento_para_analise_nao_cadastra() =>
        Assert.Equal(ClientesErros.ConsentimentoObrigatorio, Cadastrar(consente: false).Erro);

    [Theory]
    [InlineData(null)]
    [InlineData(" ")]
    [InlineData("A")]
    public void Nome_obrigatorio(string? nome) =>
        Assert.Equal(ClientesErros.NomeObrigatorio, Cadastrar(nome: nome).Erro);

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(1000.001)]
    public void Renda_invalida(double renda) =>
        Assert.Equal(ClientesErros.RendaInvalida, Cadastrar(renda: (decimal)renda).Erro);

    [Fact]
    public void Revogar_mantem_o_historico_e_e_idempotente()
    {
        var c = Cadastrar().Valor;
        c.Revogar(FinalidadeDoConsentimento.AnaliseDeCredito, Agora.AddDays(1));
        c.Revogar(FinalidadeDoConsentimento.AnaliseDeCredito, Agora.AddDays(2));

        Assert.False(c.PodeUsarParaAnaliseDeCredito);
        var unico = Assert.Single(c.Consentimentos);
        Assert.Equal(Agora.AddDays(1), unico.RevogadoEm); // a segunda revogação não reescreve a data
    }

    [Fact]
    public void Consentir_de_novo_depois_de_revogar_cria_um_registro_novo()
    {
        var c = Cadastrar().Valor;
        c.Revogar(FinalidadeDoConsentimento.AnaliseDeCredito, Agora.AddDays(1));
        c.Consentir(FinalidadeDoConsentimento.AnaliseDeCredito, "2026-11", Agora.AddDays(2));
        c.Consentir(FinalidadeDoConsentimento.AnaliseDeCredito, "2026-11", Agora.AddDays(3)); // idempotente

        Assert.True(c.PodeUsarParaAnaliseDeCredito);
        Assert.Equal(2, c.Consentimentos.Count);
    }

    [Theory]
    // valores conferidos com o script Python da lição (mesma fórmula)
    [InlineData("52998224725", 4_000, 479)]
    [InlineData("52998224725", 12_000, 599)]
    [InlineData("12345678909", 1_000, 648)]
    [InlineData("98765432100", 12_000, 867)]
    [InlineData("39053344705", 1_000, 381)]
    public void Score_ficticio_e_deterministico(string cpf, int renda, int esperado) =>
        Assert.Equal(esperado, ScoreFicticio.Calcular(Cpf.Criar(cpf).Valor, new Dinheiro(renda)));
}

public sealed class CadastrarClienteTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private readonly ClienteRepositorioEmMemoria _repositorio = new();
    private readonly RelogioFixo _relogio = new(new DateTimeOffset(2026, 10, 30, 12, 0, 0, TimeSpan.Zero));

    private static CadastrarClienteComando Comando(string cpf = "529.982.247-25") =>
        new("Maria Silva", cpf, 4_000m, AceitaAnaliseDeCredito: true, VersaoDoTermo: "2026-10", AceitaOfertasPorEmail: false);

    [Fact]
    public async Task CPF_repetido_e_conflito()
    {
        var casoDeUso = new CadastrarCliente(_repositorio, _relogio);
        Assert.True((await casoDeUso.ExecutarAsync(Comando(), Ct)).Sucesso);

        var segundo = await casoDeUso.ExecutarAsync(Comando("52998224725"), Ct); // mesmo CPF sem pontuação
        Assert.Equal(TipoErro.Conflito, segundo.Erro?.Tipo);
    }

    [Fact]
    public async Task Um_login_tem_um_cadastro_so_e_o_vinculo_e_consultavel()
    {
        var casoDeUso = new CadastrarCliente(_repositorio, _relogio);
        var primeiro = await casoDeUso.ExecutarAsync(Comando() with { UsuarioId = "sub-123" }, Ct);
        var outroCpfMesmoLogin = await casoDeUso.ExecutarAsync(Comando("123.456.789-09") with { UsuarioId = "sub-123" }, Ct);

        Assert.True(primeiro.Sucesso);
        Assert.Equal(ClientesErros.UsuarioJaCadastrado, outroCpfMesmoLogin.Erro);
        var perfil = await new ConsultaDeClientes(_repositorio).ObterPerfilPorUsuarioAsync("sub-123", Ct);
        Assert.Equal(primeiro.Valor.Id.Valor, perfil?.ClienteId);
    }

    [Fact]
    public async Task Cinquenta_cadastros_simultaneos_do_mesmo_CPF_gravam_um_so()
    {
        var casoDeUso = new CadastrarCliente(_repositorio, _relogio);
        var tarefas = Enumerable.Range(0, 50).Select(_ => Task.Run(() => casoDeUso.ExecutarAsync(Comando(), Ct), Ct));
        var resultados = await Task.WhenAll(tarefas);

        Assert.Equal(1, resultados.Count(r => r.Sucesso));
        Assert.All(resultados.Where(r => r.Falhou), r => Assert.Equal(ClientesErros.CpfJaCadastrado, r.Erro));
    }

    [Fact]
    public async Task Contrato_publico_reflete_a_revogacao()
    {
        var cliente = (await new CadastrarCliente(_repositorio, _relogio).ExecutarAsync(Comando(), Ct)).Valor;
        var consulta = new ConsultaDeClientes(_repositorio);
        Assert.True((await consulta.ObterPerfilDeCreditoAsync(cliente.Id.Valor, Ct))!.ConsentimentoAnaliseDeCredito);

        var revogar = await new RevogarConsentimento(_repositorio, _relogio).ExecutarAsync(cliente.Id.Valor, "analisedecredito", Ct);
        Assert.True(revogar.Sucesso);

        var perfil = await consulta.ObterPerfilDeCreditoAsync(cliente.Id.Valor, Ct);
        Assert.NotNull(perfil);
        Assert.False(perfil.ConsentimentoAnaliseDeCredito);
        Assert.Equal(400_000, perfil.RendaMensalCentavos);
    }

    [Theory]
    [InlineData("1")]
    [InlineData("Marketing")]
    [InlineData(null)]
    public async Task Finalidade_desconhecida_e_erro_de_validacao(string? finalidade)
    {
        var r = await new RevogarConsentimento(_repositorio, _relogio).ExecutarAsync(Guid.CreateVersion7(), finalidade, Ct);
        Assert.Equal(ClientesErros.FinalidadeInvalida, r.Erro);
    }

    [Fact]
    public async Task Consulta_de_cliente_inexistente_devolve_null()
    {
        var consulta = new ConsultaDeClientes(_repositorio);
        Assert.Null(await consulta.ObterPerfilDeCreditoAsync(Guid.CreateVersion7(), Ct));
    }
}
