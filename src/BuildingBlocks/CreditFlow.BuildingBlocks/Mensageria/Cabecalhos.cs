using System.Text;
using Confluent.Kafka;

namespace CreditFlow.BuildingBlocks.Mensageria;

/// <summary>
/// Nomes dos cabeçalhos das mensagens. Os metadados do evento vão nos cabeçalhos (e não num envelope
/// dentro do JSON) para o consumidor decidir se quer a mensagem SEM desserializar o corpo, e para
/// ferramentas como o Kafka UI filtrarem por tipo. <c>traceparent</c>/<c>tracestate</c> seguem o W3C Trace Context.
/// </summary>
public static class Cabecalhos
{
    public const string EventoId = "cf-evento-id";
    public const string Tipo = "cf-tipo";
    public const string Versao = "cf-versao";
    public const string OcorridoEm = "cf-ocorrido-em";
    public const string ContentType = "content-type";
    public const string TraceParent = "traceparent";
    public const string TraceState = "tracestate";

    // Acrescentados quando a mensagem vai para a DLT
    public const string DltMotivo = "cf-dlt-motivo";
    public const string DltExcecao = "cf-dlt-excecao";
    public const string DltMensagem = "cf-dlt-mensagem";
    public const string DltTopicoOrigem = "cf-dlt-topico-origem";
    public const string DltParticaoOrigem = "cf-dlt-particao-origem";
    public const string DltOffsetOrigem = "cf-dlt-offset-origem";
    public const string DltTentativas = "cf-dlt-tentativas";
    public const string DltEm = "cf-dlt-em";

    internal static string? Ler(Headers? headers, string nome) =>
        headers is not null && headers.TryGetLastBytes(nome, out var bytes) ? Encoding.UTF8.GetString(bytes) : null;

    internal static void Definir(Headers headers, string nome, string? valor)
    {
        headers.Remove(nome);
        if (valor is not null)
            headers.Add(nome, Encoding.UTF8.GetBytes(valor));
    }
}
