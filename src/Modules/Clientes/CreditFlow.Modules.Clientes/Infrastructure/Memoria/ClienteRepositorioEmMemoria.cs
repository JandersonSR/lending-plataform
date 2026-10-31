using System.Collections.Concurrent;
using CreditFlow.BuildingBlocks.ValueObjects;
using CreditFlow.Modules.Clientes.Application;
using CreditFlow.Modules.Clientes.Domain;

namespace CreditFlow.Modules.Clientes.Infrastructure.Memoria;

/// <summary>
/// Repositório em memória: para testes e para rodar sem Postgres. Cada processo tem o seu, então
/// com duas réplicas os dados divergem; serve só para desenvolvimento.
/// </summary>
internal sealed class ClienteRepositorioEmMemoria : IClienteRepositorio
{
    private readonly ConcurrentDictionary<ClienteId, Cliente> _porId = new();
    // O "índice único" do CPF: TryAdd é atômico, então duas requisições simultâneas não passam juntas.
    private readonly ConcurrentDictionary<string, ClienteId> _porCpf = new();
    private readonly ConcurrentDictionary<string, ClienteId> _porUsuario = new();

    public Task<Cliente?> ObterAsync(ClienteId id, CancellationToken ct) =>
        Task.FromResult(_porId.TryGetValue(id, out var c) ? c : null);

    public Task<bool> ExisteCpfAsync(Cpf cpf, CancellationToken ct) =>
        Task.FromResult(_porCpf.ContainsKey(cpf.Digitos));

    public Task<Cliente?> ObterPorUsuarioAsync(string usuarioId, CancellationToken ct) =>
        Task.FromResult(_porUsuario.TryGetValue(usuarioId, out var id) && _porId.TryGetValue(id, out var c) ? c : null);

    public Task<bool> AdicionarAsync(Cliente cliente, CancellationToken ct)
    {
        if (!_porCpf.TryAdd(cliente.Cpf.Digitos, cliente.Id))
            return Task.FromResult(false);
        if (cliente.UsuarioId is { } usuario && !_porUsuario.TryAdd(usuario, cliente.Id))
        {
            _porCpf.TryRemove(cliente.Cpf.Digitos, out _); // desfaz: os dois "índices únicos" valem juntos
            return Task.FromResult(false);
        }
        _porId[cliente.Id] = cliente;
        return Task.FromResult(true);
    }

    public Task AtualizarAsync(Cliente cliente, CancellationToken ct)
    {
        _porId[cliente.Id] = cliente;
        return Task.CompletedTask;
    }
}
