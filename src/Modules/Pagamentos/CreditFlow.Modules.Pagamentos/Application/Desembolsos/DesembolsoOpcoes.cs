namespace CreditFlow.Modules.Pagamentos.Application.Desembolsos;

/// <summary>Seção <c>Pagamentos:Desembolso</c>: a política de novas tentativas contra o gateway.</summary>
public sealed class DesembolsoOpcoes
{
    public const string Secao = "Pagamentos:Desembolso";

    /// <summary>
    /// Total de tentativas no gateway (contadas no banco, sobrevivem a reentregas e trocas de pod).
    /// Esgotou: consulta/cancela a chave e, se nada saiu, falha o desembolso (Propostas compensa).
    /// É decisão de NEGÓCIO: quanto tempo o cliente espera o dinheiro antes de a contratação ser desfeita.
    /// </summary>
    public int MaxTentativasNoGateway { get; set; } = 6;

    public TimeSpan EsperaInicial { get; set; } = TimeSpan.FromMilliseconds(500);
    public TimeSpan EsperaMaxima { get; set; } = TimeSpan.FromSeconds(8);
}
