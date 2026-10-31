using CreditFlow.BuildingBlocks.Modulos;

namespace CreditFlow.Host;

internal static class Sondas
{
    /// <summary>GET /healthz/live no próprio processo. 0 = vivo, 1 = não. Usado pelo HEALTHCHECK da imagem.</summary>
    public static async Task<int> VerificarVivoAsync()
    {
        var porta = (Environment.GetEnvironmentVariable("ASPNETCORE_HTTP_PORTS") ?? "8080").Split(';', ',')[0].Trim();
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(3) };
        try
        {
            using var resposta = await http.GetAsync(new Uri($"http://localhost:{porta}/healthz/live"));
            return resposta.IsSuccessStatusCode ? 0 : 1;
        }
        catch (HttpRequestException)
        {
            return 1;
        }
        catch (TaskCanceledException)
        {
            return 1;
        }
    }
}

internal static class Migracoes
{
    /// <summary>
    /// Roda todas as <see cref="IMigracao"/> registradas pelos módulos do perfil, em ordem, e devolve o código
    /// de saída do processo. Não inicia hosted services nem o Kestrel: o Job termina quando as migrações terminam.
    /// </summary>
    public static async Task<int> AplicarAsync(IServiceProvider servicos, ILogger log)
    {
        using var cancelar = new CancellationTokenSource(TimeSpan.FromMinutes(10));
        var migracoes = servicos.GetServices<IMigracao>().ToList();
        log.LogInformation("Modo migração: {Quantidade} migração(ões) registrada(s).", migracoes.Count);
        try
        {
            foreach (var migracao in migracoes)
            {
                log.LogInformation("Aplicando {Migracao}...", migracao.Nome);
                await migracao.AplicarAsync(cancelar.Token);
            }
            log.LogInformation("Migrações concluídas.");
            return 0;
        }
#pragma warning disable CA1031 // o Job precisa sair com 1 e o motivo no log, seja qual for a falha
        catch (Exception ex)
#pragma warning restore CA1031
        {
            log.LogError(ex, "Migração falhou.");
            return 1;
        }
    }
}
