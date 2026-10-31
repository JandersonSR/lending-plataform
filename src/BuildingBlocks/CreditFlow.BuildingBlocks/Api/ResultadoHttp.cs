using Microsoft.AspNetCore.Http;

namespace CreditFlow.BuildingBlocks.Api;

/// <summary>
/// Tradução de <see cref="Erro"/> para HTTP, num lugar só. O caso de uso não sabe que existe HTTP;
/// o endpoint não decide status code caso a caso. Formato: ProblemDetails (RFC 9457) com o
/// <c>code</c> estável como extensão.
/// </summary>
public static class ResultadoHttp
{
    /// <summary>
    /// O "type" do ProblemDetails é uma URI que identifica o TIPO do problema. Uma URN não promete
    /// uma página que talvez não exista; troque por uma URL de documentação quando houver uma.
    /// </summary>
    public const string TipoBase = "urn:creditflow:erro:";

    public static int StatusDe(TipoErro tipo) => tipo switch
    {
        TipoErro.Validacao => StatusCodes.Status400BadRequest,
        TipoErro.NaoEncontrado => StatusCodes.Status404NotFound,
        TipoErro.Conflito => StatusCodes.Status409Conflict,
        TipoErro.Regra => StatusCodes.Status422UnprocessableEntity,
        TipoErro.Indisponivel => StatusCodes.Status503ServiceUnavailable,
        _ => StatusCodes.Status500InternalServerError,
    };

    public static IResult ParaProblema(this Erro erro)
    {
        ArgumentNullException.ThrowIfNull(erro);
        var status = StatusDe(erro.Tipo);
        var extensoes = new Dictionary<string, object?> { ["code"] = erro.Codigo };

        if (erro.Tipo == TipoErro.Validacao && erro.Campos is { Count: > 0 } campos)
            return TypedResults.ValidationProblem(
                campos.ToDictionary(c => c.Key, c => c.Value),
                detail: erro.Mensagem,
                title: "Requisição inválida",
                type: TipoBase + erro.Codigo,
                extensions: extensoes);

        return TypedResults.Problem(
            detail: erro.Mensagem,
            statusCode: status,
            title: TituloDe(erro.Tipo),
            type: TipoBase + erro.Codigo,
            extensions: extensoes);
    }

    /// <summary>Atalho para endpoints: sucesso vira <paramref name="sucesso"/>(valor); falha vira ProblemDetails.</summary>
    public static IResult ParaHttp<T>(this Result<T> resultado, Func<T, IResult> sucesso)
    {
        ArgumentNullException.ThrowIfNull(resultado);
        ArgumentNullException.ThrowIfNull(sucesso);
        return resultado.Sucesso ? sucesso(resultado.Valor) : resultado.Erro.ParaProblema();
    }

    private static string TituloDe(TipoErro tipo) => tipo switch
    {
        TipoErro.Validacao => "Requisição inválida",
        TipoErro.NaoEncontrado => "Não encontrado",
        TipoErro.Conflito => "Conflito",
        TipoErro.Regra => "Regra de negócio",
        TipoErro.Indisponivel => "Serviço indisponível",
        _ => "Erro",
    };
}
