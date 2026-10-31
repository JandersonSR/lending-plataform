using System.Security.Claims;
using Microsoft.AspNetCore.Http;

namespace CreditFlow.BuildingBlocks.Auth;

internal sealed class UsuarioAtual(IHttpContextAccessor acessor) : ICurrentUser
{
    private ClaimsPrincipal? Principal => acessor.HttpContext?.User;

    public bool Autenticado => Principal?.Identity?.IsAuthenticated == true;
    public string? Id => Principal?.FindFirstValue(ClaimsKeycloak.Sub);
    public string? Nome => Principal?.FindFirstValue(ClaimsKeycloak.PreferredUsername);
    public string? ClienteOAuth => Principal?.FindFirstValue(ClaimsKeycloak.Azp);

    // O Keycloak dá às contas de serviço o username "service-account-<clientId>".
    public bool EhMaquina => Nome?.StartsWith("service-account-", StringComparison.Ordinal) == true;

    public IReadOnlyCollection<string> Papeis =>
        Principal?.FindAll(ClaimsKeycloak.Role).Select(c => c.Value).Distinct().ToArray() ?? [];

    public bool TemPapel(string papel) => Principal?.IsInRole(papel) == true;
}
