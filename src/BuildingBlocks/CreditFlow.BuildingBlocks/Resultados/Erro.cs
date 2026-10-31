namespace CreditFlow.BuildingBlocks;

/// <summary>Categoria do erro: decide o status HTTP, não a mensagem.</summary>
public enum TipoErro
{
    /// <summary>Entrada inválida (400).</summary>
    Validacao,
    /// <summary>O recurso não existe (404).</summary>
    NaoEncontrado,
    /// <summary>Estado atual impede a operação, ex.: duplicidade (409).</summary>
    Conflito,
    /// <summary>Entrada bem formada, mas uma regra de negócio recusa (422).</summary>
    Regra,
    /// <summary>Uma dependência necessária está fora (503).</summary>
    Indisponivel,
}

/// <summary>
/// Falha PREVISTA de um caso de uso (valor fora da faixa, cliente inexistente...). Não é exceção:
/// exceção fica para o que ninguém previu (banco fora, bug). O <see cref="Codigo"/> é estável e vai
/// no ProblemDetails: o front e os testes dependem dele, nunca da mensagem.
/// </summary>
public sealed record Erro(string Codigo, string Mensagem, TipoErro Tipo)
{
    /// <summary>Erros por campo (só em <see cref="TipoErro.Validacao"/>), no formato do ValidationProblemDetails.</summary>
    public IReadOnlyDictionary<string, string[]>? Campos { get; init; }

    public static Erro Validacao(string codigo, string mensagem, string? campo = null) =>
        new(codigo, mensagem, TipoErro.Validacao)
        {
            Campos = campo is null ? null : new Dictionary<string, string[]> { [campo] = [mensagem] },
        };

    public static Erro NaoEncontrado(string codigo, string mensagem) => new(codigo, mensagem, TipoErro.NaoEncontrado);
    public static Erro Conflito(string codigo, string mensagem) => new(codigo, mensagem, TipoErro.Conflito);
    public static Erro Regra(string codigo, string mensagem) => new(codigo, mensagem, TipoErro.Regra);
    public static Erro Indisponivel(string codigo, string mensagem) => new(codigo, mensagem, TipoErro.Indisponivel);

    /// <summary>Junta vários erros de validação num só, somando os campos (para devolver tudo de uma vez).</summary>
    public static Erro JuntarValidacoes(IEnumerable<Erro> erros)
    {
        var lista = erros.ToList();
        if (lista.Count == 0)
            throw new ArgumentException("Nenhum erro para juntar.", nameof(erros));
        if (lista.Count == 1)
            return lista[0];

        var campos = new Dictionary<string, string[]>();
        foreach (var erro in lista)
            foreach (var (campo, mensagens) in erro.Campos ?? new Dictionary<string, string[]> { [""] = [erro.Mensagem] })
                campos[campo] = campos.TryGetValue(campo, out var atuais) ? [.. atuais, .. mensagens] : mensagens;

        return new Erro("validacao", "Um ou mais campos são inválidos.", TipoErro.Validacao) { Campos = campos };
    }
}
