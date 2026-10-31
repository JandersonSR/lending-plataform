namespace CreditFlow.Modules.Simulacao.Contracts;

/// <summary>Leitura de uma simulação por outro módulo (Propostas cria a proposta a partir dela).</summary>
public interface IConsultaDeSimulacoes
{
    /// <summary>A simulação, ou null se não existe.</summary>
    Task<SimulacaoDto?> ObterAsync(Guid simulacaoId, CancellationToken ct = default);
}

/// <summary>
/// Simulação como contrato público. Dinheiro em CENTAVOS (long), taxas como FRAÇÃO (0,0199 = 1,99%).
/// Nada de double, nada de valor já formatado: quem consome decide como apresentar.
/// </summary>
public sealed record SimulacaoDto(
    Guid Id,
    Guid? ClienteId,
    long ValorCentavos,
    int PrazoMeses,
    string Sistema,
    string FaixaDeScore,
    decimal TaxaMensal,
    long IofCentavos,
    long ValorLiberadoCentavos,
    long TotalJurosCentavos,
    long TotalPagoCentavos,
    decimal CetMensal,
    decimal CetAnual,
    DateOnly DataContratacao,
    DateTimeOffset CriadaEm,
    DateTimeOffset ValidaAte,
    IReadOnlyList<ParcelaDto> Parcelas);

public sealed record ParcelaDto(
    int Numero,
    DateOnly Vencimento,
    long ValorCentavos,
    long JurosCentavos,
    long AmortizacaoCentavos,
    long SaldoDevedorCentavos);
