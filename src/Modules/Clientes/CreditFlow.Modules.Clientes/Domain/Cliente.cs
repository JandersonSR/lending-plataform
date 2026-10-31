using CreditFlow.BuildingBlocks;
using CreditFlow.BuildingBlocks.ValueObjects;

namespace CreditFlow.Modules.Clientes.Domain;

/// <summary>
/// Agregado Cliente. Invariantes que ele protege:
/// 1. nome, CPF e renda sempre válidos (só se cria por <see cref="Cadastrar"/>);
/// 2. não existe cliente sem consentimento para análise de crédito no momento do cadastro;
/// 3. no máximo UM consentimento ativo por finalidade; revogar não apaga o histórico.
/// A unicidade do CPF não cabe aqui (exige olhar outros clientes): é do caso de uso + índice único.
/// </summary>
public sealed class Cliente
{
    private readonly List<Consentimento> _consentimentos = [];

    private Cliente() { } // EF

    public ClienteId Id { get; private set; }
    public string Nome { get; private set; } = "";
    public Cpf Cpf { get; private set; } = null!;
    /// <summary>Claim <c>sub</c> do Keycloak de quem se cadastrou: liga o login ao cliente (um para um).</summary>
    public string? UsuarioId { get; private set; }
    public Dinheiro RendaMensal { get; private set; }
    public int Score { get; private set; }
    public DateTimeOffset CadastradoEm { get; private set; }

    public IReadOnlyList<Consentimento> Consentimentos => _consentimentos;

    public bool PodeUsarParaAnaliseDeCredito => TemConsentimentoAtivo(FinalidadeDoConsentimento.AnaliseDeCredito);

    public static Result<Cliente> Cadastrar(
        string? nome, Cpf cpf, decimal rendaMensal, bool aceitaAnaliseDeCredito, string? versaoDoTermo,
        bool aceitaOfertasPorEmail, DateTimeOffset agora, string? usuarioId = null)
    {
        ArgumentNullException.ThrowIfNull(cpf);

        var nomeLimpo = nome?.Trim() ?? "";
        if (nomeLimpo.Length is < 2 or > 120)
            return ClientesErros.NomeObrigatorio;

        var renda = Dinheiro.Criar(rendaMensal, "rendaMensal");
        if (renda.Falhou || renda.Valor.EhZero)
            return ClientesErros.RendaInvalida;

        if (string.IsNullOrWhiteSpace(versaoDoTermo))
            return ClientesErros.VersaoDoTermoObrigatoria;

        if (!aceitaAnaliseDeCredito)
            return ClientesErros.ConsentimentoObrigatorio;

        var cliente = new Cliente
        {
            Id = ClienteId.Novo(),
            Nome = nomeLimpo,
            Cpf = cpf,
            UsuarioId = string.IsNullOrWhiteSpace(usuarioId) ? null : usuarioId,
            RendaMensal = renda.Valor,
            Score = ScoreFicticio.Calcular(cpf, renda.Valor),
            CadastradoEm = agora,
        };
        cliente.Consentir(FinalidadeDoConsentimento.AnaliseDeCredito, versaoDoTermo.Trim(), agora);
        if (aceitaOfertasPorEmail)
            cliente.Consentir(FinalidadeDoConsentimento.OfertasPorEmail, versaoDoTermo.Trim(), agora);
        return cliente;
    }

    /// <summary>Concede (ou renova) o consentimento. Idempotente: com um ativo para a finalidade, não cria outro.</summary>
    public void Consentir(FinalidadeDoConsentimento finalidade, string versaoDoTermo, DateTimeOffset agora)
    {
        if (TemConsentimentoAtivo(finalidade))
            return;
        _consentimentos.Add(new Consentimento(finalidade, versaoDoTermo, agora));
    }

    /// <summary>Revoga o consentimento ativo da finalidade. Revogar o que não existe não é erro (idempotente).</summary>
    public void Revogar(FinalidadeDoConsentimento finalidade, DateTimeOffset agora)
    {
        foreach (var c in _consentimentos.Where(c => c.Finalidade == finalidade && c.Ativo))
            c.Revogar(agora);
    }

    private bool TemConsentimentoAtivo(FinalidadeDoConsentimento finalidade) =>
        _consentimentos.Any(c => c.Finalidade == finalidade && c.Ativo);
}
