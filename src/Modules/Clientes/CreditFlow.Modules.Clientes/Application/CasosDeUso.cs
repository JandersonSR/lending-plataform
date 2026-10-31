using CreditFlow.BuildingBlocks;
using CreditFlow.BuildingBlocks.ValueObjects;
using CreditFlow.Modules.Clientes.Domain;

namespace CreditFlow.Modules.Clientes.Application;

public sealed record CadastrarClienteComando(
    string? Nome,
    string? Cpf,
    decimal RendaMensal,
    bool AceitaAnaliseDeCredito,
    string? VersaoDoTermo,
    bool AceitaOfertasPorEmail,
    string? UsuarioId = null);

/// <summary>Caso de uso: cadastrar cliente. Orquestra; quem decide as regras é o agregado.</summary>
public sealed class CadastrarCliente(IClienteRepositorio repositorio, TimeProvider relogio)
{
    public async Task<Result<Cliente>> ExecutarAsync(CadastrarClienteComando comando, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(comando);

        var cpf = Cpf.Criar(comando.Cpf);
        if (cpf.Falhou)
            return cpf.Erro;

        var cliente = Cliente.Cadastrar(
            comando.Nome, cpf.Valor, comando.RendaMensal, comando.AceitaAnaliseDeCredito,
            comando.VersaoDoTermo, comando.AceitaOfertasPorEmail, relogio.GetUtcNow(), comando.UsuarioId);
        if (cliente.Falhou)
            return cliente.Erro;

        // Checagem antecipada para a resposta boa no caso comum...
        if (await repositorio.ExisteCpfAsync(cpf.Valor, ct))
            return ClientesErros.CpfJaCadastrado;
        if (cliente.Valor.UsuarioId is { } usuario && await repositorio.ObterPorUsuarioAsync(usuario, ct) is not null)
            return ClientesErros.UsuarioJaCadastrado;

        // ...e o índice único decide a corrida entre duas requisições simultâneas com o mesmo CPF.
        if (!await repositorio.AdicionarAsync(cliente.Valor, ct))
            return ClientesErros.CpfJaCadastrado;

        return cliente;
    }
}

public sealed class ObterCliente(IClienteRepositorio repositorio)
{
    public async Task<Result<Cliente>> ExecutarAsync(Guid id, CancellationToken ct)
    {
        var cliente = await repositorio.ObterAsync(new ClienteId(id), ct);
        return cliente is null ? ClientesErros.NaoEncontrado : cliente;
    }
}

public sealed class ObterClienteDoUsuario(IClienteRepositorio repositorio)
{
    public async Task<Result<Cliente>> ExecutarAsync(string? usuarioId, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(usuarioId))
            return ClientesErros.NaoEncontrado;
        var cliente = await repositorio.ObterPorUsuarioAsync(usuarioId, ct);
        return cliente is null ? ClientesErros.NaoEncontrado : cliente;
    }
}

public sealed class RevogarConsentimento(IClienteRepositorio repositorio, TimeProvider relogio)
{
    public async Task<Result> ExecutarAsync(Guid id, string? finalidade, CancellationToken ct)
    {
        if (!Enum.TryParse<FinalidadeDoConsentimento>(finalidade, ignoreCase: true, out var qual)
            || !Enum.IsDefined(qual) || int.TryParse(finalidade, out _))
            return ClientesErros.FinalidadeInvalida;

        var cliente = await repositorio.ObterAsync(new ClienteId(id), ct);
        if (cliente is null)
            return ClientesErros.NaoEncontrado;

        cliente.Revogar(qual, relogio.GetUtcNow());
        await repositorio.AtualizarAsync(cliente, ct);
        return Result.Ok();
    }
}
