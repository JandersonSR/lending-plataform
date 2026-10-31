using CreditFlow.Modules.Propostas.Contracts;

namespace CreditFlow.Modules.Propostas.Application;

internal sealed class ConsultaDePropostas(IPropostaRepositorio repositorio) : IConsultaDePropostas
{
    public async Task<SituacaoDaPropostaDto?> ObterSituacaoAsync(Guid propostaId, CancellationToken ct = default)
    {
        var p = await repositorio.ObterAsync(propostaId, ct);
        return p is null ? null : new SituacaoDaPropostaDto(p.Id, p.ClienteId, p.Status.ToString(), p.ValorLiberado.Centavos, p.ContratadaEm);
    }
}
