using CreditFlow.Modules.Notificacoes.Contracts;

namespace CreditFlow.Modules.Notificacoes.Domain;

public enum StatusDaNotificacao { Pendente, Enviada, Falhou }

/// <summary>
/// Um aviso a um cliente, num canal, causado por UM evento. A identidade de negócio é (EventoId, Canal):
/// o mesmo evento entregue duas vezes pelo Kafka não pode virar dois e-mails.
/// </summary>
public sealed class Notificacao
{
    public Guid Id { get; init; }
    public Guid EventoId { get; init; }
    public CanalDeNotificacao Canal { get; init; }
    public Guid ClienteId { get; init; }
    public Guid? PropostaId { get; init; }
    public string Tipo { get; init; } = "";
    public string Titulo { get; init; } = "";
    public string Mensagem { get; init; } = "";
    public StatusDaNotificacao Status { get; set; }
    public int Tentativas { get; set; }
    public string? UltimoErro { get; set; }
    public DateTimeOffset CriadaEm { get; init; }
    public DateTimeOffset? EnviadaEm { get; set; }

    public static Notificacao Nova(Guid eventoId, CanalDeNotificacao canal, Guid clienteId, Guid? propostaId,
        string tipo, string titulo, string mensagem, DateTimeOffset agora) => new()
    {
        Id = Guid.CreateVersion7(),
        EventoId = eventoId,
        Canal = canal,
        ClienteId = clienteId,
        PropostaId = propostaId,
        Tipo = tipo,
        Titulo = titulo,
        Mensagem = mensagem,
        Status = StatusDaNotificacao.Pendente,
        CriadaEm = agora,
    };
}
