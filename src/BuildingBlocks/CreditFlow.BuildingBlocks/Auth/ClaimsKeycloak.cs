using System.Security.Claims;
using System.Text.Json;

namespace CreditFlow.BuildingBlocks.Auth;

/// <summary>Nomes de claims como o Keycloak emite (com MapInboundClaims = false, chegam assim).</summary>
public static class ClaimsKeycloak
{
    public const string Sub = "sub";
    public const string PreferredUsername = "preferred_username";
    public const string Azp = "azp";
    public const string RealmAccess = "realm_access";
    /// <summary>Tipo de claim de role que a API cria a partir de realm_access.roles.</summary>
    public const string Role = "role";

    /// <summary>
    /// Lê <c>realm_access</c> (objeto JSON: {"roles":["cliente",...]}) e devolve uma claim <c>role</c> por papel.
    /// O JsonWebTokenHandler entrega objetos aninhados como uma claim cujo Value é o JSON cru.
    /// </summary>
    public static IEnumerable<Claim> PapeisDoRealm(ClaimsPrincipal principal, string emissor)
    {
        foreach (var claim in principal.FindAll(RealmAccess))
        {
            JsonDocument doc;
            try { doc = JsonDocument.Parse(claim.Value); }
            catch (JsonException) { continue; }

            using (doc)
            {
                if (doc.RootElement.ValueKind != JsonValueKind.Object ||
                    !doc.RootElement.TryGetProperty("roles", out var roles) ||
                    roles.ValueKind != JsonValueKind.Array)
                    continue;

                foreach (var r in roles.EnumerateArray())
                    if (r.ValueKind == JsonValueKind.String && r.GetString() is { Length: > 0 } nome)
                        yield return new Claim(Role, nome, ClaimValueTypes.String, emissor);
            }
        }
    }
}
