using System.Globalization;
using CreditFlow.BuildingBlocks.Api;
using CreditFlow.BuildingBlocks.Auth;
using CreditFlow.Modules.Propostas.Application;
using CreditFlow.Modules.Propostas.Domain;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace CreditFlow.Modules.Propostas.Api;

public sealed record CriarPropostaRequest(Guid ClienteId, Guid SimulacaoId);
public sealed record DecisaoRequest(string? Texto);
public sealed record ContratarRequest(string? ChavePix);

public sealed record ParcelaResposta(int Numero, decimal Valor);

public sealed record PropostaResposta(
    Guid Id, Guid ClienteId, Guid SimulacaoId, string Status,
    decimal ValorSolicitado, int PrazoMeses, decimal TaxaMensalPercentual, decimal CetAnualPercentual, string Sistema,
    decimal MaiorParcela, decimal Comprometimento, bool DecisaoAutomatica, string? CodigoMotivo, string? Justificativa,
    string? AnalisadoPor, DateTimeOffset CriadaEm, DateTimeOffset? DecididaEm, DateTimeOffset? ValidaAte,
    DateTimeOffset? ContratadaEm, IReadOnlyList<ParcelaResposta> Parcelas, string Versao)
{
    internal static PropostaResposta De(Proposta p) => new(
        p.Id, p.ClienteId, p.SimulacaoId, p.Status.ToString(),
        p.ValorSolicitado.EmCentavos().Valor, p.PrazoMeses, p.TaxaMensal.PontosArredondados(4), p.CetAnual.PontosArredondados(4), p.Sistema,
        p.MaiorParcela.Valor, p.Comprometimento, p.DecisaoAutomatica, p.CodigoMotivo, p.Justificativa,
        p.AnalisadoPor, p.CriadaEm, p.DecididaEm, p.ValidaAte, p.ContratadaEm,
        p.Parcelas.Select(x => new ParcelaResposta(x.Numero, x.ValorCentavos / 100m)).ToList(),
        p.Versao.ToString(CultureInfo.InvariantCulture));
}

