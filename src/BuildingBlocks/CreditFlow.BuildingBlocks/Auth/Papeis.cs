namespace CreditFlow.BuildingBlocks.Auth;

/// <summary>Roles de realm do Keycloak (deploy/local/keycloak/creditflow-realm.json). Mudou lá, muda aqui.</summary>
public static class Papeis
{
    public const string Cliente = "cliente";
    public const string Analista = "analista";
    public const string OperadorFinanceiro = "operador-financeiro";
    public const string Admin = "admin";
}
