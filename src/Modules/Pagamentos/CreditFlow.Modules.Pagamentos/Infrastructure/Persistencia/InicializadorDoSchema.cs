using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace CreditFlow.Modules.Pagamentos.Infrastructure.Persistencia;

/// <summary>
/// Aplica o DDL do schema "pagamentos" na subida, só com <c>Pagamentos:CriarSchemaAoIniciar=true</c>
/// (ambiente local). Mesma convenção do módulo Clientes. Em produção, o Job de migrações aplica.
/// Registrado ANTES do relay e do consumidor: hosted services sobem na ordem de registro.
/// </summary>
internal sealed partial class InicializadorDoSchema(IServiceScopeFactory escopos, ILogger<InicializadorDoSchema> log) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await using var stream = typeof(InicializadorDoSchema).Assembly
            .GetManifestResourceStream("pagamentos.001_schema_pagamentos.sql")
            ?? throw new InvalidOperationException("Recurso pagamentos.001_schema_pagamentos.sql não encontrado no assembly.");
        using var leitor = new StreamReader(stream);
        var ddl = await leitor.ReadToEndAsync(cancellationToken);

        await using var escopo = escopos.CreateAsyncScope();
        var db = escopo.ServiceProvider.GetRequiredService<PagamentosDbContext>();
        await db.Database.ExecuteSqlRawAsync(ddl, cancellationToken);
        SchemaAplicado(log);
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    [LoggerMessage(Level = LogLevel.Information, Message = "Schema 'pagamentos' verificado/aplicado.")]
    private static partial void SchemaAplicado(ILogger logger);
}
