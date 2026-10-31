namespace CreditFlow.BuildingBlocks.Mensageria;

/// <summary>
/// Os tópicos do CreditFlow. Um tópico por módulo que publica (não um por tipo de evento):
/// a ordem só existe dentro de uma partição, e eventos do mesmo agregado (mesma chave) precisam
/// cair na mesma partição do mesmo tópico para chegar em ordem (PropostaAprovada antes de ContratoAssinado).
/// O sufixo .v1 é a versão do contrato do TÓPICO (formato do envelope/chave), não de cada evento.
/// </summary>
public static class Topicos
{
    public const string Propostas = "creditflow.propostas.v1";
    public const string Pagamentos = "creditflow.pagamentos.v1";
    public const string Simulacoes = "creditflow.simulacoes.v1";

    public static IReadOnlyList<string> Todos { get; } = [Propostas, Pagamentos, Simulacoes];

    /// <summary>Tópico de mensagens mortas de um grupo de consumidores: quem tem de corrigir é o dono do grupo.</summary>
    public static string DeMensagensMortas(string grupoDeConsumidores) => $"{grupoDeConsumidores}.dlt";
}
