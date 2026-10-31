using NetArchTest.Rules;

namespace CreditFlow.ArchitectureTests;

/// <summary>
/// As regras do monolito modular, verificadas a cada build. Disciplina não escala; teste que quebra escala.
/// Duas camadas de verificação: os .csproj (o que o projeto PODE usar) e o IL compilado, via NetArchTest
/// (o que o código de fato USA). A primeira pega referência sem uso; a segunda pega dependência que
/// chegou por um caminho indireto.
/// </summary>
public sealed class FronteirasTests
{
    public static TheoryData<string> Modulos => new(Solucao.Modulos);

    [Fact]
    public void A_solucao_tem_os_modulos_esperados()
    {
        // Se este falhar, a descoberta parou de achar as DLLs e os outros testes passariam "vazios".
        Assert.Contains("Clientes", Solucao.Modulos);
        Assert.Contains("Simulacao", Solucao.Modulos);
    }

    [Fact]
    public void Csproj_de_modulo_so_referencia_BuildingBlocks_e_Contracts()
    {
        var violacoes = new List<string>();
        foreach (var csproj in Solucao.CsprojsDeModulos())
        {
            var projeto = Path.GetFileNameWithoutExtension(csproj.Name);
            var ehContrato = projeto.EndsWith(".Contracts", StringComparison.Ordinal);
            foreach (var referencia in Solucao.ReferenciasDeProjeto(csproj.FullName))
            {
                var permitida = referencia == "CreditFlow.BuildingBlocks"
                    // um módulo pode ver o Contracts de qualquer módulo (inclusive o seu); um Contracts não vê ninguém
                    || (!ehContrato && referencia.StartsWith(Solucao.PrefixoModulos, StringComparison.Ordinal)
                        && referencia.EndsWith(".Contracts", StringComparison.Ordinal));
                if (!permitida)
                    violacoes.Add($"{projeto} → {referencia}");
            }
        }
        Assert.True(violacoes.Count == 0, "Referências proibidas:\n" + string.Join('\n', violacoes));
    }

    [Theory]
    [MemberData(nameof(Modulos))]
    public void Modulo_nao_usa_tipos_internos_de_outro_modulo(string modulo)
    {
        var internosDosOutros = Solucao.Modulos
            .Where(m => m != modulo)
            .SelectMany(m => new[] { "Domain", "Application", "Infrastructure", "Api" }
                .Select(camada => $"{Solucao.PrefixoModulos}{m}.{camada}")
                .Append($"{Solucao.PrefixoModulos}{m}.{m}Module"))
            .ToArray();

        var resultado = Types.InAssembly(Solucao.AssemblyDoModulo(modulo))
            .ShouldNot().HaveDependencyOnAny(internosDosOutros)
            .GetResult();

        Assert.True(resultado.IsSuccessful, Falhas(resultado));
    }

    [Theory]
    [MemberData(nameof(Modulos))]
    public void Domain_nao_depende_de_infraestrutura_nem_de_framework(string modulo)
    {
        var proibidos = new[]
        {
            "Microsoft.EntityFrameworkCore", "Npgsql", "MongoDB", "StackExchange.Redis", "Confluent.Kafka",
            "Microsoft.AspNetCore", "Microsoft.Extensions.Caching", "Microsoft.Extensions.Hosting",
            "System.Net.Http", "OpenTelemetry",
            // Da Mensageria, o domínio pode conhecer os eventos (via .Contracts), nunca quem publica ou consome.
            "CreditFlow.BuildingBlocks.Mensageria.IPublicadorDeEventos", "CreditFlow.BuildingBlocks.Mensageria.MensagemOutbox",
            "CreditFlow.BuildingBlocks.Mensageria.IManipuladorDeEvento", "CreditFlow.BuildingBlocks.Auth",
            "CreditFlow.BuildingBlocks.Observabilidade", "CreditFlow.BuildingBlocks.Api",
            $"{Solucao.PrefixoModulos}{modulo}.Application",
            $"{Solucao.PrefixoModulos}{modulo}.Infrastructure",
            $"{Solucao.PrefixoModulos}{modulo}.Api",
        };

        var resultado = Types.InAssembly(Solucao.AssemblyDoModulo(modulo))
            .That().ResideInNamespace($"{Solucao.PrefixoModulos}{modulo}.Domain")
            .ShouldNot().HaveDependencyOnAny(proibidos)
            .GetResult();

        Assert.True(resultado.IsSuccessful, Falhas(resultado));
    }

    [Theory]
    [MemberData(nameof(Modulos))]
    public void Application_nao_depende_de_infraestrutura_concreta(string modulo)
    {
        var resultado = Types.InAssembly(Solucao.AssemblyDoModulo(modulo))
            .That().ResideInNamespace($"{Solucao.PrefixoModulos}{modulo}.Application")
            .ShouldNot().HaveDependencyOnAny(
                "Microsoft.EntityFrameworkCore", "Npgsql", "MongoDB", "StackExchange.Redis", "Confluent.Kafka",
                $"{Solucao.PrefixoModulos}{modulo}.Infrastructure", $"{Solucao.PrefixoModulos}{modulo}.Api")
            .GetResult();

        Assert.True(resultado.IsSuccessful, Falhas(resultado));
    }

    [Theory]
    [MemberData(nameof(Modulos))]
    public void Contracts_nao_expoe_tipos_internos_do_modulo(string modulo)
    {
        var contratos = Solucao.AssemblyDosContratos(modulo);
        if (contratos is null)
            return; // módulo sem contrato público (ainda)

        var resultado = Types.InAssembly(contratos)
            .ShouldNot().HaveDependencyOnAny(
                $"{Solucao.PrefixoModulos}{modulo}.Domain", $"{Solucao.PrefixoModulos}{modulo}.Application",
                $"{Solucao.PrefixoModulos}{modulo}.Infrastructure", "Microsoft.EntityFrameworkCore", "MongoDB")
            .GetResult();

        Assert.True(resultado.IsSuccessful, Falhas(resultado));
    }

    [Fact]
    public void BuildingBlocks_nao_conhece_nenhum_modulo()
    {
        var resultado = Types.InAssembly(Solucao.BuildingBlocks)
            .ShouldNot().HaveDependencyOn("CreditFlow.Modules")
            .GetResult();

        Assert.True(resultado.IsSuccessful, Falhas(resultado));
    }

    private static string Falhas(TestResult r) =>
        "Tipos que violam a regra: " + string.Join(", ", r.FailingTypeNames ?? Enumerable.Empty<string>());
}
