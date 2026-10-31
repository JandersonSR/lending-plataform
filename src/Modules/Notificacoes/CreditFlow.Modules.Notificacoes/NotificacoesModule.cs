using CreditFlow.BuildingBlocks.Mensageria;
using CreditFlow.BuildingBlocks.Modulos;
using CreditFlow.Modules.Notificacoes.Api;
using CreditFlow.Modules.Notificacoes.Application;
using CreditFlow.Modules.Notificacoes.Infrastructure;
using CreditFlow.Modules.Notificacoes.Infrastructure.Mongo;
using CreditFlow.Modules.Pagamentos.Contracts.Eventos;
using CreditFlow.Modules.Propostas.Contracts.Eventos;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using MongoDB.Driver;

namespace CreditFlow.Modules.Notificacoes;

/// <summary>
/// Módulo Notificacoes: consome eventos e registra (e "envia") avisos. Dados no MongoDB, coleção
/// "notificacoes". Sem ConnectionStrings:Mongo o módulo não sobe: é o único lugar onde ele guarda algo.
/// </summary>
public sealed class NotificacoesModule : IModule
{
    public const string GrupoDeConsumidores = "creditflow-notificacoes";

    public string Nome => "Notificacoes";

    public void Registrar(IServiceCollection services, IConfiguration configuration)
    {
        var conexao = configuration.GetConnectionString("Mongo");
        if (string.IsNullOrWhiteSpace(conexao))
            throw new InvalidOperationException("O módulo Notificacoes exige ConnectionStrings:Mongo.");

        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton<IMongoClient>(_ =>
        {
            var config = MongoClientSettings.FromConnectionString(conexao);
            if (!conexao.Contains("serverSelectionTimeoutMS", StringComparison.OrdinalIgnoreCase))
                config.ServerSelectionTimeout = TimeSpan.FromSeconds(5);
            config.ApplicationName = "creditflow";
            return new MongoClient(config);
        });
        services.TryAddSingleton(sp =>
        {
            var nome = MongoUrl.Create(conexao).DatabaseName;
            return sp.GetRequiredService<IMongoClient>().GetDatabase(string.IsNullOrWhiteSpace(nome) ? "creditflow" : nome);
        });

        services.TryAddSingleton<INotificacaoRepositorio, NotificacaoRepositorioMongo>();
        services.TryAddSingleton<IEnviadorDeNotificacoes, EnviadorSimulado>();
        services.AddScoped<Notificar>();
        services.AddScoped<ListarNotificacoes>();
        services.AddHostedService<IndicesDeNotificacoes>();
        services.AddHealthChecks().AddCheck<VerificacaoDoMongo>("mongo", tags: ["ready"]);

        if (!string.IsNullOrWhiteSpace(configuration["Kafka:BootstrapServers"]) || !string.IsNullOrWhiteSpace(configuration.GetConnectionString("Kafka")))
        {
            services.AddConsumidorKafka(configuration, GrupoDeConsumidores, [Topicos.Pagamentos, Topicos.Propostas])
                .Manipular<DesembolsoConcluido, AoDesembolsoConcluido>()
                .Manipular<DesembolsoFalhou, AoDesembolsoFalhou>()
                .Manipular<ParcelaPaga, AoParcelaPaga>()
                .Manipular<PropostaAprovada, AoPropostaAprovada>()
                .Manipular<PropostaRecusada, AoPropostaRecusada>();
        }
    }

    public void Mapear(IEndpointRouteBuilder endpoints) => NotificacoesEndpoints.Mapear(endpoints);
}
