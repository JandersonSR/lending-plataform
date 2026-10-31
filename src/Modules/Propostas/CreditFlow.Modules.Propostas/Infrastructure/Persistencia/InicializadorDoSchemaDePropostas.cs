using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace CreditFlow.Modules.Propostas.Infrastructure.Persistencia;

/// <summary>
/// Aplica o DDL de "propostas" na subida quando <c>Propostas:CriarSchemaAoIniciar</c> é true (só local).
/// Em produção, o Job de migrações do chart aplica com o usuário de DDL, antes do deploy.
/// </summary>
internal sealed partial class InicializadorDoSchemaDePropostas(IServiceScopeFactory escopos, ILogger<InicializadorDoSchemaDePropostas> log) : IHostedService
{
    public const string Recurso = "propostas.001_schema_propostas.sql";

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await using var stream = typeof(InicializadorDoSchemaDePropostas).Assembly.GetManifestResourceStream(Recurso)
            ?? throw new InvalidOperationException($"Recurso {Recurso} não encontrado no assembly.");
        using var leitor = new StreamReader(stream);
        var ddl = await leitor.ReadToEndAsync(cancellationToken);

        await using var escopo = escopos.CreateAsyncScope();
        var db = escopo.ServiceProvider.GetRequiredService<PropostasDbContext>();
        await db.Database.ExecuteSqlRawAsync(ddl, cancellationToken);
        SchemaAplicado(log);
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    [LoggerMessage(Level = LogLevel.Information, Message = "Schema 'propostas' verificado/aplicado.")]
    private static partial void SchemaAplicado(ILogger logger);
}
