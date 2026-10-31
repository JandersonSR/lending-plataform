using Confluent.Kafka;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CreditFlow.BuildingBlocks.Mensageria;

public static class MensageriaServiceCollectionExtensions
{
    /// <summary>Tag de health check que o Host filtra em /healthz/live.</summary>
    public const string TagDeLiveness = "live";

    /// <summary>
    /// Chamado uma vez pelo Program.cs. Registra o que é comum: opções do Kafka, um produtor por
    /// processo (singleton: o IProducer é thread-safe e caro de criar, cada um abre conexões a todos
    /// os brokers), o publicador direto e, no ambiente local, a criação dos tópicos.
    /// Outbox e consumidores são registrados por cada módulo (<see cref="AddOutboxRelay{TDbContext}"/>,
    /// <see cref="AddConsumidorKafka"/>), porque só o módulo sabe seu DbContext e seus eventos.
    /// </summary>
    public static IServiceCollection AddMensageria(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<KafkaOpcoes>()
            .Bind(configuration.GetSection(KafkaOpcoes.Secao))
            .PostConfigure(o =>
            {
                if (string.IsNullOrWhiteSpace(o.BootstrapServers))
                    o.BootstrapServers = configuration.GetConnectionString("Kafka") ?? "";
            });
        services.AddOptions<OutboxOpcoes>().Bind(configuration.GetSection(OutboxOpcoes.Secao));

        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton<FabricaDeClientesKafka>();
        services.TryAddSingleton<IProducer<string, byte[]>>(sp => sp.GetRequiredService<FabricaDeClientesKafka>().CriarProdutor("produtor"));
        services.TryAddSingleton<IPublicadorDeEventos, PublicadorDeEventos>();
        services.AddHostedService<EsvaziadorDoProdutor>();

        if (configuration.GetValue<bool>($"{KafkaOpcoes.Secao}:{nameof(KafkaOpcoes.CriarTopicosAoIniciar)}"))
            services.AddHostedService<CriadorDeTopicos>();

        // Tag "live": entra na liveness (/healthz/live). Sem consumidores no processo, sempre saudável.
        services.AddHealthChecks().AddCheck<VerificacaoDosConsumidores>("mensageria-consumidores", tags: [TagDeLiveness]);

        return services;
    }

    /// <summary>Liga o relay da outbox do DbContext do módulo. Rode em todo papel onde o módulo grava eventos.</summary>
    public static IServiceCollection AddOutboxRelay<TDbContext>(this IServiceCollection services) where TDbContext : DbContext
    {
        services.AddHostedService<OutboxRelay<TDbContext>>();
        return services;
    }

    /// <summary>
    /// Registra um consumidor (um grupo do Kafka) e devolve o builder para declarar os eventos:
    /// <code>
    /// services.AddConsumidorKafka(configuration, "creditflow-pagamentos", [Topicos.Propostas])
    ///         .Manipular&lt;ContratoAssinado, AoContratoAssinado&gt;();
    /// </code>
    /// O nome do grupo é também o "consumidor" da inbox e o prefixo da DLT (<c>creditflow-pagamentos.dlt</c>).
    /// </summary>
    public static ConsumidorKafkaBuilder AddConsumidorKafka(
        this IServiceCollection services, IConfiguration configuration, string grupo, IReadOnlyList<string> topicos)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(grupo);
        var opcoes = new ConsumidorOpcoes();
        configuration.GetSection($"Mensageria:Consumidores:{grupo}").Bind(opcoes);
        opcoes.Grupo = grupo;
        opcoes.Topicos = [.. topicos];

        var mapa = new MapaDeManipuladores();
        RegistroDeConsumidores.Adicionar(grupo);

        // AddSingleton<IHostedService>(fábrica), e não AddHostedService: este último usa TryAddEnumerable
        // e descartaria o segundo consumidor do mesmo tipo no mesmo processo.
        services.AddSingleton<IHostedService>(sp => new ConsumidorKafka(
            opcoes, mapa,
            sp.GetRequiredService<FabricaDeClientesKafka>(),
            sp.GetRequiredService<IProducer<string, byte[]>>(),
            sp.GetRequiredService<IServiceScopeFactory>(),
            sp.GetRequiredService<TimeProvider>(),
            sp.GetRequiredService<ILoggerFactory>()));

        return new ConsumidorKafkaBuilder(services, mapa);
    }
}

public sealed class ConsumidorKafkaBuilder
{
    private readonly IServiceCollection _services;
    private readonly MapaDeManipuladores _mapa;

    internal ConsumidorKafkaBuilder(IServiceCollection services, MapaDeManipuladores mapa)
    {
        _services = services;
        _mapa = mapa;
    }

    /// <summary>Declara que este consumidor entende <typeparamref name="TEvento"/> (tipo e versão do atributo).</summary>
    public ConsumidorKafkaBuilder Manipular<TEvento, TManipulador>()
        where TEvento : class, IEventoIntegracao
        where TManipulador : class, IManipuladorDeEvento<TEvento>
    {
        _mapa.Adicionar<TEvento>();
        _services.TryAddScoped<IManipuladorDeEvento<TEvento>, TManipulador>();
        return this;
    }
}

/// <summary>Grupos registrados neste processo, para o <see cref="CriadorDeTopicos"/> criar as DLTs.</summary>
internal static class RegistroDeConsumidores
{
    private static readonly HashSet<string> Grupos = [];
    private static readonly Lock Trava = new();

    internal static void Adicionar(string grupo) { lock (Trava) Grupos.Add(grupo); }

    internal static string[] Todos() { lock (Trava) return [.. Grupos]; }
}

/// <summary>No desligamento, espera as mensagens em voo do produtor saírem (Flush) antes de o processo morrer.</summary>
/// Registrado antes dos relays e consumidores dos módulos, para PARAR depois deles (o host para na ordem inversa).
internal sealed class EsvaziadorDoProdutor(FabricaDeClientesKafka fabrica) : IHostedService
{
    public Task StartAsync(CancellationToken ct) => Task.CompletedTask;

    public Task StopAsync(CancellationToken ct)
    {
        // Só os produtores que alguém chegou a criar (resolver o IProducer aqui criaria um à toa).
        foreach (var produtor in fabrica.ProdutoresCriados)
            produtor.Flush(TimeSpan.FromSeconds(10));
        return Task.CompletedTask;
    }
}
