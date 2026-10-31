using CreditFlow.BuildingBlocks.Api;
using CreditFlow.BuildingBlocks.Auth;
using CreditFlow.Modules.Pagamentos.Application.Consultas;
using CreditFlow.Modules.Pagamentos.Application.Parcelas;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

namespace CreditFlow.Modules.Pagamentos.Api;

/// <summary>Endpoints de Pagamentos (perfil api-pagamentos; o Ingress manda /v1/pagamentos para cá).</summary>
internal static class PagamentosEndpoints
{
    public static void Mapear(IEndpointRouteBuilder endpoints)
    {
        var grupo = endpoints.MapGroup("/v1/pagamentos").WithTags("Pagamentos");

        grupo.MapGet("/desembolsos/{id:guid}", async (Guid id, ConsultasDePagamentos consultas, CancellationToken ct) =>
                (await consultas.ObterDesembolsoAsync(id, ct)).ParaHttp(d => TypedResults.Ok(d)))
            .RequireAuthorization(Politicas.ConsultarPagamentos)
            .WithName("ObterDesembolso")
            .WithSummary("Situação de um desembolso");

        grupo.MapGet("/desembolsos", async ([FromQuery] Guid propostaId, ConsultasDePagamentos consultas, CancellationToken ct) =>
                (await consultas.ObterDesembolsoPorPropostaAsync(propostaId, ct)).ParaHttp(d => TypedResults.Ok(d)))
            .RequireAuthorization(Politicas.ConsultarPagamentos)
            .WithName("ObterDesembolsoDaProposta")
            .WithSummary("Desembolso de uma proposta (?propostaId=). 404 enquanto o ContratoAssinado não foi consumido.");

        grupo.MapGet("/propostas/{propostaId:guid}/parcelas", async (Guid propostaId, ConsultasDePagamentos consultas, CancellationToken ct) =>
                TypedResults.Ok(await consultas.ListarParcelasAsync(propostaId, ct)))
            .RequireAuthorization(Politicas.ConsultarPagamentos)
            .WithName("ListarParcelas")
            .WithSummary("Parcelas do contrato (vazio até o desembolso concluir)");

        grupo.MapPost("/parcelas/{id:guid}/pagamento", async (
                Guid id,
                [FromHeader(Name = "Idempotency-Key")] string? chave,
                PagamentoDeParcelaRequisicao corpo,
                PagarParcela casoDeUso,
                HttpContext http,
                CancellationToken ct) =>
            {
                var resultado = await casoDeUso.ExecutarAsync(chave ?? "", new PagarParcelaComando(id, corpo.ValorCentavos), ct);
                return resultado.ParaHttp(r =>
                {
                    // Sinaliza a repetição sem mudar o status: o cliente que reenviou recebe o mesmo 200.
                    if (r.Repetida) http.Response.Headers["Idempotent-Replayed"] = "true";
                    return TypedResults.Ok(r.Valor);
                });
            })
            .RequireAuthorization(Politicas.OperarPagamentos)
            .WithName("PagarParcela")
            .WithSummary("Baixa de parcela. Exige o cabeçalho Idempotency-Key.");
    }
}

public sealed record PagamentoDeParcelaRequisicao(long ValorCentavos);
