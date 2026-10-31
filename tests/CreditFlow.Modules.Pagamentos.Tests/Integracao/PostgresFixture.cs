using CreditFlow.Modules.Pagamentos.Infrastructure.Persistencia;
using Microsoft.EntityFrameworkCore;
using Testcontainers.PostgreSql;

namespace CreditFlow.Modules.Pagamentos.Tests.Integracao;

/// <summary>Um Postgres 16 de verdade por classe de teste (precisa de Docker). Aplica o mesmo DDL embutido da produção.</summary>
public sealed class PostgresFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder().WithImage("postgres:16-alpine").Build();

    public async ValueTask InitializeAsync()
    {
        await _container.StartAsync();
        await using var stream = typeof(PagamentosDbContext).Assembly.GetManifestResourceStream("pagamentos.001_schema_pagamentos.sql")!;
        using var leitor = new StreamReader(stream);
        await using var db = NovoContexto();
        await db.Database.ExecuteSqlRawAsync(await leitor.ReadToEndAsync());
    }

    public PagamentosDbContext NovoContexto() =>
        new(new DbContextOptionsBuilder<PagamentosDbContext>().UseNpgsql(_container.GetConnectionString()).Options);

    public async ValueTask DisposeAsync() => await _container.DisposeAsync();
}
