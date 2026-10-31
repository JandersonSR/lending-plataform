using Microsoft.Extensions.Diagnostics.HealthChecks;
using MongoDB.Bson;
using MongoDB.Driver;

namespace CreditFlow.Modules.Notificacoes.Infrastructure.Mongo;

/// <summary>Readiness: o Mongo responde a um ping? (tag "ready": fora do ar = pod fora do balanceador, não reiniciado).</summary>
internal sealed class VerificacaoDoMongo(IMongoDatabase banco) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            using var limite = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            limite.CancelAfter(TimeSpan.FromSeconds(2));
            await banco.RunCommandAsync<BsonDocument>(new BsonDocument("ping", 1), cancellationToken: limite.Token);
            return HealthCheckResult.Healthy();
        }
        catch (Exception ex) when (ex is MongoException or TimeoutException or OperationCanceledException)
        {
            return HealthCheckResult.Unhealthy("MongoDB não respondeu ao ping.", ex);
        }
    }
}
