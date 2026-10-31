using System.Net.Http.Json;
using System.Text.Json;
using Testcontainers.Keycloak;

namespace CreditFlow.IntegrationTests.Infra;

/// <summary>Keycloak de verdade, com o realm do repositório importado na subida.</summary>
public sealed class KeycloakFixture : IAsyncLifetime
{
    private readonly KeycloakContainer _container = new KeycloakBuilder()
        .WithImage(Imagens.Keycloak)
        .WithResourceMapping(Path.Combine(AppContext.BaseDirectory, "Auth", "creditflow-realm.json"), "/opt/keycloak/data/import/")
        .WithCommand("--import-realm")
        .Build();

    private readonly HttpClient _http = new();

    public string Authority => $"{_container.GetBaseAddress().TrimEnd('/')}/realms/creditflow";

    public ValueTask InitializeAsync() => new(_container.StartAsync());

    public async ValueTask DisposeAsync()
    {
        _http.Dispose();
        await _container.DisposeAsync();
    }

    /// <summary>Token de usuário pelo client creditflow-testes (password grant, só existe no realm local).</summary>
    public Task<string> TokenDeUsuarioAsync(string usuario) => TokenAsync(new Dictionary<string, string>
    {
        ["grant_type"] = "password",
        ["client_id"] = "creditflow-testes",
        ["client_secret"] = "testes-segredo-so-para-dev-local",
        ["username"] = usuario,
        ["password"] = "Senha@123",
        ["scope"] = "openid",
    });

    public Task<string> TokenDoBackofficeAsync() => TokenAsync(new Dictionary<string, string>
    {
        ["grant_type"] = "client_credentials",
        ["client_id"] = "creditflow-backoffice",
        ["client_secret"] = "backoffice-segredo-so-para-dev-local",
    });

    private async Task<string> TokenAsync(Dictionary<string, string> campos)
    {
        using var resposta = await _http.PostAsync($"{Authority}/protocol/openid-connect/token", new FormUrlEncodedContent(campos));
        var corpo = await resposta.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(resposta.IsSuccessStatusCode, corpo.ToString());
        return corpo.GetProperty("access_token").GetString()!;
    }
}
