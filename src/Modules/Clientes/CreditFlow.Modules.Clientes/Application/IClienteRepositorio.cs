using CreditFlow.BuildingBlocks.ValueObjects;
using CreditFlow.Modules.Clientes.Domain;

namespace CreditFlow.Modules.Clientes.Application;

/// <summary>Porta de saída: o caso de uso diz O QUE precisa; Postgres ou memória dizem COMO.</summary>
public interface IClienteRepositorio
{
    Task<Cliente?> ObterAsync(ClienteId id, CancellationToken ct);

    Task<bool> ExisteCpfAsync(Cpf cpf, CancellationToken ct);

    Task<Cliente?> ObterPorUsuarioAsync(string usuarioId, CancellationToken ct);

    /// <summary>Grava um cliente novo. Devolve false se o CPF (ou o usuário) já existe (corrida entre duas requisições).</summary>
    Task<bool> AdicionarAsync(Cliente cliente, CancellationToken ct);

    /// <summary>Persiste mudanças num cliente obtido por <see cref="ObterAsync"/>.</summary>
    Task AtualizarAsync(Cliente cliente, CancellationToken ct);
}
