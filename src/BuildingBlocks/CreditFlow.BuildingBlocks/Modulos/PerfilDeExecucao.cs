using Microsoft.Extensions.Configuration;

namespace CreditFlow.BuildingBlocks.Modulos;

/// <summary>Papel do processo: atender HTTP ou só processar mensagens.</summary>
public enum PapelDoProcesso
{
    Api,
    Worker,
}

/// <summary>
/// O que ESTE processo executa: quais módulos e em qual papel. A mesma imagem vira api-geral,
/// api-simulacao, worker-pagamentos ou api-pagamentos só mudando duas variáveis de ambiente:
/// <c>CREDITFLOW__MODULOS=Simulacao</c> e <c>CREDITFLOW__PAPEL=api</c>.
/// </summary>
public sealed record PerfilDeExecucao(IReadOnlySet<string> Modulos, PapelDoProcesso Papel)
{
    public const string Secao = "CreditFlow";

    public bool EhApi => Papel == PapelDoProcesso.Api;
    public bool EhWorker => Papel == PapelDoProcesso.Worker;

    public bool Executa(string nomeDoModulo) => Modulos.Contains(nomeDoModulo);

    /// <summary>
    /// Lê <c>CreditFlow:Modulos</c> (lista separada por vírgula, ou array no JSON) e <c>CreditFlow:Papel</c>.
    /// Sem lista, executa todos os módulos do catálogo (o modo "monolito" do desenvolvimento local).
    /// Nome desconhecido derruba a subida: um erro de digitação no Helm não pode virar um pod
    /// saudável que não atende nada.
    /// </summary>
    public static PerfilDeExecucao Ler(IConfiguration configuration, IReadOnlyCollection<IModule> catalogo)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(catalogo);

        var secao = configuration.GetSection(Secao);
        var conhecidos = catalogo.Select(m => m.Nome).ToHashSet(StringComparer.OrdinalIgnoreCase);

        var pedidos = LerLista(secao.GetSection("Modulos"));
        if (pedidos.Count == 0)
            pedidos = [.. conhecidos];

        var desconhecidos = pedidos.Where(p => !conhecidos.Contains(p)).ToList();
        if (desconhecidos.Count > 0)
            throw new InvalidOperationException(
                $"CreditFlow:Modulos tem módulo desconhecido: {string.Join(", ", desconhecidos)}. " +
                $"Conhecidos: {string.Join(", ", conhecidos.Order())}.");

        var textoPapel = secao["Papel"];
        PapelDoProcesso papel;
        if (string.IsNullOrWhiteSpace(textoPapel))
            papel = PapelDoProcesso.Api;
        else if (!Enum.TryParse(textoPapel, ignoreCase: true, out papel) || !Enum.IsDefined(papel))
            throw new InvalidOperationException($"CreditFlow:Papel inválido: '{textoPapel}'. Use 'api' ou 'worker'.");

        // Normaliza para o nome canônico do catálogo ("simulacao" vira "Simulacao").
        var canonicos = catalogo
            .Where(m => pedidos.Contains(m.Nome, StringComparer.OrdinalIgnoreCase))
            .Select(m => m.Nome)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        return new PerfilDeExecucao(canonicos, papel);
    }

    private static List<string> LerLista(IConfigurationSection secao)
    {
        // Array no JSON ("Modulos": ["Clientes", "Simulacao"]) vira Modulos:0, Modulos:1...
        var filhos = secao.GetChildren().Select(c => c.Value).Where(v => !string.IsNullOrWhiteSpace(v)).ToList();
        var texto = filhos.Count > 0 ? string.Join(',', filhos) : secao.Value ?? "";
        return texto
            .Split([',', ';', ' '], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public override string ToString() => $"{Papel.ToString().ToLowerInvariant()}: {string.Join(", ", Modulos.Order())}";
}
