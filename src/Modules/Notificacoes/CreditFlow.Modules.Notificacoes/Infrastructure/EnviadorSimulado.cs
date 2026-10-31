using CreditFlow.Modules.Notificacoes.Application;
using CreditFlow.Modules.Notificacoes.Domain;
using Microsoft.Extensions.Logging;

namespace CreditFlow.Modules.Notificacoes.Infrastructure;

/// <summary>Provedor fictício: registra no log o que enviaria. Trocar por SES/SendGrid/Twilio é trocar esta classe.</summary>
internal sealed partial class EnviadorSimulado(ILogger<EnviadorSimulado> log) : IEnviadorDeNotificacoes
{
    public Task EnviarAsync(Notificacao n, CancellationToken ct)
    {
        Enviado(log, n.Canal.ToString(), n.ClienteId, n.Tipo);
        return Task.CompletedTask;
    }

    // Sem o texto da mensagem e sem dado pessoal no log (LGPD): só o necessário para rastrear.
    [LoggerMessage(Level = LogLevel.Information, Message = "Notificação {Canal} enviada ao cliente {ClienteId}: {Tipo}")]
    private static partial void Enviado(ILogger logger, string canal, Guid clienteId, string tipo);
}
