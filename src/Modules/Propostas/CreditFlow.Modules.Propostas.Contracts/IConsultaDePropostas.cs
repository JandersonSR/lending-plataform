namespace CreditFlow.Modules.Propostas.Contracts;

/// <summary>Leitura síncrona da situação de uma proposta por outro módulo (ex.: Pagamentos conferindo a saga).</summary>
public interface IConsultaDePropostas
{
    Task<SituacaoDaPropostaDto?> ObterSituacaoAsync(Guid propostaId, CancellationToken ct = default);
}

/// <param name="Status">Nome do estado (EmAnalise, Aprovada, Contratada, Liberada...). Texto, não enum: quem consome não quebra se surgir um novo.</param>
public sealed record SituacaoDaPropostaDto(Guid PropostaId, Guid ClienteId, string Status, long ValorLiberadoCentavos, DateTimeOffset? ContratadaEm);
