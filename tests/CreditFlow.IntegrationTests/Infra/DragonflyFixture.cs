using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;

namespace CreditFlow.IntegrationTests.Infra;

/// <summary>
/// Dragonfly num contêiner genérico (não há módulo pronto). Fala o protocolo do Redis na 6379.
/// Os testes também PARAM o contêiner, para provar que o cache fora não derruba ninguém.
/// </summary>
public sealed class DragonflyFixture : IAsyncLifetime
{
    private readonly IContainer _container = new ContainerBuilder()
        .WithImage(Imagens.Dragonfly)
        .WithCommand("--proactor_threads=2", "--maxmemory=256mb", "--version_check=false")
        .WithPortBinding(6379, assignRandomHostPort: true)
        .WithWaitStrategy(Wait.ForUnixContainer().UntilExternalTcpPortIsAvailable(6379))
        .Build();

    public string Conexao => $"{_container.Hostname}:{_container.GetMappedPublicPort(6379)}";

    public Task PararAsync() => _container.StopAsync();

    public ValueTask InitializeAsync() => new(_container.StartAsync());

    public async ValueTask DisposeAsync() => await _container.DisposeAsync();
}
