using CreditFlow.BuildingBlocks.Mensageria;
using CreditFlow.Modules.Simulacao.Application;
using CreditFlow.Modules.Simulacao.Domain;
using Microsoft.Extensions.Logging;

namespace CreditFlow.Modules.Simulacao.Infrastructure.Eventos;

/// <summary>Publica SimulacaoRealizada direto no Kafka (sem outbox), sem segurar a requisição.</summary>
internal sealed partial class PublicadorDeSimulacoesKafka(IPublicadorDeEventos publicador, ILogger<PublicadorDeSimulacoesKafka> log)
    : IPublicadorDeSimulacoes
{
    public void Publicar(SimulacaoDeEmprestimo simulacao)
    {
        try
        {
            publicador.PublicarSemEsperar(MapeamentoDeContratos.ParaEvento(simulacao), simulacao.Id.Valor.ToString());
        }
#pragma warning disable CA1031 // estatística: nenhuma falha de publicação pode virar 500 na simulação
        catch (Exception ex)
#pragma warning restore CA1031
        {
            FalhaAoPublicar(log, simulacao.Id.Valor, ex);
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "SimulacaoRealizada {SimulacaoId} não foi publicada")]
    private static partial void FalhaAoPublicar(ILogger logger, Guid simulacaoId, Exception ex);
}

/// <summary>Sem Kafka configurado (testes, dev sem broker): não publica nada.</summary>
internal sealed class PublicadorDeSimulacoesNulo : IPublicadorDeSimulacoes
{
    public void Publicar(SimulacaoDeEmprestimo simulacao) { }
}
