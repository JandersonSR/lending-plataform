using System.Threading.RateLimiting;
using CreditFlow.BuildingBlocks.Modulos;
using CreditFlow.Modules.Simulacao.Api;
using CreditFlow.Modules.Simulacao.Application;
using CreditFlow.Modules.Simulacao.Contracts;
using CreditFlow.Modules.Simulacao.Infrastructure.Cache;
using CreditFlow.Modules.Simulacao.Infrastructure.Eventos;
using CreditFlow.Modules.Simulacao.Infrastructure.Memoria;
using CreditFlow.Modules.Simulacao.Infrastructure.Mongo;
using CreditFlow.Modules.Simulacao.Infrastructure.Taxas;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace CreditFlow.Modules.Simulacao;

/// <summary>
/// Módulo Simulacao: público, CPU-bound, sem estado compartilhado além do histórico. É o candidato
/// natural a rodar sozinho no perfil api-simulacao e escalar por CPU (módulo 8 do curso).
/// </summary>
public sealed class SimulacaoModule : IModule, IModuloComContratos
{
    public string Nome => "Simulacao";

    public void Registrar(IServiceCollection services, IConfiguration configuration)
    {
        RegistrarContratos(services, configuration);

        services.AddScoped<SimularEmprestimo>();
        services.AddScoped<ObterSimulacao>();

        if (TemKafka(configuration))
            services.TryAddSingleton<IPublicadorDeSimulacoes, PublicadorDeSimulacoesKafka>();
        else
            services.TryAddSingleton<IPublicadorDeSimulacoes, PublicadorDeSimulacoesNulo>();

        var porMinuto = configuration.GetValue($"{SimulacaoOpcoes.Secao}:{nameof(SimulacaoOpcoes.RequisicoesPorMinuto)}", 30);
        services.AddRateLimiter(o => o.AddPolicy(SimulacaoEndpoints.PoliticaDeLimite, http =>
            RateLimitPartition.GetFixedWindowLimiter(
                // Por IP do cliente. Atrás do ingress, isso só funciona com os cabeçalhos X-Forwarded-For
                // processados (ASPNETCORE_FORWARDEDHEADERS_ENABLED=true no Helm); senão todo mundo é "o ingress".
                partitionKey: http.Connection.RemoteIpAddress?.ToString() ?? "desconhecido",
                factory: _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = porMinuto,
                    Window = TimeSpan.FromMinutes(1),
                    QueueLimit = 0,
                })));
    }

    /// <summary>
    /// O mínimo para atender <see cref="IConsultaDeSimulacoes"/> (Propostas usa) num processo sem Simulacao ativo.
    /// Os registros usam TryAdd: as implementações de Mongo (Infrastructure/Mongo) e de cache
    /// (Infrastructure/Cache), quando registradas ANTES destas linhas, vencem as de memória/configuração.
    /// </summary>
    public void RegistrarContratos(IServiceCollection services, IConfiguration configuration)
    {
        services.TryAddSingleton(TimeProvider.System);

        services.AddOptions<SimulacaoOpcoes>()
            .Bind(configuration.GetSection(SimulacaoOpcoes.Secao))
            .Validate(TabelaDeTaxasEmConfiguracao.Valida,
                "Simulacao:Faixas inválida: precisa de uma faixa com ScoreMinimo 0, nomes preenchidos, taxas entre 0 e 20% e mínimos distintos.")
            .ValidateOnStart();

        // Módulos 4 e 5 do curso: MongoDB (histórico) e HybridCache + Dragonfly (taxas). Cada um só ativa com a
        // sua connection string; sem elas, os TryAdd abaixo registram memória e configuração.
        services.AddSimulacoesNoMongo(configuration);
        services.AddCacheDeTaxas(configuration);

        services.TryAddSingleton<ISimulacaoRepositorio>(_ => new SimulacaoRepositorioEmMemoria());
        services.TryAddSingleton<ITabelaDeTaxas, TabelaDeTaxasEmConfiguracao>();
        services.TryAddScoped<IConsultaDeSimulacoes, ConsultaDeSimulacoes>();
    }

    public void Mapear(IEndpointRouteBuilder endpoints) => SimulacaoEndpoints.Mapear(endpoints);

    private static bool TemKafka(IConfiguration configuration) =>
        !string.IsNullOrWhiteSpace(configuration["Kafka:BootstrapServers"])
        || !string.IsNullOrWhiteSpace(configuration.GetConnectionString("Kafka"));
}
