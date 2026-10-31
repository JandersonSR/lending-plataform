using CreditFlow.Modules.Notificacoes.Application;
using CreditFlow.Modules.Notificacoes.Contracts;
using CreditFlow.Modules.Notificacoes.Domain;
using MongoDB.Driver;

namespace CreditFlow.Modules.Notificacoes.Infrastructure.Mongo;

internal sealed class NotificacaoRepositorioMongo(IMongoDatabase banco) : INotificacaoRepositorio
{
    public const string NomeDaColecao = "notificacoes";

    internal IMongoCollection<NotificacaoDocumento> Colecao { get; } = banco.GetCollection<NotificacaoDocumento>(NomeDaColecao);

    /// <summary>
    /// Idempotência sem tabela de inbox: o índice ÚNICO (evento_id, canal) é a inbox. Duas réplicas com o
    /// mesmo evento: uma insere, a outra recebe E11000 (duplicate key) e lê a que já existe.
    /// </summary>
    public async Task<(Notificacao Notificacao, bool Nova)> RegistrarAsync(Notificacao notificacao, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(notificacao);
        try
        {
            await Colecao.InsertOneAsync(ParaDocumento(notificacao), options: null, ct);
            return (notificacao, true);
        }
        catch (MongoWriteException ex) when (ex.WriteError?.Category == ServerErrorCategory.DuplicateKey)
        {
            var canal = notificacao.Canal.ToString();
            var existente = await Colecao.Find(d => d.EventoId == notificacao.EventoId && d.Canal == canal).FirstAsync(ct);
            return (ParaDominio(existente), false);
        }
    }

    public Task MarcarAsync(Guid id, StatusDaNotificacao status, string? erro, DateTimeOffset? enviadaEm, CancellationToken ct)
    {
        // Atualização parcial e atômica no servidor ($set/$inc): não lê para depois gravar.
        var atualizacao = Builders<NotificacaoDocumento>.Update
            .Set(d => d.Status, status.ToString())
            .Inc(d => d.Tentativas, 1)
            .Set(d => d.UltimoErro, erro);
        if (enviadaEm is { } quando)
            atualizacao = atualizacao.Set(d => d.EnviadaEm, quando.UtcDateTime);
        return Colecao.UpdateOneAsync(d => d.Id == id, atualizacao, options: null, ct);
    }

    public async Task<IReadOnlyList<Notificacao>> ListarDoClienteAsync(Guid clienteId, int limite, CancellationToken ct)
    {
        var documentos = await Colecao.Find(d => d.ClienteId == clienteId)
            .SortByDescending(d => d.CriadaEm)
            .Limit(Math.Clamp(limite, 1, 200))
            .ToListAsync(ct);
        return [.. documentos.Select(ParaDominio)];
    }

    internal static NotificacaoDocumento ParaDocumento(Notificacao n) => new()
    {
        Id = n.Id,
        EventoId = n.EventoId,
        Canal = n.Canal.ToString(),
        ClienteId = n.ClienteId,
        PropostaId = n.PropostaId,
        Tipo = n.Tipo,
        Titulo = n.Titulo,
        Mensagem = n.Mensagem,
        Status = n.Status.ToString(),
        Tentativas = n.Tentativas,
        UltimoErro = n.UltimoErro,
        CriadaEm = n.CriadaEm.UtcDateTime,
        EnviadaEm = n.EnviadaEm?.UtcDateTime,
    };

    internal static Notificacao ParaDominio(NotificacaoDocumento d) => new()
    {
        Id = d.Id,
        EventoId = d.EventoId,
        Canal = Enum.Parse<CanalDeNotificacao>(d.Canal),
        ClienteId = d.ClienteId,
        PropostaId = d.PropostaId,
        Tipo = d.Tipo,
        Titulo = d.Titulo,
        Mensagem = d.Mensagem,
        Status = Enum.Parse<StatusDaNotificacao>(d.Status),
        Tentativas = d.Tentativas,
        UltimoErro = d.UltimoErro,
        CriadaEm = new DateTimeOffset(DateTime.SpecifyKind(d.CriadaEm, DateTimeKind.Utc)),
        EnviadaEm = d.EnviadaEm is { } e ? new DateTimeOffset(DateTime.SpecifyKind(e, DateTimeKind.Utc)) : null,
    };
}
