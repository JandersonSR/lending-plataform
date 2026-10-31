using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MongoDB.Driver;

namespace CreditFlow.Modules.Simulacao.Infrastructure.Mongo;

/// <summary>
/// Cria os índices na subida. CreateMany é idempotente quando a definição é a mesma; se a definição
/// MUDAR com o mesmo nome (ex.: outro expireAfter), o servidor recusa (IndexOptionsConflict) e é preciso
/// um collMod ou drop/create: isso é uma "migração" de Mongo e vai num passo de deploy, não aqui.
/// </summary>
internal sealed partial class IndicesDeSimulacao(IMongoDatabase banco, IOptions<SimulacaoMongoOpcoes> opcoes, ILogger<IndicesDeSimulacao> log) : IHostedService
{
    public static IReadOnlyList<CreateIndexModel<SimulacaoDocumento>> Definicoes { get; } =
    [
        // TTL: um processo do mongod (a cada ~60 s) apaga documentos com expira_em no passado.
        // expireAfter = 0: a data de expiração é o próprio valor do campo (calculado ao gravar).
        new(Builders<SimulacaoDocumento>.IndexKeys.Ascending(d => d.ExpiraEm),
            new CreateIndexOptions { Name = "ttl_expira_em", ExpireAfter = TimeSpan.Zero }),

        // Histórico do cliente (igualdade em cliente_id, ordenação por criada_em). Parcial: simulação
        // anônima não tem cliente_id e não precisa ocupar o índice.
        new(Builders<SimulacaoDocumento>.IndexKeys.Ascending(d => d.ClienteId).Descending(d => d.CriadaEm),
            new CreateIndexOptions<SimulacaoDocumento>
            {
                Name = "ix_cliente_criada_em",
                PartialFilterExpression = Builders<SimulacaoDocumento>.Filter.Exists(d => d.ClienteId),
            }),
    ];

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        try
        {
            await banco.GetCollection<SimulacaoDocumento>(opcoes.Value.Colecao).Indexes
                .CreateManyAsync(Definicoes, cancellationToken);
            IndicesCriados(log);
        }
        catch (Exception ex) when (ex is MongoException or TimeoutException)
        {
            // Mongo fora na subida não pode impedir o pod de subir (a simulação continua calculando;
            // gravar o histórico falha até ele voltar). O índice é criado na próxima subida.
            IndicesFalharam(log, ex);
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    [LoggerMessage(Level = LogLevel.Information, Message = "Índices da coleção de simulações verificados.")]
    private static partial void IndicesCriados(ILogger logger);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Não foi possível criar os índices de simulações agora.")]
    private static partial void IndicesFalharam(ILogger logger, Exception ex);
}
