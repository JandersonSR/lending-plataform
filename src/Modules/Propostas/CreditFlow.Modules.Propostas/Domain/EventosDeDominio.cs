namespace CreditFlow.Modules.Propostas.Domain;

/// <summary>
/// Fato que aconteceu dentro do agregado. Fica numa lista do agregado até o SaveChanges; quem os
/// transforma em eventos de integração e grava na outbox, na mesma transação, é a Mensageria (módulo 6).
/// </summary>
public interface IEventoDeDominio
{
    Guid PropostaId { get; }
    DateTimeOffset OcorridoEm { get; }
}

public sealed record PropostaCriada(
    Guid PropostaId, Guid ClienteId, Guid SimulacaoId, long ValorSolicitadoCentavos, int PrazoMeses,
    DateTimeOffset OcorridoEm) : IEventoDeDominio;

public sealed record PropostaEnviadaParaAnalise(Guid PropostaId, Guid ClienteId, string Motivo, DateTimeOffset OcorridoEm) : IEventoDeDominio;

public sealed record PropostaAprovada(
    Guid PropostaId, Guid ClienteId, bool Automatica, string? AnalisadoPor, DateTimeOffset ValidaAte,
    DateTimeOffset OcorridoEm) : IEventoDeDominio;

public sealed record PropostaRecusada(
    Guid PropostaId, Guid ClienteId, bool Automatica, string CodigoMotivo, string? AnalisadoPor,
    DateTimeOffset OcorridoEm) : IEventoDeDominio;

/// <summary>
/// O que Pagamentos precisa para desembolsar e criar as parcelas, sem consultar Propostas.
/// O PIX é de <see cref="ValorLiberadoCentavos"/> (solicitado − IOF); as parcelas pagam o solicitado com juros.
/// </summary>
public sealed record ContratoAssinado(
    Guid PropostaId, Guid ClienteId, string SolicitanteId, long ValorSolicitadoCentavos, long ValorLiberadoCentavos, string ChavePix,
    IReadOnlyList<ParcelaContratada> Parcelas, DateTimeOffset OcorridoEm) : IEventoDeDominio;

public sealed record ParcelaContratada(int Numero, long ValorCentavos, DateOnly Vencimento);

public sealed record PropostaCancelada(Guid PropostaId, Guid ClienteId, DateTimeOffset OcorridoEm) : IEventoDeDominio;

public sealed record PropostaExpirada(Guid PropostaId, Guid ClienteId, DateTimeOffset OcorridoEm) : IEventoDeDominio;

public sealed record PropostaLiberada(Guid PropostaId, Guid ClienteId, DateTimeOffset OcorridoEm) : IEventoDeDominio;

public sealed record ContratacaoDesfeita(Guid PropostaId, Guid ClienteId, string Motivo, DateTimeOffset OcorridoEm) : IEventoDeDominio;
