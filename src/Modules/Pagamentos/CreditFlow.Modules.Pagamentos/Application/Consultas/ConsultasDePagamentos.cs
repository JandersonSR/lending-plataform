using CreditFlow.BuildingBlocks;
using CreditFlow.BuildingBlocks.Auth;
using CreditFlow.Modules.Pagamentos.Contracts;
using CreditFlow.Modules.Pagamentos.Domain.Desembolsos;
using CreditFlow.Modules.Pagamentos.Infrastructure.Persistencia;
using Microsoft.EntityFrameworkCore;

namespace CreditFlow.Modules.Pagamentos.Application.Consultas;

public sealed record DesembolsoResposta(
    Guid Id, Guid PropostaId, Guid ClienteId, string Status, long ValorCentavos, int Tentativas,
    string? IdTransacaoPix, string? Motivo, string? Detalhe, DateTimeOffset SolicitadoEm, DateTimeOffset? ConcluidoEm);

public sealed record ParcelaResposta(
    Guid Id, Guid PropostaId, int Numero, long ValorCentavos, DateOnly Vencimento, string Status,
    long? ValorPagoCentavos, DateTimeOffset? PagaEm);

/// <summary>
/// Leituras de Pagamentos. AsNoTracking e projeção direto para o DTO: consulta não carrega agregado.
/// Regra de recurso: quem tem só o papel "cliente" enxerga apenas o que tem o seu sub (UsuarioId);
/// devolvemos 404 (e não 403) para não confirmar a existência de desembolsos de outras pessoas.
/// </summary>
internal sealed class ConsultasDePagamentos(PagamentosDbContext db, ICurrentUser usuario) : IConsultaDePagamentos
{
    private bool VeTudo => usuario.TemPapel(Papeis.OperadorFinanceiro) || usuario.TemPapel(Papeis.Analista) ||
                           usuario.TemPapel(Papeis.Admin) || usuario.EhMaquina;

    public async Task<Result<DesembolsoResposta>> ObterDesembolsoAsync(Guid id, CancellationToken ct)
    {
        var d = await Visiveis().SingleOrDefaultAsync(x => x.Id == id, ct);
        return d is null ? Erro.NaoEncontrado("desembolso.nao_encontrado", $"Desembolso {id} não encontrado.") : Mapear(d);
    }

    public async Task<Result<DesembolsoResposta>> ObterDesembolsoPorPropostaAsync(Guid propostaId, CancellationToken ct)
    {
        var d = await Visiveis().SingleOrDefaultAsync(x => x.PropostaId == propostaId, ct);
        return d is null
            ? Erro.NaoEncontrado("desembolso.nao_encontrado", $"Nenhum desembolso para a proposta {propostaId}.")
            : Mapear(d);
    }

    public async Task<IReadOnlyList<ParcelaResposta>> ListarParcelasAsync(Guid propostaId, CancellationToken ct)
    {
        var parcelas = db.Parcelas.AsNoTracking().Where(p => p.PropostaId == propostaId);
        if (!VeTudo)
            parcelas = parcelas.Where(p => p.UsuarioId == usuario.Id);
        return await parcelas.OrderBy(p => p.Numero)
            .Select(p => new ParcelaResposta(p.Id, p.PropostaId, p.Numero, p.ValorCentavos, p.Vencimento,
                p.Status.ToString(), p.ValorPagoCentavos, p.PagaEm))
            .ToListAsync(ct);
    }

    /// <summary>Contrato in-process para outros módulos (sem filtro de usuário: quem chama é código, não pessoa).</summary>
    async Task<SituacaoDoDesembolso?> IConsultaDePagamentos.ObterDesembolsoDaPropostaAsync(Guid propostaId, CancellationToken ct)
    {
        var d = await db.Desembolsos.AsNoTracking().SingleOrDefaultAsync(x => x.PropostaId == propostaId, ct);
        return d is null ? null : new SituacaoDoDesembolso(d.Id, d.PropostaId, d.Status.ToString(), d.ValorCentavos,
            d.Tentativas, d.IdTransacaoGateway, d.Motivo?.ToString(), d.SolicitadoEm, d.ConcluidoEm);
    }

    private IQueryable<Desembolso> Visiveis()
    {
        var q = db.Desembolsos.AsNoTracking();
        return VeTudo ? q : q.Where(d => d.UsuarioId == usuario.Id);
    }

    private static DesembolsoResposta Mapear(Desembolso d) => new(d.Id, d.PropostaId, d.ClienteId, d.Status.ToString(),
        d.ValorCentavos, d.Tentativas, d.IdTransacaoGateway, d.Motivo?.ToString(), d.DetalheFalha, d.SolicitadoEm, d.ConcluidoEm);
}
