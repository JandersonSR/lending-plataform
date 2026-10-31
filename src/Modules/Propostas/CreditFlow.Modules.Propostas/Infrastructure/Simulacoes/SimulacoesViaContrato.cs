using CreditFlow.BuildingBlocks.ValueObjects;
using CreditFlow.Modules.Propostas.Application;
using CreditFlow.Modules.Propostas.Domain;
using CreditFlow.Modules.Simulacao.Contracts;

namespace CreditFlow.Modules.Propostas.Infrastructure.Simulacoes;

/// <summary>
/// Adaptador: traduz o contrato público da Simulação para o vocabulário de Propostas. Se o contrato
/// mudar, só este arquivo muda.
/// </summary>
internal sealed class SimulacoesViaContrato(IConsultaDeSimulacoes consulta) : ISimulacoesParaProposta
{
    public async Task<CondicoesDaSimulacao?> ObterAsync(Guid simulacaoId, CancellationToken ct)
    {
        var s = await consulta.ObterAsync(simulacaoId, ct);
        if (s is null)
            return null;

        return new CondicoesDaSimulacao(
            s.Id,
            s.ClienteId,
            Dinheiro.DeCentavos(s.ValorCentavos),
            Dinheiro.DeCentavos(s.ValorLiberadoCentavos),
            s.PrazoMeses,
            Percentual.DeFracao(s.TaxaMensal),
            Percentual.DeFracao(s.CetAnual),
            s.Sistema,
            [.. s.Parcelas.Select(p => new ParcelaPrevista(p.Numero, p.ValorCentavos))],
            s.CriadaEm);
    }
}
