using System.Net;
using System.Net.Http.Headers;
using CreditFlow.BuildingBlocks.Auth;
using CreditFlow.IntegrationTests.Infra;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace CreditFlow.IntegrationTests.Auth;

/// <summary>
/// O realm do repositório + o AddAutenticacaoCreditFlow de verdade, contra um Keycloak de verdade.
/// Uma API mínima (TestServer) com um endpoint por policy: o que se testa aqui é a autenticação, não Propostas.
/// </summary>
public sealed class KeycloakTests(KeycloakFixture kc) : IClassFixture<KeycloakFixture>, IAsyncLifetime
{
    private WebApplication _app = null!;
    private HttpClient _http = null!;

    public async ValueTask InitializeAsync()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Auth:Authority"] = kc.Authority,
            ["Auth:Audience"] = "creditflow-api",
            ["Auth:ExigirHttps"] = "false",
        });
        builder.Services.AddAutenticacaoCreditFlow(builder.Configuration);

        _app = builder.Build();
        _app.UseAutenticacaoCreditFlow();
        _app.MapGet("/criar", (ICurrentUser u) => Results.Ok(new { u.Id, u.Nome, u.Papeis })).RequireAuthorization(Politicas.CriarProposta);
        _app.MapGet("/analisar", () => Results.Ok()).RequireAuthorization(Politicas.AnalisarProposta);
        _app.MapGet("/pagamentos", (ICurrentUser u) => Results.Ok(new { u.EhMaquina })).RequireAuthorization(Politicas.OperarPagamentos);
        await _app.StartAsync();
        _http = _app.GetTestClient();
    }

    public async ValueTask DisposeAsync()
    {
        _http.Dispose();
        await _app.DisposeAsync();
    }

    private async Task<HttpResponseMessage> Get(string rota, string? token)
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, rota);
        if (token is not null) req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return await _http.SendAsync(req);
    }

    [Fact]
    public async Task Sem_token_401()
    {
        using var r = await Get("/criar", null);
        Assert.Equal(HttpStatusCode.Unauthorized, r.StatusCode);
    }

    [Fact]
    public async Task Cliente_cria_mas_nao_analisa()
    {
        var token = await kc.TokenDeUsuarioAsync("ana.cliente");

        using var criar = await Get("/criar", token);
        Assert.Equal(HttpStatusCode.OK, criar.StatusCode);
        Assert.Contains("ana.cliente", await criar.Content.ReadAsStringAsync()); // NameClaimType = preferred_username

        using var analisar = await Get("/analisar", token);
        Assert.Equal(HttpStatusCode.Forbidden, analisar.StatusCode);
    }

    [Fact]
    public async Task Analista_analisa()
    {
        using var r = await Get("/analisar", await kc.TokenDeUsuarioAsync("carla.analista"));
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
    }

    [Fact]
    public async Task Backoffice_m2m_opera_pagamentos_como_maquina()
    {
        using var r = await Get("/pagamentos", await kc.TokenDoBackofficeAsync());
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        Assert.Contains("true", await r.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Token_adulterado_401_com_motivo_no_cabecalho()
    {
        var token = await kc.TokenDeUsuarioAsync("ana.cliente");
        var partes = token.Split('.');
        var adulterado = $"{partes[0]}.{partes[1]}.{partes[2][..^4]}AAAA";

        using var r = await Get("/criar", adulterado);
        Assert.Equal(HttpStatusCode.Unauthorized, r.StatusCode);
        Assert.Contains("invalid_token", r.Headers.WwwAuthenticate.ToString());
    }
}
