namespace CreditFlow.Modules.Propostas.Domain;

/// <summary>
/// Estados da proposta. Gravado como texto no banco (nunca como número: reordenar o enum
/// trocaria o significado das linhas antigas sem nenhum erro).
/// </summary>
public enum StatusProposta
{
    /// <summary>A análise automática não decidiu; espera um analista.</summary>
    EmAnalise,
    /// <summary>Aprovada (automática ou por analista). Vale até <see cref="Proposta.ValidaAte"/> para contratar.</summary>
    Aprovada,
    /// <summary>Final. Regra automática ou analista recusou.</summary>
    Recusada,
    /// <summary>Cliente assinou. A saga de desembolso começa aqui (evento ContratoAssinado).</summary>
    Contratada,
    /// <summary>Final. O dinheiro saiu (DesembolsoConcluido).</summary>
    Liberada,
    /// <summary>Final. A saga compensou: o desembolso falhou e a contratação foi desfeita.</summary>
    DesembolsoFalhou,
    /// <summary>Final. O cliente desistiu antes de contratar.</summary>
    Cancelada,
    /// <summary>Final. A aprovação venceu sem contratação.</summary>
    Expirada,
}

public static class StatusPropostaExtensions
{
    /// <summary>Estados em que a proposta ainda "ocupa" o cliente: no máximo uma por cliente (índice parcial no banco).</summary>
    public static bool Ativa(this StatusProposta s) =>
        s is StatusProposta.EmAnalise or StatusProposta.Aprovada or StatusProposta.Contratada;

    public static bool Final(this StatusProposta s) =>
        s is StatusProposta.Recusada or StatusProposta.Liberada or StatusProposta.DesembolsoFalhou
            or StatusProposta.Cancelada or StatusProposta.Expirada;
}
