using Microsoft.EntityFrameworkCore;

namespace CreditFlow.BuildingBlocks.Mensageria;

/// <summary>
/// Inbox: "este consumidor já aplicou este evento?". A resposta tem de ser gravada na MESMA transação
/// do efeito; é isso que transforma "pelo menos uma vez" (Kafka) em "efeito exatamente uma vez" (banco).
/// </summary>
public static class InboxDbContextExtensions
{
    /// <summary>
    /// O caso comum: abre transação, tenta registrar o evento na inbox (<c>INSERT ... ON CONFLICT DO NOTHING</c>),
    /// e só se for a primeira vez executa o efeito e o <c>SaveChangesAsync</c>, tudo junto no COMMIT.
    /// Dois pods com a mesma mensagem ao mesmo tempo: o segundo INSERT espera a trava da chave primária;
    /// quando o primeiro faz COMMIT, ele vê o conflito, afeta 0 linhas e sai sem efeito.
    /// </summary>
    /// <returns>true se o efeito foi aplicado agora; false se era duplicata.</returns>
    public static async Task<bool> ProcessarUmaVezAsync(
        this DbContext db, ContextoDaMensagem contexto, Func<CancellationToken, Task> efeito, TimeProvider relogio, CancellationToken ct)
    {
        var estrategia = db.Database.CreateExecutionStrategy();
        return await estrategia.ExecuteAsync(async () =>
        {
            db.ChangeTracker.Clear(); // se a estratégia repetir, não reaproveita entidades da tentativa anterior
            await using var transacao = await db.Database.BeginTransactionAsync(ct);

            if (!await TentarRegistrarNaInboxAsync(db, contexto, relogio, ct))
            {
                await transacao.RollbackAsync(ct);
                return false;
            }

            await efeito(ct);
            await db.SaveChangesAsync(ct);
            await transacao.CommitAsync(ct);
            return true;
        });
    }

    /// <summary>
    /// Para manipuladores com mais de uma transação (ex.: grava → chama gateway → grava): consulta antes,
    /// e registra com <see cref="TentarRegistrarNaInboxAsync"/> na ÚLTIMA transação.
    /// </summary>
    public static Task<bool> JaProcessadaAsync(this DbContext db, ContextoDaMensagem contexto, CancellationToken ct) =>
        db.Set<MensagemInbox>().AsNoTracking()
            .AnyAsync(m => m.Consumidor == contexto.Consumidor && m.EventoId == contexto.EventoId, ct);

    /// <summary>
    /// <c>INSERT ... ON CONFLICT DO NOTHING</c> na transação corrente. false = já existia (duplicata).
    /// Por que não <c>Add</c> + <c>SaveChanges</c>: a violação de chave única abortaria a transação inteira
    /// no Postgres, e você teria de distinguir "duplicata" de "erro" pela mensagem da exceção.
    /// </summary>
    public static async Task<bool> TentarRegistrarNaInboxAsync(
        this DbContext db, ContextoDaMensagem contexto, TimeProvider relogio, CancellationToken ct)
    {
        var linhas = await db.Database.ExecuteSqlRawAsync(
            $"INSERT INTO {db.TabelaDe<MensagemInbox>()} (consumidor, evento_id, tipo, processada_em) " +
            "VALUES ({0}, {1}, {2}, {3}) ON CONFLICT DO NOTHING",
            [contexto.Consumidor, contexto.EventoId, contexto.Tipo, relogio.GetUtcNow()], ct);
        return linhas == 1;
    }
}
