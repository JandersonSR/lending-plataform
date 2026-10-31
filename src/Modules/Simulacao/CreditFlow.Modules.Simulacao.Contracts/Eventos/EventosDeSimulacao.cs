using CreditFlow.BuildingBlocks;
using CreditFlow.BuildingBlocks.Mensageria;

namespace CreditFlow.Modules.Simulacao.Contracts.Eventos;

/// <summary>
/// Uma simulação foi calculada (tópico creditflow.simulacoes.v1, chave = SimulacaoId).
/// Evento estatístico: publicado DIRETO (IPublicadorDeEventos.PublicarSemEsperar), sem outbox,
/// porque perder um ocasionalmente não fere nenhuma regra e a Simulação não tem transação de banco
/// relacional onde a outbox pudesse morar. Escrito por cf-c; o dono de Simulação pode acrescentar campos opcionais.
/// </summary>
[EventoIntegracao("SimulacaoRealizada", 1, Topicos.Simulacoes)]
public sealed record SimulacaoRealizada(
    Guid EventoId,
    DateTimeOffset OcorridoEm,
    Guid SimulacaoId,
    string Sistema,
    long ValorCentavos,
    int PrazoMeses,
    decimal TaxaMensal,
    decimal CetAnual,
    string? FaixaDeScore) : IEventoIntegracao;
