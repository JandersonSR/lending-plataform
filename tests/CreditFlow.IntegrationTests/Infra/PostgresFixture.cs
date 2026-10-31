using CreditFlow.Modules.Propostas.Infrastructure.Persistencia;
using Microsoft.EntityFrameworkCore;
using Testcontainers.PostgreSql;

namespace CreditFlow.IntegrationTests.Infra;

/// <summary>Um PostgreSQL por classe de teste, com o schema "propostas" aplicado pelo MESMO SQL do módulo.</summary>
public sealed class PostgresFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder()
        .WithImage(Imagens.Postgres)
        .WithDatabase("creditflow")
        .Build();

    public string Conexao => _container.GetConnectionString();

    public async ValueTask InitializeAsync()
    {
        await _container.StartAsync();
        var ddl = await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "Propostas", "001_schema_propostas.sql"));
        await using var db = NovoContexto();
        await db.Database.ExecuteSqlRawAsync(ddl);
    }

    internal PropostasDbContext NovoContexto() =>
        new(new DbContextOptionsBuilder<PropostasDbContext>().UseNpgsql(Conexao).Options);

    public async ValueTask DisposeAsync() => await _container.DisposeAsync();
}
