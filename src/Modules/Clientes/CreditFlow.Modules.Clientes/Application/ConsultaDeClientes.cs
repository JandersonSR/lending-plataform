using CreditFlow.Modules.Clientes.Contracts;
using CreditFlow.Modules.Clientes.Domain;

namespace CreditFlow.Modules.Clientes.Application;

/// <summary>Implementação do contrato público. É a única porta pela qual outro módulo lê Clientes.</summary>
internal sealed class ConsultaDeClientes(IClienteRepositorio repositorio) : IConsultaDeClientes
{
    public async Task<PerfilDeCreditoDto?> ObterPerfilDeCreditoAsync(Guid clienteId, CancellationToken ct = default) =>
        Perfil(await repositorio.ObterAsync(new ClienteId(clienteId), ct));

    public async Task<PerfilDeCreditoDto?> ObterPerfilPorUsuarioAsync(string usuarioId, CancellationToken ct = default) =>
        string.IsNullOrWhiteSpace(usuarioId) ? null : Perfil(await repositorio.ObterPorUsuarioAsync(usuarioId, ct));

    private static PerfilDeCreditoDto? Perfil(Cliente? cliente) =>
        cliente is null
            ? null
            : new PerfilDeCreditoDto(
                cliente.Id.Valor,
                cliente.Score,
                cliente.RendaMensal.Centavos,
                cliente.PodeUsarParaAnaliseDeCredito);
}
