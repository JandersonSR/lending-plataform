namespace CreditFlow.Modules.Simulacao.Infrastructure.Taxas;

/// <summary>Seção "Simulacao" do appsettings.</summary>
public sealed class SimulacaoOpcoes
{
    public const string Secao = "Simulacao";

    /// <summary>
    /// Faixas de score. Sem inicializador de propósito: o binder de configuração ACRESCENTA itens a uma
    /// lista já preenchida, então valores padrão aqui se somariam aos do appsettings. O padrão fica em
    /// <see cref="TabelaDeTaxasEmConfiguracao.Padrao"/>.
    /// </summary>
    public List<FaixaOpcao> Faixas { get; set; } = [];

    /// <summary>Requisições por minuto, por IP, no endpoint público de simulação.</summary>
    public int RequisicoesPorMinuto { get; set; } = 30;
}

public sealed class FaixaOpcao
{
    public string Nome { get; set; } = "";
    public int ScoreMinimo { get; set; }
    /// <summary>Em pontos percentuais: 1.99 = 1,99% ao mês.</summary>
    public decimal TaxaMensalPercentual { get; set; }
}
