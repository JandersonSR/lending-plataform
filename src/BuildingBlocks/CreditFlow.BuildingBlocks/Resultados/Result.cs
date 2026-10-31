using System.Diagnostics.CodeAnalysis;

namespace CreditFlow.BuildingBlocks;

/// <summary>
/// Resultado de uma operação que pode falhar de forma prevista. Quem chama é obrigado a olhar
/// <see cref="Sucesso"/> antes de seguir: a falha faz parte da assinatura, não de um try/catch.
/// </summary>
public class Result
{
    private static readonly Result OkSemValor = new(null);

    protected Result(Erro? erro) => Erro = erro;

    /// <summary>Preenchido quando <see cref="Sucesso"/> é false.</summary>
    public Erro? Erro { get; }

    [MemberNotNullWhen(false, nameof(Erro))]
    public bool Sucesso => Erro is null;

    [MemberNotNullWhen(true, nameof(Erro))]
    public bool Falhou => Erro is not null;

    public static Result Ok() => OkSemValor;
    public static Result Falha(Erro erro) => new(erro ?? throw new ArgumentNullException(nameof(erro)));
    public static Result<T> Ok<T>(T valor) => Result<T>.Ok(valor);
    public static Result<T> Falha<T>(Erro erro) => Result<T>.Falha(erro);

    public static implicit operator Result(Erro erro) => Falha(erro);
}

/// <summary>Resultado com valor. Ler <see cref="Valor"/> numa falha é bug do chamador e lança exceção.</summary>
public sealed class Result<T> : Result
{
    private readonly T? _valor;

    private Result(T? valor, Erro? erro) : base(erro) => _valor = valor;

    public T Valor => Sucesso
        ? _valor!
        : throw new InvalidOperationException($"Resultado com falha ({Erro.Codigo}) não tem valor. Verifique Sucesso antes.");

    public static Result<T> Ok(T valor) => new(valor, null);
    public static new Result<T> Falha(Erro erro) => new(default, erro ?? throw new ArgumentNullException(nameof(erro)));

    /// <summary>Aplica <paramref name="mapear"/> no valor se deu certo; propaga o erro se não.</summary>
    public Result<TNovo> Map<TNovo>(Func<T, TNovo> mapear) =>
        Sucesso ? Result<TNovo>.Ok(mapear(Valor)) : Result<TNovo>.Falha(Erro);

    public static implicit operator Result<T>(T valor) => Ok(valor);
    public static implicit operator Result<T>(Erro erro) => Falha(erro);
}
