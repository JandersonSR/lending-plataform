using System.Reflection;
using System.Xml.Linq;

namespace CreditFlow.ArchitectureTests;

/// <summary>Descobre módulos, assemblies e .csproj da solução sem lista fixa: módulo novo entra nas regras sozinho.</summary>
internal static class Solucao
{
    public const string PrefixoModulos = "CreditFlow.Modules.";

    /// <summary>Nomes dos módulos ("Clientes", "Simulacao"...), a partir das DLLs CreditFlow.Modules.X.dll na saída.</summary>
    public static IReadOnlyList<string> Modulos { get; } = Directory
        .GetFiles(AppContext.BaseDirectory, PrefixoModulos + "*.dll")
        .Select(Path.GetFileNameWithoutExtension)
        .OfType<string>()
        .Where(n => !n.EndsWith(".Contracts", StringComparison.Ordinal) && !n.EndsWith(".Tests", StringComparison.Ordinal))
        .Select(n => n[PrefixoModulos.Length..])
        .Where(n => !n.Contains('.', StringComparison.Ordinal))
        .Order(StringComparer.Ordinal)
        .ToList();

    public static Assembly AssemblyDoModulo(string modulo) => Carregar(PrefixoModulos + modulo);

    public static Assembly? AssemblyDosContratos(string modulo) =>
        File.Exists(Path.Combine(AppContext.BaseDirectory, $"{PrefixoModulos}{modulo}.Contracts.dll"))
            ? Carregar($"{PrefixoModulos}{modulo}.Contracts")
            : null;

    public static Assembly BuildingBlocks => Carregar("CreditFlow.BuildingBlocks");

    private static Assembly Carregar(string nome) =>
        Assembly.Load(AssemblyName.GetAssemblyName(Path.Combine(AppContext.BaseDirectory, nome + ".dll")));

    /// <summary>Raiz do repositório: sobe as pastas a partir da saída do teste até achar CreditFlow.slnx.</summary>
    public static DirectoryInfo Raiz { get; } = AcharRaiz();

    private static DirectoryInfo AcharRaiz()
    {
        for (var d = new DirectoryInfo(AppContext.BaseDirectory); d is not null; d = d.Parent)
            if (File.Exists(Path.Combine(d.FullName, "CreditFlow.slnx")))
                return d;
        throw new InvalidOperationException("CreditFlow.slnx não encontrado acima de " + AppContext.BaseDirectory);
    }

    /// <summary>Nomes dos projetos referenciados por um .csproj (ProjectReference), sem caminho nem extensão.</summary>
    public static IReadOnlyList<string> ReferenciasDeProjeto(string caminhoCsproj) =>
        XDocument.Load(caminhoCsproj)
            .Descendants("ProjectReference")
            .Select(e => (string?)e.Attribute("Include"))
            .OfType<string>()
            .Select(i => Path.GetFileNameWithoutExtension(i.Replace('\\', Path.DirectorySeparatorChar)))
            .ToList();

    public static IEnumerable<FileInfo> CsprojsDeModulos() =>
        new DirectoryInfo(Path.Combine(Raiz.FullName, "src", "Modules")).EnumerateFiles("*.csproj", SearchOption.AllDirectories);
}
