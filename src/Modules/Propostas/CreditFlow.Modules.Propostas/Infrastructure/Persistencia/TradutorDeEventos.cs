using CreditFlow.BuildingBlocks.Mensageria;
using Dominio = CreditFlow.Modules.Propostas.Domain;
using Integracao = CreditFlow.Modules.Propostas.Contracts.Eventos;

namespace CreditFlow.Modules.Propostas.Infrastructure.Persistencia;

/// <summary>
/// A camada de tradução domínio → integração (módulo 6, lição 6.2). Roda ANTES do SaveChanges, então as
/// linhas da outbox vão no mesmo commit da proposta: ou o estado e o evento ficam gravados juntos, ou nenhum.
/// </summary>
internal static class TradutorDeEventos
{
    /// <returns>As mensagens acrescentadas ao change tracker (para soltar se o SaveChanges falhar).</returns>
    public static List<MensagemOutbox> GravarNaOutbox(PropostasDbContext db, Dominio.Proposta proposta, DateTimeOffset agora)
    {
        var mensagens = new List<MensagemOutbox>();
        var chave = proposta.Id.ToString();
        foreach (var evento in proposta.EventosDeDominio)
        {
            MensagemOutbox? m = evento switch
            {
                Dominio.PropostaCriada e => db.AdicionarAoOutbox(new Integracao.PropostaCriada(
                    Guid.CreateVersion7(), e.OcorridoEm, e.PropostaId, e.ClienteId, e.SimulacaoId,
                    e.ValorSolicitadoCentavos, e.PrazoMeses), chave, agora),
                Dominio.PropostaAprovada e => db.AdicionarAoOutbox(new Integracao.PropostaAprovada(
                    Guid.CreateVersion7(), e.OcorridoEm, e.PropostaId, e.ClienteId, e.Automatica, e.ValidaAte), chave, agora),
                Dominio.PropostaRecusada e => db.AdicionarAoOutbox(new Integracao.PropostaRecusada(
                    Guid.CreateVersion7(), e.OcorridoEm, e.PropostaId, e.ClienteId, e.Automatica, e.CodigoMotivo), chave, agora),
                Dominio.ContratoAssinado e => db.AdicionarAoOutbox(new Integracao.ContratoAssinado(
                    Guid.CreateVersion7(), e.OcorridoEm, e.PropostaId, e.ClienteId, e.SolicitanteId,
                    e.ValorSolicitadoCentavos, e.ValorLiberadoCentavos, e.ChavePix,
                    [.. e.Parcelas.Select(p => new Integracao.ParcelaDoContrato(p.Numero, p.Vencimento, p.ValorCentavos))]),
                    chave, agora),
                _ => null, // fatos só internos: não saem do módulo
            };
            if (m is not null)
                mensagens.Add(m);
        }
        return mensagens;
    }
}
