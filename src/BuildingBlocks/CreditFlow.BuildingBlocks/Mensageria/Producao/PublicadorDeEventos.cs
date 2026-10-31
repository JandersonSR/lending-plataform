using System.Text;
using Confluent.Kafka;
using Microsoft.Extensions.Logging;

namespace CreditFlow.BuildingBlocks.Mensageria;

/// <summary>
/// Publicação DIRETA, sem outbox. Só para eventos cuja perda ocasional é aceitável e que não nascem
/// de uma transação de banco (ex.: SimulacaoRealizada, que é estatística). Evento que dispara
/// dinheiro, estado de proposta ou saga vai pela outbox (<see cref="OutboxDbContextExtensions.AdicionarAoOutbox{TEvento}"/>).
/// </summary>
public interface IPublicadorDeEventos
{
    /// <summary>Espera a confirmação do broker (acks=all). Lança <see cref="ProduceException{TKey,TValue}"/> se não entregar.</summary>
    Task PublicarAsync<TEvento>(TEvento evento, string chave, CancellationToken ct = default) where TEvento : class, IEventoIntegracao;

    /// <summary>Dispara e não espera: a falha de entrega só é registrada em log. Não segura a requisição HTTP.</summary>
    void PublicarSemEsperar<TEvento>(TEvento evento, string chave) where TEvento : class, IEventoIntegracao;
}

internal sealed partial class PublicadorDeEventos(IProducer<string, byte[]> produtor, ILogger<PublicadorDeEventos> log)
    : IPublicadorDeEventos
{
    public async Task PublicarAsync<TEvento>(TEvento evento, string chave, CancellationToken ct = default)
        where TEvento : class, IEventoIntegracao
    {
        var (topico, mensagem, atividade) = Montar(evento, chave);
        using (atividade)
            await produtor.ProduceAsync(topico, mensagem, ct);
    }

    public void PublicarSemEsperar<TEvento>(TEvento evento, string chave) where TEvento : class, IEventoIntegracao
    {
        var (topico, mensagem, atividade) = Montar(evento, chave);
        using (atividade)
        {
            produtor.Produce(topico, mensagem, relatorio =>
            {
                if (relatorio.Error.IsError)
                    LogFalhaSemEsperar(log, topico, chave, relatorio.Error.Reason);
            });
        }
    }

    private static (string Topico, Message<string, byte[]> Mensagem, System.Diagnostics.Activity? Atividade) Montar<TEvento>(
        TEvento evento, string chave) where TEvento : class, IEventoIntegracao
    {
        var descritor = DescritorDeEvento.De<TEvento>();
        var (traceParent, traceState) = Rastreamento.Capturar();
        var atividade = Rastreamento.IniciarPublicacao(descritor.Topico, chave, traceParent, traceState);
        var headers = new Headers();
        Cabecalhos.Definir(headers, Cabecalhos.EventoId, evento.EventoId.ToString());
        Cabecalhos.Definir(headers, Cabecalhos.Tipo, descritor.Tipo);
        Cabecalhos.Definir(headers, Cabecalhos.Versao, descritor.Versao.ToString(System.Globalization.CultureInfo.InvariantCulture));
        Cabecalhos.Definir(headers, Cabecalhos.OcorridoEm, evento.OcorridoEm.ToString("O"));
        Cabecalhos.Definir(headers, Cabecalhos.ContentType, "application/json");
        Rastreamento.Injetar(headers, atividade, traceParent, traceState);
        var corpo = Encoding.UTF8.GetBytes(SerializadorDeEventos.Serializar(evento, typeof(TEvento)));
        return (descritor.Topico, new Message<string, byte[]> { Key = chave, Value = corpo, Headers = headers }, atividade);
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Evento não entregue em {Topico} (chave {Chave}): {Motivo}")]
    private static partial void LogFalhaSemEsperar(ILogger log, string topico, string chave, string motivo);
}
