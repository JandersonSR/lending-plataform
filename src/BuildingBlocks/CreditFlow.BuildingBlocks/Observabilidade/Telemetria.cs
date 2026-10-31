using System.Diagnostics;
using System.Reflection;

namespace CreditFlow.BuildingBlocks.Observabilidade;

/// <summary>
/// Nomes das fontes de telemetria próprias do CreditFlow. Os módulos criam spans com
/// <see cref="Fonte"/> e registram métricas de negócio por <see cref="MetricasDeCredito"/>.
/// </summary>
public static class Telemetria
{
    /// <summary>Nome do <see cref="ActivitySource"/> e do <see cref="System.Diagnostics.Metrics.Meter"/> do domínio de crédito.</summary>
    public const string NomeDaFonte = "CreditFlow.Credito";

    /// <summary>Versão do assembly de entrada (o Host), gravada no build com -p:Version.</summary>
    public static readonly string Versao =
        Assembly.GetEntryAssembly()?.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
        ?? Assembly.GetEntryAssembly()?.GetName().Version?.ToString()
        ?? "0.0.0";

    /// <summary>
    /// Fonte de spans das jornadas de crédito (simular, propor, analisar, contratar, desembolsar).
    /// Uso: <c>using var span = Telemetria.Fonte.StartActivity("Simulacao.Calcular");</c>
    /// (devolve null quando ninguém está ouvindo: sempre use o operador ?.).
    /// </summary>
    public static readonly ActivitySource Fonte = new(NomeDaFonte, Versao);

    /// <summary>Atributos padronizados dos spans e métricas de crédito (baixa cardinalidade: nunca CPF, id ou valor exato).</summary>
    public static class Atributos
    {
        public const string Sistema = "creditflow.amortizacao.sistema";      // price | sac
        public const string FaixaPrazo = "creditflow.prazo.faixa";           // 6-12 | 13-24 | 25-36 | 37-48
        public const string FaixaValor = "creditflow.valor.faixa";           // ate-5k | 5k-20k | 20k-50k
        public const string Resultado = "creditflow.resultado";              // aprovada | recusada | concluido | falhou ...
        public const string Motivo = "creditflow.motivo";                     // código curto, nunca texto livre
        public const string PropostaId = "creditflow.proposta.id";            // só em span (alta cardinalidade), nunca em métrica
    }

    /// <summary>Faixa de prazo para usar como atributo de métrica (evita um valor por mês).</summary>
    public static string FaixaDePrazo(int meses) => meses switch
    {
        <= 12 => "6-12",
        <= 24 => "13-24",
        <= 36 => "25-36",
        _ => "37-48",
    };

    /// <summary>Faixa de valor em reais para atributo de métrica.</summary>
    public static string FaixaDeValor(decimal valor) => valor switch
    {
        <= 5_000m => "ate-5k",
        <= 20_000m => "5k-20k",
        _ => "20k-50k",
    };
}
