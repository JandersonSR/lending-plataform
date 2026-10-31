using CreditFlow.BuildingBlocks;
using CreditFlow.BuildingBlocks.Tempo;
using CreditFlow.Modules.Clientes.Contracts;
using CreditFlow.Modules.Simulacao.Domain;
using CreditFlow.Modules.Simulacao.Domain.Calculo;

namespace CreditFlow.Modules.Simulacao.Application;

public sealed record SimularEmprestimoComando(decimal Valor, int PrazoMeses, string? Sistema, Guid? ClienteId);

/// <summary>
/// Caso de uso: simular. Busca a taxa (pelo score, se houver cliente com consentimento), chama o
/// agregado (que chama o motor), grava e avisa. Não sabe de HTTP, Mongo ou Kafka: só das portas.
/// </summary>
public sealed class SimularEmprestimo(
    ITabelaDeTaxas tabelaDeTaxas,
    ISimulacaoRepositorio repositorio,
    IPublicadorDeSimulacoes publicador,
    IConsultaDeClientes clientes,
    TimeProvider relogio)
{
    public async Task<Result<SimulacaoDeEmprestimo>> ExecutarAsync(SimularEmprestimoComando comando, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(comando);

        var sistema = LerSistema(comando.Sistema);
        if (sistema.Falhou)
            return sistema.Erro;

        int? score = null;
        if (comando.ClienteId is { } clienteId)
        {
            var perfil = await clientes.ObterPerfilDeCreditoAsync(clienteId, ct);
            if (perfil is null)
                return SimulacaoErros.ClienteNaoEncontrado;
            // LGPD: sem consentimento para análise de crédito, o score não é usado. Simula como anônimo.
            score = perfil.ConsentimentoAnaliseDeCredito ? perfil.Score : null;
        }

        var faixas = await tabelaDeTaxas.ObterFaixasAsync(ct);
        var faixa = SeletorDeFaixa.Escolher(faixas, score);

        var simulacao = SimulacaoDeEmprestimo.Criar(
            comando.Valor, comando.PrazoMeses, sistema.Valor, faixa, comando.ClienteId,
            Brasilia.Hoje(relogio), relogio.GetUtcNow());
        if (simulacao.Falhou)
            return simulacao.Erro;

        await repositorio.SalvarAsync(simulacao.Valor, ct);
        publicador.Publicar(simulacao.Valor);
        return simulacao;
    }

    /// <summary>"price"/"sac", sem diferenciar maiúsculas. Número ("1") não vale: o contrato é o nome.</summary>
    public static Result<SistemaDeAmortizacao> LerSistema(string? texto) =>
        texto?.Trim().ToLowerInvariant() switch
        {
            "price" => SistemaDeAmortizacao.Price,
            "sac" => SistemaDeAmortizacao.Sac,
            _ => SimulacaoErros.SistemaInvalido,
        };
}

public sealed class ObterSimulacao(ISimulacaoRepositorio repositorio)
{
    public async Task<Result<SimulacaoDeEmprestimo>> ExecutarAsync(Guid id, CancellationToken ct)
    {
        var simulacao = await repositorio.ObterAsync(new SimulacaoId(id), ct);
        return simulacao is null ? SimulacaoErros.NaoEncontrada : simulacao;
    }
}
