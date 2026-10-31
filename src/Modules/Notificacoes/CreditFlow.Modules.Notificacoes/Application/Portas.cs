using CreditFlow.Modules.Notificacoes.Contracts;
using CreditFlow.Modules.Notificacoes.Domain;

namespace CreditFlow.Modules.Notificacoes.Application;

public interface INotificacaoRepositorio
{
    /// <summary>
    /// Grava se ainda não existe uma notificação para (EventoId, Canal). Devolve a que ficou valendo:
    /// a nova, ou a que já estava lá (entrega repetida do mesmo evento).
    /// </summary>
    Task<(Notificacao Notificacao, bool Nova)> RegistrarAsync(Notificacao notificacao, CancellationToken ct);

    Task MarcarAsync(Guid id, StatusDaNotificacao status, string? erro, DateTimeOffset? enviadaEm, CancellationToken ct);

    Task<IReadOnlyList<Notificacao>> ListarDoClienteAsync(Guid clienteId, int limite, CancellationToken ct);
}

/// <summary>O "provedor" de e-mail/SMS. No CreditFlow é simulado: registra no log.</summary>
public interface IEnviadorDeNotificacoes
{
    Task EnviarAsync(Notificacao notificacao, CancellationToken ct);
}

internal static class Canais
{
    public static readonly CanalDeNotificacao[] Todos = [CanalDeNotificacao.Email, CanalDeNotificacao.Sms];
}
