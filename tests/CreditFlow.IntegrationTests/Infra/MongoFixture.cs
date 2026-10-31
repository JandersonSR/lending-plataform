using MongoDB.Driver;
using Testcontainers.MongoDb;

namespace CreditFlow.IntegrationTests.Infra;

public sealed class MongoFixture : IAsyncLifetime
{
    private readonly MongoDbContainer _container = new MongoDbBuilder().WithImage(Imagens.Mongo).Build();

    public IMongoDatabase Banco { get; private set; } = null!;

    public async ValueTask InitializeAsync()
    {
        await _container.StartAsync();
        Banco = new MongoClient(_container.GetConnectionString()).GetDatabase("creditflow_testes");
    }

    public async ValueTask DisposeAsync() => await _container.DisposeAsync();
}
