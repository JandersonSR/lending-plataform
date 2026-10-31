using CreditFlow.BuildingBlocks.Auth;
using CreditFlow.Modules.Notificacoes.Application;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace CreditFlow.Modules.Notificacoes.Api;

public sealed record NotificacaoResposta(Guid Id, string Canal, string Tipo, string Titulo, string Status, DateTimeOffset CriadaEm, DateTimeOffset? EnviadaEm);

internal static class NotificacoesEndpoints
{
    public static void Mapear(IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/v1/clientes/{clienteId:guid}/notificacoes", async (Guid clienteId, ListarNotificacoes caso, CancellationToken ct) =>
                TypedResults.Ok((await caso.DoClienteAsync(clienteId, ct))
                    .Select(n => new NotificacaoResposta(n.Id, n.Canal.ToString(), n.Tipo, n.Titulo, n.Status.ToString(), n.CriadaEm, n.EnviadaEm))
                    .ToList()))
            .RequireAuthorization(Politicas.AnalisarProposta) // suporte/analista consultam o que foi avisado
            .WithTags("Notificacoes")
            .WithName("NotificacoesDoCliente")
            .WithSummary("O que foi enviado a um cliente, mais recentes primeiro.")
            .Produces<List<NotificacaoResposta>>(StatusCodes.Status200OK);
    }
}
