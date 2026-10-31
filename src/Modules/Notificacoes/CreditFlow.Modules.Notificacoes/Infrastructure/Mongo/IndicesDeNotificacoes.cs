using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using MongoDB.Driver;

namespace CreditFlow.Modules.Notificacoes.Infrastructure.Mongo;

internal sealed partial class IndicesDeNotificacoes(IMongoDatabase banco, ILogger<IndicesDeNotificacoes> log) : IHostedService
{
    public static IReadOnlyList<CreateIndexModel<NotificacaoDocumento>> Definicoes { get; } =
    [
        // A "inbox" do módulo: um aviso por evento e canal.
        new(Builders<NotificacaoDocumento>.IndexKeys.Ascending(d => d.EventoId).Ascending(d => d.Canal),
            new CreateIndexOptions { Name = "ux_evento_canal", Unique = true }),
        // Histórico do cliente: igualdade, depois ordenação (regra ESR: Equality, Sort, Range).
        new(Builders<NotificacaoDocumento>.IndexKeys.Ascending(d => d.ClienteId).Descending(d => d.CriadaEm),
            new CreateIndexOptions { Name = "ix_cliente_criada_em" }),
        // TTL relativo: apaga 180 dias depois de criada_em.
        new(Builders<NotificacaoDocumento>.IndexKeys.Ascending(d => d.CriadaEm),
            new CreateIndexOptions { Name = "ttl_criada_em", ExpireAfter = TimeSpan.FromDays(180) }),
    ];

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        try
        {
            await banco.GetCollection<NotificacaoDocumento>(NotificacaoRepositorioMongo.NomeDaColecao).Indexes
                .CreateManyAsync(Definicoes, cancellationToken);
            Criados(log);
        }
        catch (Exception ex) when (ex is MongoException or TimeoutException)
        {
            Falharam(log, ex);
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    [LoggerMessage(Level = LogLevel.Information, Message = "Índices de notificações verificados.")]
    private static partial void Criados(ILogger logger);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Não foi possível criar os índices de notificações agora.")]
    private static partial void Falharam(ILogger logger, Exception ex);
}
