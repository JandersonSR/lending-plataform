using CreditFlow.Modules.Clientes.Contracts;
using CreditFlow.Modules.Simulacao.Application;
using CreditFlow.Modules.Simulacao.Domain;

namespace CreditFlow.Modules.Simulacao.Tests;

/// <summary>Relógio parado num instante: o teste decide que horas são.</summary>
internal sealed class RelogioFixo(DateTimeOffset agora) : TimeProvider
{
    public override DateTimeOffset GetUtcNow() => agora;
}

internal sealed class ClientesFalsos : IConsultaDeClientes
{
    public Dictionary<Guid, PerfilDeCreditoDto> Perfis { get; } = [];

    public Task<PerfilDeCreditoDto?> ObterPerfilDeCreditoAsync(Guid clienteId, CancellationToken ct = default) =>
        Task.FromResult(Perfis.TryGetValue(clienteId, out var p) ? p : null);

    public Task<PerfilDeCreditoDto?> ObterPerfilPorUsuarioAsync(string usuarioId, CancellationToken ct = default) =>
        Task.FromResult<PerfilDeCreditoDto?>(null);
}

internal sealed class PublicadorEspiao : IPublicadorDeSimulacoes
{
    public List<SimulacaoDeEmprestimo> Publicadas { get; } = [];
    public void Publicar(SimulacaoDeEmprestimo simulacao) => Publicadas.Add(simulacao);
}

internal sealed class TabelaFixa : ITabelaDeTaxas
{
    public Task<IReadOnlyList<FaixaDeScore>> ObterFaixasAsync(CancellationToken ct) =>
        Task.FromResult(Infrastructure.Taxas.TabelaDeTaxasEmConfiguracao.Padrao);
}
