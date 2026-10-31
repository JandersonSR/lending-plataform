namespace CreditFlow.BuildingBlocks.Auth;

/// <summary>Seção <c>Auth</c> do appsettings.</summary>
public sealed class AuthOpcoes
{
    public const string Secao = "Auth";

    /// <summary>URL do realm, exatamente como aparece no <c>iss</c> do token. Ex.: http://localhost:8081/realms/creditflow</summary>
    public string Authority { get; set; } = "";

    /// <summary>Valor que precisa estar no <c>aud</c>. No CreditFlow: creditflow-api.</summary>
    public string Audience { get; set; } = "creditflow-api";

    /// <summary>
    /// Endereço do documento de descoberta quando a API não alcança o Authority pelo mesmo nome que o navegador
    /// (API num contêiner, Keycloak atrás de proxy). Vazio = {Authority}/.well-known/openid-configuration.
    /// </summary>
    public string? MetadataAddress { get; set; }

    /// <summary>Emissores aceitos além do Authority (migração de hostname, por exemplo). Use com parcimônia.</summary>
    public string[] EmissoresAdicionais { get; set; } = [];

    /// <summary>Folga para diferença de relógio entre Keycloak e API. O padrão do .NET é 5 minutos; aqui, 30 segundos.</summary>
    public int FolgaDeRelogioSegundos { get; set; } = 30;

    /// <summary>Exigir HTTPS no metadata. Só pode ser false em desenvolvimento.</summary>
    public bool ExigirHttps { get; set; } = true;
}
