using CreditFlow.Modules.Propostas.Domain;

namespace CreditFlow.Modules.Propostas.Application;

public interface IPropostaRepositorio
{
    Task<Proposta?> ObterAsync(Guid id, CancellationToken ct);

    /// <summary>Fila do analista, mais antigas primeiro.</summary>
    Task<IReadOnlyList<Proposta>> ListarPorStatusAsync(StatusProposta status, int limite, CancellationToken ct);

    Task<IReadOnlyList<Proposta>> ListarDoSolicitanteAsync(string solicitanteId, int limite, CancellationToken ct);

    /// <summary>false = já existe proposta ativa para o cliente (o índice único parcial decidiu).</summary>
    Task<bool> AdicionarAsync(Proposta proposta, CancellationToken ct);

    /// <summary>
    /// Grava as mudanças conferindo a versão (<c>xmin</c>). Com <paramref name="versaoEsperada"/>, confere contra
    /// a versão que o CLIENTE viu (If-Match), e não só contra a que foi lida agora.
    /// false = outra transação gravou antes (conflito de concorrência).
    /// </summary>
    Task<bool> SalvarAsync(Proposta proposta, uint? versaoEsperada, CancellationToken ct);
}

/// <summary>
/// Porta para a Simulação (anticorrupção): Propostas diz do que precisa, no vocabulário dela.
/// O adaptador em Infrastructure/Simulacoes traduz o contrato público do módulo Simulacao.
/// </summary>
public interface ISimulacoesParaProposta
{
    Task<CondicoesDaSimulacao?> ObterAsync(Guid simulacaoId, CancellationToken ct);
}
