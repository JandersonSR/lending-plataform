namespace CreditFlow.BuildingBlocks.Auth;

/// <summary>Quem está chamando, já validado. Domínio e handlers dependem disto, não de HttpContext.</summary>
public interface ICurrentUser
{
    bool Autenticado { get; }

    /// <summary>Claim <c>sub</c>: id estável do usuário no Keycloak. Use como chave; nunca o e-mail ou o username.</summary>
    string? Id { get; }

    /// <summary>Claim <c>preferred_username</c>. Só para exibir e registrar.</summary>
    string? Nome { get; }

    /// <summary>Claim <c>azp</c>: o client que pediu o token (creditflow-web, creditflow-backoffice...).</summary>
    string? ClienteOAuth { get; }

    /// <summary>Token de conta de serviço (client credentials), sem pessoa por trás.</summary>
    bool EhMaquina { get; }

    IReadOnlyCollection<string> Papeis { get; }

    bool TemPapel(string papel);
}
