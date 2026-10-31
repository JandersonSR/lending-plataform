using CreditFlow.BuildingBlocks.Api;
using CreditFlow.BuildingBlocks.Auth;
using CreditFlow.Modules.Clientes.Application;
using CreditFlow.Modules.Clientes.Domain;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace CreditFlow.Modules.Clientes.Api;

/// <summary>Corpo do POST /v1/clientes. Tipos primitivos: quem valida é o domínio (Cpf, Dinheiro, Cliente).</summary>
public sealed record CadastrarClienteRequest(
    string? Nome,
    string? Cpf,
    decimal RendaMensal,
    bool AceitaAnaliseDeCredito,
    string? VersaoDoTermo,
    bool AceitaOfertasPorEmail = false);

public sealed record ConsentimentoResponse(string Finalidade, string VersaoDoTermo, DateTimeOffset ConcedidoEm, DateTimeOffset? RevogadoEm);

/// <summary>Resposta: CPF SEMPRE mascarado. A API não devolve o dado pessoal inteiro para ninguém.</summary>
public sealed record ClienteResponse(
    Guid Id,
    string Nome,
    string CpfMascarado,
    decimal RendaMensal,
    int Score,
    DateTimeOffset CadastradoEm,
    IReadOnlyList<ConsentimentoResponse> Consentimentos)
{
    internal static ClienteResponse De(Cliente c) => new(
        c.Id.Valor, c.Nome, c.Cpf.Mascarado, c.RendaMensal.Valor, c.Score, c.CadastradoEm,
        [.. c.Consentimentos.Select(s => new ConsentimentoResponse(s.Finalidade.ToString(), s.VersaoDoTermo, s.ConcedidoEm, s.RevogadoEm))]);
}

internal static class ClientesEndpoints
{
    public static void Mapear(IEndpointRouteBuilder endpoints)
    {
        var grupo = endpoints.MapGroup("/v1/clientes")
            .WithTags("Clientes")
            .RequireAuthorization(Politicas.Autenticado);

        grupo.MapPost("/", async (CadastrarClienteRequest req, CadastrarCliente casoDeUso, ICurrentUser usuario, CancellationToken ct) =>
            {
                // O vínculo login → cliente vem do TOKEN (sub), nunca do corpo. Conta de serviço (M2M) cadastra sem vínculo.
                var resultado = await casoDeUso.ExecutarAsync(new CadastrarClienteComando(
                    req.Nome, req.Cpf, req.RendaMensal, req.AceitaAnaliseDeCredito, req.VersaoDoTermo, req.AceitaOfertasPorEmail,
                    usuario.EhMaquina ? null : usuario.Id), ct);
                return resultado.ParaHttp(c => TypedResults.Created($"/v1/clientes/{c.Id}", ClienteResponse.De(c)));
            })
            .WithName("CadastrarCliente")
            .WithSummary("Cadastra um cliente com consentimento LGPD para análise de crédito")
            .Produces<ClienteResponse>(StatusCodes.Status201Created)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        grupo.MapGet("/eu", async (ObterClienteDoUsuario casoDeUso, ICurrentUser usuario, CancellationToken ct) =>
                (await casoDeUso.ExecutarAsync(usuario.Id, ct)).ParaHttp(c => TypedResults.Ok(ClienteResponse.De(c))))
            .WithName("ObterMeuCadastro")
            .WithSummary("O cadastro de cliente ligado ao usuário do token")
            .Produces<ClienteResponse>()
            .ProducesProblem(StatusCodes.Status404NotFound);

        grupo.MapGet("/{id:guid}", async (Guid id, ObterCliente casoDeUso, CancellationToken ct) =>
                (await casoDeUso.ExecutarAsync(id, ct)).ParaHttp(c => TypedResults.Ok(ClienteResponse.De(c))))
            .WithName("ObterCliente")
            .WithSummary("Obtém um cliente (CPF mascarado)")
            .Produces<ClienteResponse>()
            .ProducesProblem(StatusCodes.Status404NotFound);

        grupo.MapDelete("/{id:guid}/consentimentos/{finalidade}", async (Guid id, string finalidade, RevogarConsentimento casoDeUso, CancellationToken ct) =>
            {
                var resultado = await casoDeUso.ExecutarAsync(id, finalidade, ct);
                return resultado.Sucesso ? TypedResults.NoContent() : resultado.Erro.ParaProblema();
            })
            .WithName("RevogarConsentimento")
            .WithSummary("Revoga o consentimento de uma finalidade (AnaliseDeCredito ou OfertasPorEmail)")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound);
    }
}
