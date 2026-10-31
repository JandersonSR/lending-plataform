namespace CreditFlow.Modules.Pagamentos.Infrastructure.Idempotencia;

/// <summary>
/// Uma requisição HTTP com <c>Idempotency-Key</c> já respondida. Repetir a chave com o mesmo corpo
/// devolve a mesma resposta sem refazer nada; com corpo diferente, 422 (o cliente reaproveitou a chave por engano).
/// </summary>
public sealed class RequisicaoIdempotente
{
    public string Chave { get; init; } = "";
    public string HashCorpo { get; init; } = "";
    public int StatusHttp { get; init; }
    public string Resposta { get; init; } = "{}";
    public DateTimeOffset CriadaEm { get; init; }
}