internal static class PropostasEndpoints
{
    public static void Mapear(IEndpointRouteBuilder endpoints)
    {
        var grupo = endpoints.MapGroup("/v1/propostas").WithTags("Propostas");

        grupo.MapPost("/", async (CriarPropostaRequest corpo, CriarProposta caso, CancellationToken ct) =>
                (await caso.ExecutarAsync(new CriarPropostaComando(corpo.ClienteId, corpo.SimulacaoId), ct))
                    .ParaHttp(p => ComEtag(TypedResults.Created($"/v1/propostas/{p.Id}", PropostaResposta.De(p)), p)))
            .RequireAuthorization(Politicas.CriarProposta)
            .WithName("CriarProposta")
            .WithSummary("Pede crédito a partir de uma simulação; a análise automática roda na hora.")
            .Produces<PropostaResposta>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        grupo.MapGet("/", async (ListarPropostas caso, CancellationToken ct) =>
                TypedResults.Ok((await caso.MinhasAsync(ct)).Select(PropostaResposta.De).ToList()))
            .RequireAuthorization(Politicas.CriarProposta)
            .WithName("MinhasPropostas")
            .WithSummary("Propostas do usuário autenticado.");

        grupo.MapGet("/fila-de-analise", async (ListarPropostas caso, CancellationToken ct) =>
                TypedResults.Ok((await caso.FilaDeAnaliseAsync(ct)).Select(PropostaResposta.De).ToList()))
            .RequireAuthorization(Politicas.AnalisarProposta)
            .WithName("FilaDeAnalise")
            .WithSummary("Propostas esperando analista, mais antigas primeiro.");

        grupo.MapGet("/{id:guid}", async (Guid id, ConsultarProposta caso, CancellationToken ct) =>
                (await caso.ExecutarAsync(id, ct)).ParaHttp(p => ComEtag(TypedResults.Ok(PropostaResposta.De(p)), p)))
            .RequireAuthorization(Politicas.ConsultarPropostas)
            .WithName("ConsultarProposta")
            .WithSummary("Uma proposta. Devolve ETag (a versão) para usar no If-Match das decisões.")
            .Produces<PropostaResposta>()
            .ProducesProblem(StatusCodes.Status404NotFound);

        grupo.MapPost("/{id:guid}/aprovar", (Guid id, DecisaoRequest corpo, HttpRequest req, DecidirProposta caso, CancellationToken ct) =>
                Decidir(id, AcaoDoAnalista.Aprovar, corpo, req, caso, ct))
            .RequireAuthorization(Politicas.AnalisarProposta)
            .WithName("AprovarProposta")
            .WithSummary("Analista aprova. Envie If-Match com a versão que você viu.")
            .Produces<PropostaResposta>()
            .ProducesProblem(StatusCodes.Status409Conflict);

        grupo.MapPost("/{id:guid}/recusar", (Guid id, DecisaoRequest corpo, HttpRequest req, DecidirProposta caso, CancellationToken ct) =>
                Decidir(id, AcaoDoAnalista.Recusar, corpo, req, caso, ct))
            .RequireAuthorization(Politicas.AnalisarProposta)
            .WithName("RecusarProposta")
            .WithSummary("Analista recusa, com motivo. Envie If-Match com a versão que você viu.")
            .Produces<PropostaResposta>()
            .ProducesProblem(StatusCodes.Status409Conflict);

        grupo.MapPost("/{id:guid}/contratar", async (Guid id, ContratarRequest corpo, ContratarProposta caso, CancellationToken ct) =>
                (await caso.ExecutarAsync(id, corpo.ChavePix, ct)).ParaHttp(p => ComEtag(TypedResults.Ok(PropostaResposta.De(p)), p)))
            .RequireAuthorization(Politicas.ContratarProposta)
            .WithName("ContratarProposta")
            .WithSummary("O cliente assina a proposta aprovada; começa a saga de desembolso.")
            .Produces<PropostaResposta>()
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        grupo.MapPost("/{id:guid}/cancelar", async (Guid id, CancelarProposta caso, CancellationToken ct) =>
                (await caso.ExecutarAsync(id, ct)).ParaHttp(p => ComEtag(TypedResults.Ok(PropostaResposta.De(p)), p)))
            .RequireAuthorization(Politicas.CriarProposta)
            .WithName("CancelarProposta")
            .Produces<PropostaResposta>()
            .ProducesProblem(StatusCodes.Status409Conflict);
    }

    private static async Task<IResult> Decidir(Guid id, AcaoDoAnalista acao, DecisaoRequest corpo, HttpRequest req, DecidirProposta caso, CancellationToken ct)
    {
        uint? versao = null;
        var ifMatch = req.Headers.IfMatch.ToString();
        if (!string.IsNullOrWhiteSpace(ifMatch))
        {
            if (!uint.TryParse(ifMatch.Replace("W/", "", StringComparison.Ordinal).Trim('"', ' '), NumberStyles.None, CultureInfo.InvariantCulture, out var v))
                return PropostasErros.VersaoInvalida.ParaProblema();
            versao = v;
        }
        return (await caso.ExecutarAsync(id, acao, corpo.Texto, versao, ct))
            .ParaHttp(p => ComEtag(TypedResults.Ok(PropostaResposta.De(p)), p));
    }

    /// <summary>ETag = xmin. O cliente devolve no If-Match: "decido sobre a versão que eu vi".</summary>
    private static IResult ComEtag(IResult resultado, Proposta p) => new ResultadoComEtag(resultado, p.Versao);

    private sealed class ResultadoComEtag(IResult interno, uint versao) : IResult
    {
        public Task ExecuteAsync(HttpContext httpContext)
        {
            httpContext.Response.Headers.ETag = $"\"{versao.ToString(CultureInfo.InvariantCulture)}\"";
            return interno.ExecuteAsync(httpContext);
        }
    }
}
