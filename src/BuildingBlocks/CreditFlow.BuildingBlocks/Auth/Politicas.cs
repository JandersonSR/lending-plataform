using Microsoft.AspNetCore.Authorization;

namespace CreditFlow.BuildingBlocks.Auth;

/// <summary>
/// Policies por caso de uso. Os endpoints pedem a policy (o "o quê"), nunca a role (o "quem"):
/// quando a regra de quem pode mudar, muda aqui, num lugar só.
/// </summary>
public static class Politicas
{
    public const string Autenticado = "autenticado";
    public const string CriarProposta = "propostas:criar";
    public const string ConsultarPropostas = "propostas:consultar";
    public const string AnalisarProposta = "propostas:analisar";
    public const string ContratarProposta = "propostas:contratar";
    public const string OperarPagamentos = "pagamentos:operar";
    public const string ConsultarPagamentos = "pagamentos:consultar";
    public const string Administrar = "admin";

    internal static void Registrar(AuthorizationOptions o)
    {
        o.AddPolicy(Autenticado, p => p.RequireAuthenticatedUser());
        o.AddPolicy(CriarProposta, p => p.RequireAuthenticatedUser().RequireRole(Papeis.Cliente));
        o.AddPolicy(ContratarProposta, p => p.RequireAuthenticatedUser().RequireRole(Papeis.Cliente));
        // o dono da proposta é conferido no handler (regra de recurso), não aqui
        o.AddPolicy(ConsultarPropostas, p => p.RequireAuthenticatedUser().RequireRole(Papeis.Cliente, Papeis.Analista, Papeis.Admin));
        o.AddPolicy(AnalisarProposta, p => p.RequireAuthenticatedUser().RequireRole(Papeis.Analista, Papeis.Admin));
        o.AddPolicy(OperarPagamentos, p => p.RequireAuthenticatedUser().RequireRole(Papeis.OperadorFinanceiro));
        o.AddPolicy(ConsultarPagamentos, p => p.RequireAuthenticatedUser().RequireRole(Papeis.OperadorFinanceiro, Papeis.Analista, Papeis.Admin, Papeis.Cliente));
        o.AddPolicy(Administrar, p => p.RequireAuthenticatedUser().RequireRole(Papeis.Admin));
    }
}
