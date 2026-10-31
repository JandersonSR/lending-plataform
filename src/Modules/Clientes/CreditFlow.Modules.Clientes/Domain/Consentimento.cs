namespace CreditFlow.Modules.Clientes.Domain;

/// <summary>Para que o dado pessoal pode ser usado. LGPD: consentimento é por finalidade, nunca "geral".</summary>
public enum FinalidadeDoConsentimento
{
    AnaliseDeCredito = 1,
    OfertasPorEmail = 2,
}

/// <summary>
/// Registro de um consentimento: quem, para quê, em que versão do termo, quando, e se foi revogado.
/// Nunca é apagado ao revogar: a LGPD exige provar que o consentimento existia quando o dado foi usado.
/// </summary>
public sealed class Consentimento
{
    private Consentimento() { } // EF

    internal Consentimento(FinalidadeDoConsentimento finalidade, string versaoDoTermo, DateTimeOffset concedidoEm)
    {
        Id = Guid.CreateVersion7();
        Finalidade = finalidade;
        VersaoDoTermo = versaoDoTermo;
        ConcedidoEm = concedidoEm;
    }

    public Guid Id { get; private set; }
    public FinalidadeDoConsentimento Finalidade { get; private set; }
    public string VersaoDoTermo { get; private set; } = "";
    public DateTimeOffset ConcedidoEm { get; private set; }
    public DateTimeOffset? RevogadoEm { get; private set; }

    public bool Ativo => RevogadoEm is null;

    internal void Revogar(DateTimeOffset agora) => RevogadoEm ??= agora;
}
