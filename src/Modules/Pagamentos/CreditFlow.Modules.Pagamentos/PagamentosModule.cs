using CreditFlow.BuildingBlocks.Mensageria;
using CreditFlow.BuildingBlocks.Modulos;
using CreditFlow.Modules.Pagamentos.Api;
using CreditFlow.Modules.Pagamentos.Application.Consultas;
using CreditFlow.Modules.Pagamentos.Application.Desembolsos;
using CreditFlow.Modules.Pagamentos.Application.Gateway;
using CreditFlow.Modules.Pagamentos.Application.Parcelas;
using CreditFlow.Modules.Pagamentos.Contracts;
using CreditFlow.Modules.Pagamentos.Infrastructure.Gateway;
using CreditFlow.Modules.Pagamentos.Infrastructure.Persistencia;
using CreditFlow.Modules.Propostas.Contracts.Eventos;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace CreditFlow.Modules.Pagamentos;

/// <summary>
/// Pagamentos sobe em dois perfis com a MESMA imagem:
/// <list type="bullet">
/// <item><b>worker-pagamentos</b> (papel worker): consumidor de creditflow.propostas.v1 (saga de desembolso) + relay da outbox.
///       Escala pelo lag do grupo <see cref="GrupoDeConsumidores"/> (KEDA).</item>
/// <item><b>api-pagamentos</b> (papel api): consultas e baixa de parcela + relay da outbox (ParcelaPaga nasce aqui).</item>
/// </list>
/// Os dois rodam relays sobre a mesma outbox ao mesmo tempo; as faixas com SKIP LOCKED mantêm a ordem por chave.
/// </summary>
public sealed class PagamentosModule : IModule, IModuloComContratos
{
    public const string GrupoDeConsumidores = "creditflow-pagamentos";

    public string Nome => "Pagamentos";

    public void Registrar(IServiceCollection services, IConfiguration configuration)
    {
        RegistrarContratos(services, configuration);

        services.AddOptions<GatewayPixSimuladoOpcoes>().Bind(configuration.GetSection(GatewayPixSimuladoOpcoes.Secao));
        services.AddOptions<DesembolsoOpcoes>().Bind(configuration.GetSection(DesembolsoOpcoes.Secao));
        services.AddScoped<IGatewayPix, GatewayPixSimulado>();
        services.AddScoped<PagarParcela>();

        // Ordem importa: hosted services sobem na ordem de registro (schema → relay → consumidor).
        if (configuration.GetValue<bool>("Pagamentos:CriarSchemaAoIniciar"))
            services.AddHostedService<InicializadorDoSchema>();

        if (!TemKafka(configuration))
            return; // sem Kafka, Pagamentos só atende consultas (útil em teste); a outbox acumula

        services.AddOutboxRelay<PagamentosDbContext>();

        if (EhWorker(configuration))
        {
            services.AddConsumidorKafka(configuration, GrupoDeConsumidores, [Topicos.Propostas])
                .Manipular<ContratoAssinado, AoContratoAssinado>();
        }
    }

    /// <summary>Só o necessário para outros módulos usarem <see cref="IConsultaDePagamentos"/> num processo sem Pagamentos.</summary>
    public void RegistrarContratos(IServiceCollection services, IConfiguration configuration)
    {
        services.TryAddSingleton(TimeProvider.System);
        services.AddDbContext<PagamentosDbContext>(o => o.UseNpgsql(
            configuration.GetConnectionString("Postgres"),
            npgsql =>
            {
                npgsql.MigrationsHistoryTable("__ef_migracoes", PagamentosDbContext.Schema);
                // Falhas transitórias (failover do Flexible Server, rede) repetidas pelo EF. Por isso
                // toda transação explícita do módulo e da Mensageria roda dentro de CreateExecutionStrategy().
                npgsql.EnableRetryOnFailure(maxRetryCount: 3, maxRetryDelay: TimeSpan.FromSeconds(2), errorCodesToAdd: null);
            }));
        services.TryAddScoped<ConsultasDePagamentos>();
        services.TryAddScoped<IConsultaDePagamentos>(sp => sp.GetRequiredService<ConsultasDePagamentos>());
    }

    public void Mapear(IEndpointRouteBuilder endpoints) => PagamentosEndpoints.Mapear(endpoints);

    private static bool TemKafka(IConfiguration configuration) =>
        !string.IsNullOrWhiteSpace(configuration[$"{KafkaOpcoes.Secao}:{nameof(KafkaOpcoes.BootstrapServers)}"])
        || !string.IsNullOrWhiteSpace(configuration.GetConnectionString("Kafka"));

    private static bool EhWorker(IConfiguration configuration) =>
        string.Equals(configuration[$"{PerfilDeExecucao.Secao}:Papel"], "worker", StringComparison.OrdinalIgnoreCase);
}
