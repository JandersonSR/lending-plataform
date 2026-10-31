namespace CreditFlow.BuildingBlocks.ValueObjects;

/// <summary>
/// CPF válido (11 dígitos e dígitos verificadores corretos). Se existe uma instância, ela é válida:
/// ninguém mais no sistema precisa validar CPF de novo. Guardado só com dígitos.
/// É dado pessoal (LGPD): <see cref="ToString"/> devolve a versão MASCARADA, para que um log
/// descuidado ("Cliente {Cpf}") não vaze o número inteiro.
/// </summary>
public sealed record Cpf
{
    private Cpf(string digitos) => Digitos = digitos;

    /// <summary>Os 11 dígitos, sem pontuação. Use só onde o número inteiro é necessário (persistência).</summary>
    public string Digitos { get; }

    /// <summary>"123.456.789-09"</summary>
    public string Formatado => $"{Digitos[..3]}.{Digitos[3..6]}.{Digitos[6..9]}-{Digitos[9..]}";

    /// <summary>"***.456.789-**": o formato que a própria Receita usa em documentos públicos.</summary>
    public string Mascarado => $"***.{Digitos[3..6]}.{Digitos[6..9]}-**";

    public static Result<Cpf> Criar(string? texto, string campo = "cpf")
    {
        if (string.IsNullOrWhiteSpace(texto))
            return Erro.Validacao("cpf.obrigatorio", "O CPF é obrigatório.", campo);

        var digitos = new string(texto.Where(char.IsAsciiDigit).ToArray());
        var soPontuacaoPermitida = texto.All(c => char.IsAsciiDigit(c) || c is '.' or '-' or ' ');
        if (!soPontuacaoPermitida || digitos.Length != 11)
            return Erro.Validacao("cpf.formato", "O CPF deve ter 11 dígitos.", campo);

        // 000.000.000-00, 111.111.111-11...: passam na conta dos dígitos, mas não são CPFs.
        if (digitos.Distinct().Count() == 1)
            return Erro.Validacao("cpf.invalido", "CPF inválido.", campo);

        if (Digito(digitos, 9) != digitos[9] - '0' || Digito(digitos, 10) != digitos[10] - '0')
            return Erro.Validacao("cpf.invalido", "CPF inválido.", campo);

        return new Cpf(digitos);
    }

    /// <summary>Dígito verificador (módulo 11) calculado sobre os primeiros <paramref name="quantos"/> dígitos.</summary>
    private static int Digito(string digitos, int quantos)
    {
        var soma = 0;
        for (var i = 0; i < quantos; i++)
            soma += (digitos[i] - '0') * (quantos + 1 - i);
        var resto = soma % 11;
        return resto < 2 ? 0 : 11 - resto;
    }

    public override string ToString() => Mascarado;
}
