using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;

namespace CreditFlow.BuildingBlocks.Mensageria;

/// <summary>
/// Liveness dos consumidores: um laço de consumo travado (deadlock, await que nunca volta) NÃO derruba o
/// processo; o pod continua "Running" e o lag cresce. Esta verificação olha o batimento do laço:
/// sem volta há mais de <see cref="LimiteSemBatimento"/>, o pod está doente e o kubelet o reinicia.
/// O limite fica acima do pior caso legítimo (30 s de retry em memória + novas tentativas internas do manipulador).
/// </summary>
internal sealed class VerificacaoDosConsumidores(IEnumerable<IHostedService> servicos, TimeProvider relogio) : IHealthCheck
{
    internal static readonly TimeSpan LimiteSemBatimento = TimeSpan.FromMinutes(2);
    internal static readonly TimeSpan CarenciaNaSubida = TimeSpan.FromMinutes(1);

    private readonly long _criadaEm = relogio.GetTimestamp();

    public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        var consumidores = servicos.OfType<ConsumidorKafka>().ToList();
        if (consumidores.Count == 0)
            return Task.FromResult(HealthCheckResult.Healthy("nenhum consumidor neste processo"));

        var dados = new Dictionary<string, object>();
        var doentes = new List<string>();
        foreach (var c in consumidores)
        {
            var tempo = c.TempoDesdeOUltimoBatimento;
            dados[c.Grupo] = tempo?.ToString("c") ?? "não iniciado";
            var naSubida = relogio.GetElapsedTime(_criadaEm) < CarenciaNaSubida;
            if ((tempo is null && !naSubida) || tempo > LimiteSemBatimento)
                doentes.Add(c.Grupo);
        }

        return Task.FromResult(doentes.Count == 0
            ? HealthCheckResult.Healthy("laços de consumo batendo", dados)
            : HealthCheckResult.Unhealthy($"laço parado: {string.Join(", ", doentes)}", data: dados));
    }
}
