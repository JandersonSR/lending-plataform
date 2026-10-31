namespace CreditFlow.BuildingBlocks.Mensageria;

/// <summary>
/// Reage a um evento de integração. Resolvido num escopo de DI novo por mensagem (DbContext novo).
/// Precisa ser idempotente: a mesma mensagem PODE chegar mais de uma vez ("pelo menos uma vez").
/// Use <see cref="InboxDbContextExtensions.ProcessarUmaVezAsync"/> para o caso comum.
/// Exceções: <see cref="ErroPermanenteException"/> → DLT na hora; transitórias → nova tentativa sem fim;
/// as demais → novas tentativas e DLT depois de <see cref="ConsumidorOpcoes.MaxTentativasAntesDaDlt"/>.
/// </summary>
public interface IManipuladorDeEvento<in TEvento> where TEvento : class, IEventoIntegracao
{
    Task ManipularAsync(TEvento evento, ContextoDaMensagem contexto, CancellationToken ct);
}

/// <summary>De onde a mensagem veio e em que tentativa estamos. Imutável por tentativa.</summary>
public sealed record ContextoDaMensagem(
    string Consumidor,
    Guid EventoId,
    string Tipo,
    int Versao,
    string Topico,
    int Particao,
    long Offset,
    string? Chave,
    DateTimeOffset? OcorridoEm,
    int Tentativa);
