using CreditFlow.BuildingBlocks.Modulos;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace CreditFlow.Modules.Clientes.Infrastructure.Postgres;

/// <summary>Aplica o DDL idempotente do schema "clientes" (recurso embutido clientes.001_schema_clientes.sql).</summary>
internal sealed partial class MigracaoDoSchemaClientes(IServiceScopeFactory escopos, ILogger<MigracaoDoSchemaClientes> log) : IMigracao
{
    public string Nome => "clientes: schema";

    public async Task AplicarAsync(CancellationToken ct)
    {
        await using var stream = typeof(MigracaoDoSchemaClientes).Assembly
            .GetManifestResourceStream("clientes.001_schema_clientes.sql")
            ?? throw new InvalidOperationException("Recurso clientes.001_schema_clientes.sql não encontrado no assembly.");
        using var leitor = new StreamReader(stream);
        var ddl = await leitor.ReadToEndAsync(ct);

        await using var escopo = escopos.CreateAsyncScope();
        var db = escopo.ServiceProvider.GetRequiredService<ClientesDbContext>();
        await db.Database.ExecuteSqlRawAsync(ddl, ct);
        SchemaAplicado(log);
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Schema 'clientes' verificado/aplicado.")]
    private static partial void SchemaAplicado(ILogger logger);
}

/// <summary>
/// Desenvolvimento local (<c>Clientes:CriarSchemaAoIniciar=true</c>): aplica a migração na subida do processo.
/// Em produção fica false: quem aplica é o Job de migração (<c>migrar</c>), antes do deploy.
/// </summary>
internal sealed class InicializadorDoSchema(MigracaoDoSchemaClientes migracao) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken) => migracao.AplicarAsync(cancellationToken);
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
