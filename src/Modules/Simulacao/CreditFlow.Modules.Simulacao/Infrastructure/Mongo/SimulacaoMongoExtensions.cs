using CreditFlow.Modules.Simulacao.Application;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using MongoDB.Driver;

namespace CreditFlow.Modules.Simulacao.Infrastructure.Mongo;

public static class SimulacaoMongoExtensions
{
    /// <summary>
    /// Registra o repositório MongoDB de simulações se houver <c>ConnectionStrings:Mongo</c>. Chamar no
    /// SimulacaoModule ANTES dos TryAdd de memória: assim o Mongo vence quando configurado.
    /// </summary>
    public static IServiceCollection AddSimulacoesNoMongo(this IServiceCollection services, IConfiguration configuration)
    {
        var conexao = configuration.GetConnectionString("Mongo");
        if (string.IsNullOrWhiteSpace(conexao))
            return services;

        services.AddOptions<SimulacaoMongoOpcoes>()
            .Bind(configuration.GetSection(SimulacaoMongoOpcoes.Secao))
            .Validate(o => o.RetencaoDias >= 8, "Simulacao:Mongo:RetencaoDias precisa ser maior que a validade da simulação (7 dias).");

        MongoConexao.Registrar(services, conexao);
        services.TryAddSingleton(sp =>
        {
            var url = MongoUrl.Create(conexao);
            var banco = string.IsNullOrWhiteSpace(url.DatabaseName)
                ? sp.GetRequiredService<IOptions<SimulacaoMongoOpcoes>>().Value.BancoPadrao
                : url.DatabaseName;
            return sp.GetRequiredService<IMongoClient>().GetDatabase(banco);
        });
        services.TryAddSingleton<ISimulacaoRepositorio, SimulacaoRepositorioMongo>();
        services.AddHostedService<IndicesDeSimulacao>();
        return services;
    }
}

/// <summary>
/// Um MongoClient por processo: ele é thread-safe e guarda o pool de conexões. Criar um por requisição
/// abriria conexões sem parar (o clássico "too many connections" do Mongo).
/// </summary>
internal static class MongoConexao
{
    public static void Registrar(IServiceCollection services, string conexao)
    {
        services.TryAddSingleton<IMongoClient>(_ =>
        {
            var config = MongoClientSettings.FromConnectionString(conexao);
            // Falhar rápido quando o Mongo está fora: o padrão de seleção de servidor é 30 s.
            if (!conexao.Contains("serverSelectionTimeoutMS", StringComparison.OrdinalIgnoreCase))
                config.ServerSelectionTimeout = TimeSpan.FromSeconds(5);
            config.ApplicationName = "creditflow";
            return new MongoClient(config);
        });
    }
}
