using System.Data.Common;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using MongoDB.Driver;

namespace CreditFlow.Host;

/// <summary>
/// Banco, Mongo ou outra dependência fora (timeout, conexão recusada, falha transitória) não é bug: é 503
/// com <c>Retry-After</c>, para o cliente e o Ingress saberem que vale tentar de novo. Qualquer outra exceção
/// segue para o tratador padrão (500 em ProblemDetails, sem pilha).
/// </summary>
internal sealed partial class DependenciaIndisponivelHandler(
    IProblemDetailsService problemas, ILogger<DependenciaIndisponivelHandler> log) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext http, Exception exception, CancellationToken cancellationToken)
    {
        if (!EhIndisponibilidade(exception))
            return false;

        DependenciaFora(log, exception.GetType().Name, exception);
        http.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
        http.Response.Headers.RetryAfter = "5";
        return await problemas.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = http,
            Exception = exception,
            ProblemDetails = new ProblemDetails
            {
                Status = StatusCodes.Status503ServiceUnavailable,
                Title = "Serviço indisponível",
                Detail = "Uma dependência do sistema está temporariamente fora. Tente de novo em alguns segundos.",
                Type = "urn:creditflow:erro:dependencia_indisponivel",
                Extensions = { ["code"] = "dependencia_indisponivel" },
            },
        });
    }

    internal static bool EhIndisponibilidade(Exception? ex)
    {
        for (var e = ex; e is not null; e = e.InnerException)
            if (e is TimeoutException or MongoConnectionException or DbException { IsTransient: true })
                return true;
        return false;
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Dependência indisponível ({Tipo}); respondendo 503")]
    private static partial void DependenciaFora(ILogger logger, string tipo, Exception ex);
}
