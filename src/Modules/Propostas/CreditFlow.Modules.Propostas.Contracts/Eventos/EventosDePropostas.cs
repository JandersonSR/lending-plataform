using CreditFlow.BuildingBlocks;
using CreditFlow.BuildingBlocks.Mensageria;

namespace CreditFlow.Modules.Propostas.Contracts.Eventos;

// Eventos de integração de Propostas (tópico creditflow.propostas.v1, chave = PropostaId).
// São a tradução PÚBLICA dos eventos de domínio do agregado Proposta (Domain/EventosDeDominio.cs),
// feita por Infrastructure/Persistencia/TradutorDeEventos.cs na mesma transação do SaveChanges.
// Só os fatos que outros módulos precisam saem do módulo; os outros (PropostaEnviadaParaAnalise,
// PropostaLiberada, ContratacaoDesfeita...) continuam internos até alguém de fora precisar deles.
// Acrescentar campo OPCIONAL não muda a versão; renomear/remover/mudar tipo = versão 2 (novo record).
// Dinheiro em centavos (long).

[EventoIntegracao("PropostaCriada", 1, Topicos.Propostas)]
public sealed record PropostaCriada(
    Guid EventoId,
    DateTimeOffset OcorridoEm,
    Guid PropostaId,
    Guid ClienteId,
    Guid SimulacaoId,
    long ValorSolicitadoCentavos,
    int PrazoMeses) : IEventoIntegracao;

[EventoIntegracao("PropostaAprovada", 1, Topicos.Propostas)]
public sealed record PropostaAprovada(
    Guid EventoId,
    DateTimeOffset OcorridoEm,
    Guid PropostaId,
    Guid ClienteId,
    bool Automatica,
    DateTimeOffset ValidaAte) : IEventoIntegracao;

[EventoIntegracao("PropostaRecusada", 1, Topicos.Propostas)]
public sealed record PropostaRecusada(
    Guid EventoId,
    DateTimeOffset OcorridoEm,
    Guid PropostaId,
    Guid ClienteId,
    bool Automatica,
    string CodigoMotivo) : IEventoIntegracao;

/// <summary>
/// O cliente assinou: começa a saga de desembolso. Leva tudo o que Pagamentos precisa
/// (event-carried state transfer): valor do PIX, chave PIX e o cronograma de parcelas já calculado.
/// Pagamentos não chama Propostas de volta para "buscar o resto": se Propostas estiver fora,
/// o desembolso acontece do mesmo jeito.
/// </summary>
/// <param name="UsuarioId">sub do titular no Keycloak (o SolicitanteId da proposta): o cliente só consulta o que é dele.</param>
/// <param name="ValorLiquidoCentavos">Valor do PIX: solicitado menos IOF (o "valor liberado").</param>
[EventoIntegracao("ContratoAssinado", 1, Topicos.Propostas)]
public sealed record ContratoAssinado(
    Guid EventoId,
    DateTimeOffset OcorridoEm,
    Guid PropostaId,
    Guid ClienteId,
    string? UsuarioId,
    long ValorSolicitadoCentavos,
    long ValorLiquidoCentavos,
    string ChavePix,
    IReadOnlyList<ParcelaDoContrato> Parcelas) : IEventoIntegracao;

/// <summary>Uma linha do cronograma. Vencimento como data (sem hora/fuso): é assim que o contrato fala.</summary>
public sealed record ParcelaDoContrato(int Numero, DateOnly Vencimento, long ValorCentavos);
