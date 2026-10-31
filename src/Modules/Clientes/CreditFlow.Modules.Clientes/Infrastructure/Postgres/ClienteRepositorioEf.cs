using CreditFlow.BuildingBlocks.ValueObjects;
using CreditFlow.Modules.Clientes.Application;
using CreditFlow.Modules.Clientes.Domain;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace CreditFlow.Modules.Clientes.Infrastructure.Postgres;

internal sealed class ClienteRepositorioEf(ClientesDbContext db) : IClienteRepositorio
{
    public Task<Cliente?> ObterAsync(ClienteId id, CancellationToken ct) =>
        db.Clientes.FirstOrDefaultAsync(c => c.Id == id, ct);

    public Task<bool> ExisteCpfAsync(Cpf cpf, CancellationToken ct) =>
        db.Clientes.AnyAsync(c => c.Cpf == cpf, ct);

    public Task<Cliente?> ObterPorUsuarioAsync(string usuarioId, CancellationToken ct) =>
        db.Clientes.FirstOrDefaultAsync(c => c.UsuarioId == usuarioId, ct);

    public async Task<bool> AdicionarAsync(Cliente cliente, CancellationToken ct)
    {
        db.Clientes.Add(cliente);
        try
        {
            await db.SaveChangesAsync(ct);
            return true;
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            // Outra requisição gravou o mesmo CPF (ou usuário) entre a checagem e aqui: o índice único decidiu.
            db.Entry(cliente).State = EntityState.Detached;
            return false;
        }
    }

    public Task AtualizarAsync(Cliente cliente, CancellationToken ct) => db.SaveChangesAsync(ct);
}
