using CreditFlow.BuildingBlocks.Mensageria;
using CreditFlow.BuildingBlocks.Modulos;
using CreditFlow.Modules.Propostas.Api;
using CreditFlow.Modules.Propostas.Application;
using CreditFlow.Modules.Propostas.Application.Integracao;
using CreditFlow.Modules.Propostas.Contracts;
using CreditFlow.Modules.Propostas.Domain;
using CreditFlow.Modules.Propostas.Infrastructure.Persistencia;
using CreditFlow.Modules.Propostas.Infrastructure.Simulacoes;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace CreditFlow.Modules.Propostas;

/// <summary>
/// Módulo Propostas. Diferente de Clientes, NÃO tem modo "memória": sem ConnectionStrings:Postgres o
/// Host não sobe com este módulo. Proposta de crédito sem banco transacional não é proposta.
/// Depende de Simulacao e Clientes só pelos .Contracts (registrados pelos próprios módulos, via
/// IModuloComContratos, mesmo quando eles não estão ativos neste processo).
/// </summary>
public sealed class PropostasModule : IModule, IModuloComContratos
{
    public string Nome => "Propostas";

    public void Registrar(IServiceCollection services, IConfiguration configuration)
    {
        if (string.IsNullOrWhiteSpace(configuration.GetConnectionString("Postgres")))
            throw new InvalidOperationException("O módulo Propostas exige ConnectionStrings:Postgres.");
        RegistrarContratos(services, configuration);

        services.AddOptions<ParametrosDeAnalise>()
            .Bind(configuration.GetSection(ParametrosDeAnalise.Secao))
            .Validate(p => p.ComprometimentoAprovacaoAutomatica <= p.ComprometimentoMaximo
                           && p.ScoreMinimo <= p.ScoreAprovacaoAutomatica
                           && p.ValidadeDaAprovacaoDias > 0 && p.ValidadeDaSimulacaoDias > 0,
                "Propostas:Analise inconsistente.")
            .ValidateOnStart();

        services.TryAddScoped<ISimulacoesParaProposta, SimulacoesViaContrato>();
        services.AddScoped<CriarProposta>();
        services.AddScoped<ConsultarProposta>();
        services.AddScoped<ListarPropostas>();
        services.AddScoped<DecidirProposta>();
        services.AddScoped<ContratarProposta>();
        services.AddScoped<CancelarProposta>();
        services.AddScoped<RegistrarResultadoDoDesembolso>();

        services.AddHealthChecks().AddCheck<VerificacaoDoPostgres>("postgres-propostas", tags: ["ready"]);

        // Ordem dos hosted services = ordem de subida: schema → relay → consumidor.
        if (configuration.GetValue<bool>("Propostas:CriarSchemaAoIniciar"))
            services.AddHostedService<InicializadorDoSchemaDePropostas>();

        // Módulo 6. Sem Kafka configurado (testes, modo mínimo), os eventos ficam na outbox esperando:
        // nada se perde, só não sai. Com Kafka: o relay publica e Propostas reage ao fim da saga.
        if (TemKafka(configuration))
        {
            services.AddOutboxRelay<PropostasDbContext>();
            services.AddReacoesAoDesembolso(configuration);
        }
    }

    private static bool TemKafka(IConfiguration configuration) =>
        !string.IsNullOrWhiteSpace(configuration[$"{KafkaOpcoes.Secao}:{nameof(KafkaOpcoes.BootstrapServers)}"])
        || !string.IsNullOrWhiteSpace(configuration.GetConnectionString("Kafka"));

    public void RegistrarContratos(IServiceCollection services, IConfiguration configuration)
    {
        services.TryAddSingleton(TimeProvider.System);
        var conexao = configuration.GetConnectionString("Postgres");
        if (string.IsNullOrWhiteSpace(conexao))
            return; // processo sem Postgres (ex.: api-simulacao): o contrato simplesmente não existe aqui

        services.AddDbContext<PropostasDbContext>(o => o.UseNpgsql(conexao, npgsql => npgsql
            .MigrationsHistoryTable("__ef_migracoes", PropostasDbContext.Schema)
            // Falhas transitórias (failover do Flexible Server, conexão derrubada): tenta de novo.
            // Com retry, transações explícitas precisam de CreateExecutionStrategy (módulo 4, lição 4.3).
            .EnableRetryOnFailure(maxRetryCount: 3)));
        services.TryAddScoped<IPropostaRepositorio, PropostaRepositorioEf>();
        services.TryAddScoped<IConsultaDePropostas, ConsultaDePropostas>();
    }

    public void Mapear(IEndpointRouteBuilder endpoints) => PropostasEndpoints.Mapear(endpoints);
}
