using CreditFlow.Modules.Simulacao.Application;
using CreditFlow.Modules.Simulacao.Domain;
using Microsoft.Extensions.Options;
using MongoDB.Driver;

namespace CreditFlow.Modules.Simulacao.Infrastructure.Mongo;

internal sealed class SimulacaoRepositorioMongo : ISimulacaoRepositorio
{
    private readonly IMongoCollection<SimulacaoDocumento> _colecao;
    private readonly TimeSpan _retencao;

    public SimulacaoRepositorioMongo(IMongoDatabase banco, IOptions<SimulacaoMongoOpcoes> opcoes)
    {
        ArgumentNullException.ThrowIfNull(banco);
        ArgumentNullException.ThrowIfNull(opcoes);
        _colecao = banco.GetCollection<SimulacaoDocumento>(opcoes.Value.Colecao);
        _retencao = TimeSpan.FromDays(opcoes.Value.RetencaoDias);
    }

    internal IMongoCollection<SimulacaoDocumento> Colecao => _colecao;

    /// <summary>
    /// Insert simples: a simulação é imutável e o id é novo, então não há o que "atualizar".
    /// Um retry do driver (retryWrites) que reenvia o mesmo insert esbarra no _id e é ignorado:
    /// gravar a mesma simulação duas vezes resulta numa só.
    /// </summary>
    public async Task SalvarAsync(SimulacaoDeEmprestimo simulacao, CancellationToken ct)
    {
        var documento = MapeamentoDeSimulacao.ParaDocumento(simulacao, _retencao);
        try
        {
            await _colecao.InsertOneAsync(documento, options: null, ct);
        }
        catch (MongoWriteException ex) when (ex.WriteError?.Category == ServerErrorCategory.DuplicateKey)
        {
            // mesma simulação já gravada: idempotente
        }
    }

    public async Task<SimulacaoDeEmprestimo?> ObterAsync(SimulacaoId id, CancellationToken ct)
    {
        var documento = await _colecao.Find(d => d.Id == id.Valor).FirstOrDefaultAsync(ct);
        return documento is null ? null : MapeamentoDeSimulacao.ParaAgregado(documento);
    }

    /// <summary>
    /// Histórico de um cliente, mais recentes primeiro, sem a tabela de parcelas (projeção): a tela de
    /// histórico mostra 20 linhas, não 20 × 48 parcelas. Usa o índice (cliente_id, criada_em).
    /// </summary>
    public async Task<IReadOnlyList<ResumoDeSimulacao>> ListarDoClienteAsync(Guid clienteId, int limite, CancellationToken ct)
    {
        return await _colecao.Find(d => d.ClienteId == clienteId)
            .SortByDescending(d => d.CriadaEm)
            .Limit(Math.Clamp(limite, 1, 100))
            .Project(d => new ResumoDeSimulacao(d.Id, d.Valor, d.PrazoMeses, d.Sistema, d.CetAnual, d.CriadaEm))
            .ToListAsync(ct);
    }
}

internal sealed record ResumoDeSimulacao(Guid Id, decimal Valor, int PrazoMeses, string Sistema, decimal CetAnual, DateTime CriadaEm);
