using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CreditFlow.BuildingBlocks;
using CreditFlow.BuildingBlocks.Mensageria;
using CreditFlow.Modules.Pagamentos.Contracts.Eventos;
using CreditFlow.Modules.Pagamentos.Domain.Parcelas;
using CreditFlow.Modules.Pagamentos.Infrastructure.Idempotencia;
using CreditFlow.Modules.Pagamentos.Infrastructure.Persistencia;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace CreditFlow.Modules.Pagamentos.Application.Parcelas;

public sealed record PagarParcelaComando(Guid ParcelaId, long ValorCentavos);

public sealed record ParcelaPagaResposta(Guid ParcelaId, Guid PropostaId, int Numero, long ValorPagoCentavos,
    DateTimeOffset PagaEm, bool ContratoQuitado);

/// <summary>Resposta de um comando idempotente: o corpo e se ela veio da memória (repetição).</summary>
public sealed record RespostaIdempotente<T>(T Valor, bool Repetida);

/// <summary>
/// Baixa de parcela (o operador financeiro registra um pagamento recebido). Idempotente por
/// <c>Idempotency-Key</c>: o operador clica duas vezes, o proxy reenvia por timeout, a rede cai
/// depois do commit; em todos os casos a parcela é baixada UMA vez e a mesma resposta volta.
/// Estado, outbox e registro da chave na MESMA transação.
/// </summary>
internal sealed class PagarParcela(PagamentosDbContext db, TimeProvider relogio)
{
    public async Task<Result<RespostaIdempotente<ParcelaPagaResposta>>> ExecutarAsync(
        string chaveIdempotencia, PagarParcelaComando comando, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(chaveIdempotencia) || chaveIdempotencia.Length > 100)
            return Erro.Validacao("idempotencia.chave_obrigatoria",
                "O cabeçalho Idempotency-Key é obrigatório (até 100 caracteres).", "Idempotency-Key");
        if (comando.ValorCentavos <= 0)
            return Erro.Validacao("parcela.valor_invalido", "O valor pago precisa ser positivo.", "valorCentavos");

        var hash = Hash(comando);
        var anterior = await ReutilizarAsync(chaveIdempotencia, hash, ct);
        if (anterior is not null)
            return anterior;

        var estrategia = db.Database.CreateExecutionStrategy();
        // Tipo explícito: os "return Erro..." viram Result pela conversão implícita.
        Func<Task<Result<RespostaIdempotente<ParcelaPagaResposta>>>> operacao = async () =>
            {
                db.ChangeTracker.Clear();
                await using var tx = await db.Database.BeginTransactionAsync(ct);

                var parcela = await db.Parcelas.SingleOrDefaultAsync(p => p.Id == comando.ParcelaId, ct);
                if (parcela is null)
                    return Erro.NaoEncontrado("parcela.nao_encontrada", $"Parcela {comando.ParcelaId} não existe.");

                var agora = relogio.GetUtcNow();
                var pagamento = parcela.Pagar(comando.ValorCentavos, agora);
                if (pagamento.Falhou)
                    return Result<RespostaIdempotente<ParcelaPagaResposta>>.Falha(pagamento.Erro);

                var abertasRestantes = await db.Parcelas.CountAsync(
                    p => p.PropostaId == parcela.PropostaId && p.Id != parcela.Id && p.Status == StatusParcela.Aberta, ct);
                var quitado = abertasRestantes == 0;

                db.AdicionarAoOutbox(new ParcelaPaga(Guid.CreateVersion7(), agora, parcela.Id, parcela.PropostaId,
                    parcela.ClienteId, parcela.Numero, comando.ValorCentavos, agora, quitado), parcela.PropostaId.ToString(), agora);

                var resposta = new ParcelaPagaResposta(parcela.Id, parcela.PropostaId, parcela.Numero, comando.ValorCentavos, agora, quitado);
                db.RequisicoesIdempotentes.Add(new RequisicaoIdempotente
                {
                    Chave = chaveIdempotencia,
                    HashCorpo = hash,
                    StatusHttp = 200,
                    Resposta = JsonSerializer.Serialize(resposta, SerializadorDeEventos.Opcoes),
                    CriadaEm = agora,
                });

                await db.SaveChangesAsync(ct);
                await tx.CommitAsync(ct);
                return Result.Ok(new RespostaIdempotente<ParcelaPagaResposta>(resposta, Repetida: false));
            };

        try
        {
            return await estrategia.ExecuteAsync(operacao);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            // Duas requisições com a mesma chave ao mesmo tempo: a outra gravou primeiro. Devolve a dela.
            db.ChangeTracker.Clear();
            return await ReutilizarAsync(chaveIdempotencia, hash, ct)
                ?? Erro.Conflito("idempotencia.em_andamento", "Requisição com esta chave em andamento; tente de novo.");
        }
        catch (DbUpdateConcurrencyException)
        {
            // Mesma parcela, chaves diferentes, ao mesmo tempo: uma ganhou. A outra relê e verá "já paga".
            return Erro.Conflito("parcela.concorrencia", "A parcela foi alterada por outra operação; consulte e tente de novo.");
        }
    }

    private async Task<Result<RespostaIdempotente<ParcelaPagaResposta>>?> ReutilizarAsync(string chave, string hash, CancellationToken ct)
    {
        var anterior = await db.RequisicoesIdempotentes.AsNoTracking().SingleOrDefaultAsync(r => r.Chave == chave, ct);
        if (anterior is null)
            return null;
        if (anterior.HashCorpo != hash)
            return Erro.Regra("idempotencia.chave_reutilizada",
                "Esta Idempotency-Key já foi usada com outro corpo. Gere uma chave nova para uma operação nova.");
        var resposta = JsonSerializer.Deserialize<ParcelaPagaResposta>(anterior.Resposta, SerializadorDeEventos.Opcoes)!;
        return Result.Ok(new RespostaIdempotente<ParcelaPagaResposta>(resposta, Repetida: true));
    }

    private static string Hash(PagarParcelaComando c) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"{c.ParcelaId:N}|{c.ValorCentavos}")));
}
