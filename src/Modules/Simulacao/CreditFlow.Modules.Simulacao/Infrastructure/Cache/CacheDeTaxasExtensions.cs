using CreditFlow.Modules.Simulacao.Application;
using CreditFlow.Modules.Simulacao.Infrastructure.Taxas;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.Caching.StackExchangeRedis;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using StackExchange.Redis;

namespace CreditFlow.Modules.Simulacao.Infrastructure.Cache;

public static class CacheDeTaxasExtensions
{
    /// <summary>
    /// HybridCache (sempre: L1 em memória) + Dragonfly como L2 quando houver <c>ConnectionStrings:Cache</c>.
    /// Chamar no SimulacaoModule ANTES do TryAdd de <see cref="ITabelaDeTaxas"/> da configuração.
    /// </summary>
    public static IServiceCollection AddCacheDeTaxas(this IServiceCollection services, IConfiguration configuration)
    {
        var conexao = configuration.GetConnectionString("Cache");
        if (!string.IsNullOrWhiteSpace(conexao))
        {
            var opcoes = OpcoesDoDragonfly(conexao);
            // Uma conexão por processo, compartilhada pelo cache (L2) e pelo pub/sub de invalidação.
            services.TryAddSingleton<IConnectionMultiplexer>(_ => ConnectionMultiplexer.Connect(opcoes));
            services.AddStackExchangeRedisCache(o => o.InstanceName = "creditflow:");
            services.AddOptions<RedisCacheOptions>()
                .Configure<IServiceProvider>((o, sp) => o.ConnectionMultiplexerFactory =
                    () => Task.FromResult(sp.GetRequiredService<IConnectionMultiplexer>()));
        }

        services.AddHybridCache(o =>
        {
            o.DefaultEntryOptions = TabelaDeTaxasEmCache.Validade;
            o.MaximumPayloadBytes = 64 * 1024; // a tabela de taxas tem poucos bytes; 64 KB pega erro de uso cedo
        });

        services.TryAddSingleton<TabelaDeTaxasEmConfiguracao>();
        services.TryAddSingleton<ITabelaDeTaxas>(sp => new TabelaDeTaxasEmCache(
            sp.GetRequiredService<TabelaDeTaxasEmConfiguracao>(), sp.GetRequiredService<HybridCache>()));
        services.TryAddSingleton<InvalidadorDeTaxas>();
        services.TryAddSingleton<IInvalidadorDeTaxas>(sp => sp.GetRequiredService<InvalidadorDeTaxas>());
        services.AddHostedService<OuvinteDeInvalidacaoDeTaxas>();
        return services;
    }

    /// <summary>
    /// Cache é opcional: se ele cair, o sistema fica mais lento, não fora. As opções abaixo garantem isso.
    /// </summary>
    internal static ConfigurationOptions OpcoesDoDragonfly(string conexao)
    {
        var o = ConfigurationOptions.Parse(conexao);
        o.AbortOnConnectFail = false; // sobe mesmo com o Dragonfly fora e reconecta em segundo plano
        if (!conexao.Contains("connectTimeout", StringComparison.OrdinalIgnoreCase)) o.ConnectTimeout = 1000;
        if (!conexao.Contains("syncTimeout", StringComparison.OrdinalIgnoreCase)) o.SyncTimeout = 500;
        if (!conexao.Contains("asyncTimeout", StringComparison.OrdinalIgnoreCase)) o.AsyncTimeout = 500;
        o.ClientName = "creditflow";
        return o;
    }
}
