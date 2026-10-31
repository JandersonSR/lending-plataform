using CreditFlow.Modules.Notificacoes.Contracts;
using CreditFlow.Modules.Notificacoes.Domain;

namespace CreditFlow.Modules.Notificacoes.Application;

/// <summary>
/// O fluxo de um aviso: registrar (idempotente) → enviar → marcar. Ordem e falhas pensadas para
/// "pelo menos uma vez" do Kafka:
/// - Evento repetido e aviso já Enviado: não envia de novo.
/// - Evento repetido e aviso ainda Pendente (o pod morreu entre registrar e enviar): envia agora.
///   O cliente pode, no pior caso, receber 2 vezes; é o preço de nunca deixar de receber.
/// </summary>
public sealed class Notificar(INotificacaoRepositorio repositorio, IEnviadorDeNotificacoes enviador, TimeProvider relogio)
{
    public async Task ExecutarAsync(Guid eventoId, Guid clienteId, Guid? propostaId, string tipo, string titulo, string mensagem, CancellationToken ct)
    {
        foreach (var canal in Canais.Todos)
        {
            var (notificacao, _) = await repositorio.RegistrarAsync(
                Notificacao.Nova(eventoId, canal, clienteId, propostaId, tipo, titulo, mensagem, relogio.GetUtcNow()), ct);
            if (notificacao.Status == StatusDaNotificacao.Enviada)
                continue;

            try
            {
                await enviador.EnviarAsync(notificacao, ct);
                await repositorio.MarcarAsync(notificacao.Id, StatusDaNotificacao.Enviada, null, relogio.GetUtcNow(), ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                await repositorio.MarcarAsync(notificacao.Id, StatusDaNotificacao.Falhou, ex.Message, null, ct);
                throw; // o consumidor decide: nova tentativa ou DLT
            }
        }
    }
}

public sealed class ListarNotificacoes(INotificacaoRepositorio repositorio)
{
    public Task<IReadOnlyList<Notificacao>> DoClienteAsync(Guid clienteId, CancellationToken ct) =>
        repositorio.ListarDoClienteAsync(clienteId, 50, ct);
}
