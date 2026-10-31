namespace CreditFlow.Modules.Clientes.Contracts;

/// <summary>
/// Consulta síncrona, in-process, que outros módulos (Simulacao, Propostas) usam para saber o perfil
/// de crédito de um cliente. Quem implementa é o módulo Clientes; quem usa só conhece esta interface.
/// </summary>
public interface IConsultaDeClientes
{
    /// <summary>Perfil de crédito, ou null se o cliente não existe.</summary>
    Task<PerfilDeCreditoDto?> ObterPerfilDeCreditoAsync(Guid clienteId, CancellationToken ct = default);

    /// <summary>
    /// Perfil do cliente ligado a um login (claim <c>sub</c> do Keycloak), ou null se o usuário ainda não se
    /// cadastrou. Permite a outro módulo derivar o cliente do TOKEN, em vez de confiar num clienteId do corpo.
    /// </summary>
    Task<PerfilDeCreditoDto?> ObterPerfilPorUsuarioAsync(string usuarioId, CancellationToken ct = default);
}

/// <summary>
/// O mínimo que outro módulo precisa para decidir crédito. Sem CPF nem nome: quem não precisa
/// do dado pessoal não recebe o dado pessoal (LGPD, princípio da necessidade).
/// </summary>
/// <param name="ClienteId">Id do cliente.</param>
/// <param name="Score">Score fictício de 0 a 1000. Só use se <paramref name="ConsentimentoAnaliseDeCredito"/> for true.</param>
/// <param name="RendaMensalCentavos">Renda declarada, em centavos.</param>
/// <param name="ConsentimentoAnaliseDeCredito">O cliente autorizou (e não revogou) o uso dos dados para análise de crédito.</param>
public sealed record PerfilDeCreditoDto(
    Guid ClienteId,
    int Score,
    long RendaMensalCentavos,
    bool ConsentimentoAnaliseDeCredito);
