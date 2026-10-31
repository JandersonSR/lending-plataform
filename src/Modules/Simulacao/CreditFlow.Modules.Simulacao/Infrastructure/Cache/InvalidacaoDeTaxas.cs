using CreditFlow.Modules.Simulacao.Infrastructure.Taxas;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace CreditFlow.Modules.Simulacao.Infrastructure.Cache;

/// <summary>
/// Invalidação por evento. O HybridCache.RemoveAsync apaga o L2 (Dragonfly) e o L1 DESTA réplica;
/// as outras réplicas continuariam com a taxa velha na memória até o LocalCacheExpiration (1 min).
/// Para encurtar isso, quem invalida também publica um aviso num canal pub/sub do Dragonfly, e cada
/// réplica que recebe apaga o próprio L1.
/// </summary>
public interface IInvalidadorDeTaxas
{
    Task InvalidarAsync(string motivo, CancellationToken ct);
}

internal sealed partial class InvalidadorDeTaxas(HybridCache cache, ILogger<InvalidadorDeTaxas> log, IConnectionMultiplexer? redis = null)
    : IInvalidadorDeTaxas
{
    public static readonly RedisChannel Canal = RedisChannel.Literal("creditflow:simulacao:invalidar-taxas");

    public async Task InvalidarAsync(string motivo, CancellationToken ct)
    {
        try
        {
            await cache.RemoveByTagAsync(TabelaDeTaxasEmCache.Tag, ct);
            await cache.RemoveAsync(TabelaDeTaxasEmCache.Chave, ct);
            if (redis is not null)
                await redis.GetSubscriber().PublishAsync(Canal, motivo);
            Invalidado(log, motivo);
        }
        catch (Exception ex) when (ex is RedisException or TimeoutException)
        {
            // Dragonfly fora: o L2 vence sozinho em até 10 min e o L1 em 1 min. Registrar e seguir;
            // falhar a operação que mudou a taxa por causa do cache seria inverter as prioridades.
            InvalidacaoFalhou(log, motivo, ex);
        }
    }

    /// <summary>Recebido de outra réplica: só o L1 local precisa sumir (o L2 quem publicou já apagou).</summary>
    internal ValueTask ApagarLocalAsync(CancellationToken ct) => cache.RemoveAsync(TabelaDeTaxasEmCache.Chave, ct);

    [LoggerMessage(Level = LogLevel.Information, Message = "Cache de taxas invalidado: {Motivo}")]
    private static partial void Invalidado(ILogger logger, string motivo);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Não foi possível invalidar o cache de taxas ({Motivo}); ele vence sozinho.")]
    private static partial void InvalidacaoFalhou(ILogger logger, string motivo, Exception ex);
}

/// <summary>
/// Liga as duas pontas: (1) mudança na configuração de taxas (appsettings/ConfigMap recarregado) dispara a
/// invalidação; (2) avisos de outras réplicas pelo pub/sub apagam o L1 local.
/// </summary>
internal sealed partial class OuvinteDeInvalidacaoDeTaxas(
    InvalidadorDeTaxas invalidador,
    IOptionsMonitor<SimulacaoOpcoes> opcoes,
    ILogger<OuvinteDeInvalidacaoDeTaxas> log,
    IConnectionMultiplexer? redis = null) : IHostedService, IDisposable
{
    private IDisposable? _assinaturaConfig;

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        _assinaturaConfig = opcoes.OnChange(novas =>
            _ = invalidador.InvalidarAsync("configuração de taxas recarregada", CancellationToken.None));

        if (redis is null)
            return;
        try
        {
            await redis.GetSubscriber().SubscribeAsync(InvalidadorDeTaxas.Canal, (_, _) =>
                _ = invalidador.ApagarLocalAsync(CancellationToken.None).AsTask());
        }
        catch (Exception ex) when (ex is RedisException or TimeoutException)
        {
            // Sem Dragonfly na subida: sem aviso entre réplicas, e o L1 de 1 min é o limite da divergência.
            // O StackExchange.Redis refaz as assinaturas sozinho quando a conexão volta.
            AssinaturaFalhou(log, ex);
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public void Dispose() => _assinaturaConfig?.Dispose();

    [LoggerMessage(Level = LogLevel.Warning, Message = "Não foi possível assinar o canal de invalidação de taxas.")]
    private static partial void AssinaturaFalhou(ILogger logger, Exception ex);
}
