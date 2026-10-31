using System.Collections.Concurrent;
using CreditFlow.Modules.Simulacao.Application;
using CreditFlow.Modules.Simulacao.Domain;

namespace CreditFlow.Modules.Simulacao.Infrastructure.Memoria;

/// <summary>
/// Repositório em memória, com LIMITE. O endpoint é público: sem limite, qualquer um encheria a
/// memória do pod simulando em loop (o rate limit atrasa, não impede). Ao passar do limite, descarta
/// as mais antigas (FIFO). É para desenvolvimento e testes; produção usa MongoDB.
/// </summary>
internal sealed class SimulacaoRepositorioEmMemoria(int capacidade = 10_000) : ISimulacaoRepositorio
{
    private readonly ConcurrentDictionary<SimulacaoId, SimulacaoDeEmprestimo> _itens = new();
    private readonly ConcurrentQueue<SimulacaoId> _ordem = new();

    public Task SalvarAsync(SimulacaoDeEmprestimo simulacao, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(simulacao);
        if (_itens.TryAdd(simulacao.Id, simulacao))
            _ordem.Enqueue(simulacao.Id);

        while (_itens.Count > capacidade && _ordem.TryDequeue(out var maisAntiga))
            _itens.TryRemove(maisAntiga, out _);

        return Task.CompletedTask;
    }

    public Task<SimulacaoDeEmprestimo?> ObterAsync(SimulacaoId id, CancellationToken ct) =>
        Task.FromResult(_itens.TryGetValue(id, out var s) ? s : null);
}
