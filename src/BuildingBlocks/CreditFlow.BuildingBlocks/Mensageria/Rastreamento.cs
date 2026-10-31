using System.Diagnostics;
using System.Diagnostics.Metrics;
using Confluent.Kafka;

namespace CreditFlow.BuildingBlocks.Mensageria;

/// <summary>
/// Rastreio W3C ponta a ponta: requisição HTTP → linha da outbox → publicação → consumo.
/// A outbox guarda o <c>traceparent</c> da requisição que criou o evento; o relay abre um span de
/// publicação filho dele e grava o próprio traceparent no cabeçalho Kafka; o consumidor abre o
/// span de processamento filho do de publicação. Resultado: um único trace no Jaeger/App Insights
/// atravessando o tempo que o evento passou parado na outbox e no tópico.
/// O OpenTelemetry precisa registrar <c>AddSource(Rastreamento.NomeDaFonte)</c> e <c>AddMeter(Rastreamento.NomeDaFonte)</c>.
/// </summary>
public static class Rastreamento
{
    public const string NomeDaFonte = "CreditFlow.Mensageria";

    public static readonly ActivitySource Fonte = new(NomeDaFonte);
    internal static readonly Meter Medidor = new(NomeDaFonte);

    internal static readonly Counter<long> OutboxPublicadas =
        Medidor.CreateCounter<long>("creditflow.mensageria.outbox.publicadas", description: "Mensagens da outbox publicadas no Kafka");
    internal static readonly Counter<long> OutboxFalhas =
        Medidor.CreateCounter<long>("creditflow.mensageria.outbox.falhas", description: "Falhas ao publicar mensagens da outbox");
    internal static readonly Counter<long> ConsumidorProcessadas =
        Medidor.CreateCounter<long>("creditflow.mensageria.consumidor.processadas", description: "Mensagens processadas pelo consumidor, por resultado");
    internal static readonly Counter<long> ConsumidorDlt =
        Medidor.CreateCounter<long>("creditflow.mensageria.consumidor.dlt", description: "Mensagens enviadas para a DLT");
    internal static readonly Histogram<double> ConsumidorDuracao =
        Medidor.CreateHistogram<double>("creditflow.mensageria.consumidor.duracao", unit: "ms", description: "Duração do processamento de uma mensagem");

    /// <summary>Captura o contexto atual (a requisição que está gravando na outbox).</summary>
    internal static (string? TraceParent, string? TraceState) Capturar()
    {
        var atual = Activity.Current;
        return atual is null || atual.IdFormat != ActivityIdFormat.W3C ? (null, null) : (atual.Id, atual.TraceStateString);
    }

    internal static Activity? IniciarPublicacao(string topico, string chave, string? traceParentOrigem, string? traceStateOrigem)
    {
        ActivityContext.TryParse(traceParentOrigem, traceStateOrigem, isRemote: true, out var pai);
        var atividade = Fonte.StartActivity($"{topico} publish", ActivityKind.Producer, pai);
        atividade?.SetTag("messaging.system", "kafka");
        atividade?.SetTag("messaging.operation.type", "send");
        atividade?.SetTag("messaging.destination.name", topico);
        atividade?.SetTag("messaging.kafka.message.key", chave);
        return atividade;
    }

    internal static Activity? IniciarProcessamento(ConsumeResult<string, byte[]> resultado, string grupo, string? tipo)
    {
        var headers = resultado.Message.Headers;
        ActivityContext.TryParse(Cabecalhos.Ler(headers, Cabecalhos.TraceParent), Cabecalhos.Ler(headers, Cabecalhos.TraceState),
            isRemote: true, out var pai);
        var atividade = Fonte.StartActivity($"{resultado.Topic} process", ActivityKind.Consumer, pai);
        atividade?.SetTag("messaging.system", "kafka");
        atividade?.SetTag("messaging.operation.type", "process");
        atividade?.SetTag("messaging.destination.name", resultado.Topic);
        atividade?.SetTag("messaging.consumer.group.name", grupo);
        atividade?.SetTag("messaging.destination.partition.id", resultado.Partition.Value.ToString(System.Globalization.CultureInfo.InvariantCulture));
        atividade?.SetTag("messaging.kafka.offset", resultado.Offset.Value);
        atividade?.SetTag("messaging.kafka.message.key", resultado.Message.Key);
        atividade?.SetTag("creditflow.evento.tipo", tipo);
        return atividade;
    }

    /// <summary>Escreve no cabeçalho o contexto do span de publicação (ou, sem listener, o da origem).</summary>
    internal static void Injetar(Headers headers, Activity? publicacao, string? traceParentOrigem, string? traceStateOrigem)
    {
        var traceParent = publicacao?.Id ?? traceParentOrigem;
        var traceState = publicacao is not null ? publicacao.TraceStateString : traceStateOrigem;
        Cabecalhos.Definir(headers, Cabecalhos.TraceParent, traceParent);
        Cabecalhos.Definir(headers, Cabecalhos.TraceState, traceState);
    }
}
