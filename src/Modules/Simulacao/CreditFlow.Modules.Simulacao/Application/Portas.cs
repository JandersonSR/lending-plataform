using CreditFlow.Modules.Simulacao.Domain;

namespace CreditFlow.Modules.Simulacao.Application;

/// <summary>
/// Onde as simulações ficam. Hoje: memória (Infrastructure/Memoria). Módulo 4 do curso: MongoDB,
/// coleção "simulacoes", um documento por simulação com a tabela inteira (Infrastructure/Mongo).
/// </summary>
public interface ISimulacaoRepositorio
{
    Task SalvarAsync(SimulacaoDeEmprestimo simulacao, CancellationToken ct);

    Task<SimulacaoDeEmprestimo?> ObterAsync(SimulacaoId id, CancellationToken ct);
}

/// <summary>
/// As faixas de score e suas taxas. Hoje: lidas da configuração (Infrastructure/Taxas).
/// Módulo 5 do curso: um decorador com HybridCache + Dragonfly na frente (Infrastructure/Cache).
/// </summary>
public interface ITabelaDeTaxas
{
    Task<IReadOnlyList<FaixaDeScore>> ObterFaixasAsync(CancellationToken ct);
}

/// <summary>Avisa o resto do sistema que uma simulação aconteceu (estatística; perda ocasional aceitável).</summary>
public interface IPublicadorDeSimulacoes
{
    /// <summary>Não espera o broker e nunca lança: falha de publicação não pode derrubar a simulação.</summary>
    void Publicar(SimulacaoDeEmprestimo simulacao);
}
