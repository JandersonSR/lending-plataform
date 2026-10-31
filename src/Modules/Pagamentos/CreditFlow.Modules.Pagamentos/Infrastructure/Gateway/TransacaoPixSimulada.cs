namespace CreditFlow.Modules.Pagamentos.Infrastructure.Gateway;

/// <summary>O "banco do PSP": o que o gateway lembra de cada chave de idempotência. Tabela pagamentos.pix_simulado.</summary>
public sealed class TransacaoPixSimulada
{
    public const string Processada = "processada";
    public const string Cancelada = "cancelada";

    public string ChaveIdempotencia { get; init; } = "";
    public string Situacao { get; init; } = Processada;
    public string? IdTransacao { get; init; }
    public string ChavePix { get; init; } = "";
    public long ValorCentavos { get; init; }
    public DateTimeOffset CriadaEm { get; init; }
}
