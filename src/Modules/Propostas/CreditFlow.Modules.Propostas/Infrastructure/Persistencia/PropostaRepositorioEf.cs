using CreditFlow.Modules.Propostas.Application;
using CreditFlow.Modules.Propostas.Domain;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace CreditFlow.Modules.Propostas.Infrastructure.Persistencia;

internal sealed class PropostaRepositorioEf(PropostasDbContext db, TimeProvider? relogio = null) : IPropostaRepositorio
{
    private readonly TimeProvider _relogio = relogio ?? TimeProvider.System;

    public Task<Proposta?> ObterAsync(Guid id, CancellationToken ct) =>
        db.Propostas.FirstOrDefaultAsync(p => p.Id == id, ct);

    public async Task<IReadOnlyList<Proposta>> ListarPorStatusAsync(StatusProposta status, int limite, CancellationToken ct) =>
        await db.Propostas.AsNoTracking()
            .Where(p => p.Status == status)
            .OrderBy(p => p.CriadaEm)
            .Take(limite)
            .ToListAsync(ct);

    public async Task<IReadOnlyList<Proposta>> ListarDoSolicitanteAsync(string solicitanteId, int limite, CancellationToken ct) =>
        await db.Propostas.AsNoTracking()
            .Where(p => p.SolicitanteId == solicitanteId)
            .OrderByDescending(p => p.CriadaEm)
            .Take(limite)
            .ToListAsync(ct);

    public async Task<bool> AdicionarAsync(Proposta proposta, CancellationToken ct)
    {
        db.Propostas.Add(proposta);
        // Módulo 6: eventos de domínio → outbox, no MESMO SaveChanges (uma transação).
        var mensagens = TradutorDeEventos.GravarNaOutbox(db, proposta, _relogio.GetUtcNow());
        try
        {
            await db.SaveChangesAsync(ct);
            proposta.LimparEventos();
            return true;
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException
        {
            SqlState: PostgresErrorCodes.UniqueViolation, ConstraintName: "ux_propostas_cliente_ativa"
        })
        {
            db.Entry(proposta).State = EntityState.Detached;
            foreach (var m in mensagens)
                db.Entry(m).State = EntityState.Detached; // o evento morre junto com a proposta recusada pelo índice
            return false;
        }
    }

    public async Task<bool> SalvarAsync(Proposta proposta, uint? versaoEsperada, CancellationToken ct)
    {
        if (versaoEsperada is { } v)
            // Faz o WHERE xmin = <versão que o cliente viu>, e não <versão lida nesta requisição>.
            db.Entry(proposta).Property(p => p.Versao).OriginalValue = v;
        TradutorDeEventos.GravarNaOutbox(db, proposta, _relogio.GetUtcNow());
        try
        {
            await db.SaveChangesAsync(ct);
            proposta.LimparEventos();
            return true;
        }
        catch (DbUpdateConcurrencyException)
        {
            // O que está em memória está velho: solta tudo (inclusive as linhas da outbox desta tentativa)
            // para a próxima leitura vir do banco.
            db.ChangeTracker.Clear();
            return false;
        }
    }
}
