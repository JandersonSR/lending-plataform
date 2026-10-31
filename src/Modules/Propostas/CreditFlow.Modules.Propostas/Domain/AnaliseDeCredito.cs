using CreditFlow.BuildingBlocks.ValueObjects;

namespace CreditFlow.Modules.Propostas.Domain;

/// <summary>
/// Parâmetros da análise automática (seção <c>Propostas:Analise</c>). Valores DIDÁTICOS: política de
/// crédito real é decisão de risco, versionada e auditada.
/// </summary>
public sealed class ParametrosDeAnalise
{
    public const string Secao = "Propostas:Analise";

    /// <summary>Score abaixo disto: recusa automática.</summary>
    public int ScoreMinimo { get; set; } = 300;
    /// <summary>Score a partir disto pode ser aprovado sem analista (se as outras condições valerem).</summary>
    public int ScoreAprovacaoAutomatica { get; set; } = 700;
    /// <summary>Maior parcela / renda acima disto: recusa automática (0,30 = 30%).</summary>
    public decimal ComprometimentoMaximo { get; set; } = 0.30m;
    /// <summary>Comprometimento até aqui permite aprovação automática.</summary>
    public decimal ComprometimentoAprovacaoAutomatica { get; set; } = 0.20m;
    /// <summary>Valor máximo aprovado sem analista.</summary>
    public decimal ValorMaximoAprovacaoAutomatica { get; set; } = 20_000m;
    /// <summary>Idade máxima da simulação para virar proposta.</summary>
    public int ValidadeDaSimulacaoDias { get; set; } = 7;
    /// <summary>Quanto tempo uma aprovação espera a contratação.</summary>
    public int ValidadeDaAprovacaoDias { get; set; } = 7;
}

public enum Decisao { Aprovar, Recusar, EncaminharParaAnalista }

/// <param name="Codigo">Estável, vai para o evento e para a tela do analista.</param>
public sealed record ResultadoDaAnalise(Decisao Decisao, string Codigo, string Explicacao, decimal Comprometimento);

/// <summary>
/// As regras da análise automática, puras: nada de banco, relógio ou HTTP. É o código mais importante
/// do módulo e o mais fácil de testar, por isso não depende de nada.
/// </summary>
public static class AnaliseDeCredito
{
    public static ResultadoDaAnalise Avaliar(int score, Dinheiro rendaMensal, Dinheiro maiorParcela, Dinheiro valorSolicitado, ParametrosDeAnalise p)
    {
        ArgumentNullException.ThrowIfNull(p);
        // Renda zero não deveria existir (Clientes exige renda > 0), mas a regra não pode dividir por zero.
        var comprometimento = rendaMensal.Valor <= 0 ? decimal.MaxValue : maiorParcela.Valor / rendaMensal.Valor;

        if (score < p.ScoreMinimo)
            return new(Decisao.Recusar, "score-insuficiente", $"Score {score} abaixo do mínimo {p.ScoreMinimo}.", comprometimento);

        if (comprometimento > p.ComprometimentoMaximo)
            return new(Decisao.Recusar, "comprometimento-de-renda",
                $"A maior parcela compromete {comprometimento:P1} da renda; o limite é {p.ComprometimentoMaximo:P0}.", comprometimento);

        if (score >= p.ScoreAprovacaoAutomatica
            && comprometimento <= p.ComprometimentoAprovacaoAutomatica
            && valorSolicitado.Valor <= p.ValorMaximoAprovacaoAutomatica)
            return new(Decisao.Aprovar, "aprovacao-automatica", "Score, comprometimento e valor dentro da alçada automática.", comprometimento);

        return new(Decisao.EncaminharParaAnalista, "analise-manual", "Fora da alçada automática; precisa de um analista.", comprometimento);
    }
}
