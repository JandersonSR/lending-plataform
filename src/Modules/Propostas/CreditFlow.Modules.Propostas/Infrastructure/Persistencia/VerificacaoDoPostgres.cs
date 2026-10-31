using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace CreditFlow.Modules.Propostas.Infrastructure.Persistencia;

/// <summary>Readiness (tag "ready"): o Postgres responde? Banco fora tira o pod do balanceador; reiniciar não resolve.</summary>
internal sealed class VerificacaoDoPostgres(PropostasDbContext db) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        using var limite = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        limite.CancelAfter(TimeSpan.FromSeconds(2));
        try
        {
            return await db.Database.CanConnectAsync(limite.Token)
                ? HealthCheckResult.Healthy()
                : HealthCheckResult.Unhealthy("PostgreSQL inacessível.");
        }
        catch (OperationCanceledException ex)
        {
            return HealthCheckResult.Unhealthy("PostgreSQL não respondeu em 2 s.", ex);
        }
    }
}
