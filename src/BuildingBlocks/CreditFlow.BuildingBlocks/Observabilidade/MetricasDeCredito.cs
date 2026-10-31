using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace CreditFlow.BuildingBlocks.Observabilidade;

/// <summary>
/// Métricas de NEGÓCIO do crédito. São elas que respondem "o produto está funcionando?",
/// coisa que CPU e latência HTTP não respondem sozinhas. Registrada como singleton por
/// <see cref="ObservabilidadeExtensions.AddObservabilidade{TBuilder}"/>; injete nos handlers.
///
/// Nomes no Prometheus (o exportador do Collector troca "." por "_" e acrescenta unidade e sufixo):
///   creditflow.simulacoes             -> creditflow_simulacoes_total
///   creditflow.simulacao.duracao (s)  -> creditflow_simulacao_duracao_seconds_bucket/_sum/_count
///   creditflow.propostas              -> creditflow_propostas_total
///   creditflow.desembolsos            -> creditflow_desembolsos_total
///   creditflow.desembolso.duracao (s) -> creditflow_desembolso_duracao_seconds_bucket (SLO do desembolso)
///   creditflow.valor.contratado (BRL) -> creditflow_valor_contratado_BRL_total
/// </summary>
public sealed class MetricasDeCredito
{
    /// <summary>Limites do histograma de duração do desembolso, em segundos. O SLO (5 min) PRECISA coincidir com um limite.</summary>
    public static readonly double[] LimitesDesembolsoSegundos = [1, 5, 15, 30, 60, 120, 300, 600, 1800, 3600];

    /// <summary>Limites do histograma do cálculo da simulação, em segundos (é CPU pura: milissegundos).</summary>
    public static readonly double[] LimitesSimulacaoSegundos = [0.001, 0.0025, 0.005, 0.01, 0.025, 0.05, 0.1, 0.25, 0.5, 1];

    private readonly Counter<long> _simulacoes;
    private readonly Histogram<double> _duracaoSimulacao;
    private readonly Counter<long> _propostas;
    private readonly Counter<long> _desembolsos;
    private readonly Histogram<double> _duracaoDesembolso;
    private readonly Counter<double> _valorContratado;

    public MetricasDeCredito(IMeterFactory fabrica)
    {
        ArgumentNullException.ThrowIfNull(fabrica);
        var medidor = fabrica.Create(new MeterOptions(Telemetria.NomeDaFonte) { Version = Telemetria.Versao });

        _simulacoes = medidor.CreateCounter<long>(
            "creditflow.simulacoes", unit: "{simulacao}",
            description: "Simulações calculadas, por sistema de amortização e faixa de prazo");
        _duracaoSimulacao = medidor.CreateHistogram<double>(
            "creditflow.simulacao.duracao", unit: "s",
            description: "Tempo de CPU do cálculo da simulação (tabela, IOF, CET)");
        _propostas = medidor.CreateCounter<long>(
            "creditflow.propostas", unit: "{proposta}",
            description: "Propostas por resultado (criada, aprovada, recusada, contratada)");
        _desembolsos = medidor.CreateCounter<long>(
            "creditflow.desembolsos", unit: "{desembolso}",
            description: "Desembolsos finalizados, por resultado (concluido, falhou) e motivo");
        _duracaoDesembolso = medidor.CreateHistogram<double>(
            "creditflow.desembolso.duracao", unit: "s",
            description: "Do ContratoAssinado ao DesembolsoConcluido/Falhou (base do SLO do desembolso)");
        _valorContratado = medidor.CreateCounter<double>(
            "creditflow.valor.contratado", unit: "BRL",
            description: "Soma do principal contratado (didático: confira a regulação antes de usar em relatório)");
    }

    /// <summary>Uma simulação calculada. <paramref name="sistema"/>: "price" ou "sac".</summary>
    public void RegistrarSimulacao(string sistema, int prazoMeses, TimeSpan duracaoDoCalculo)
    {
        var tags = new TagList
        {
            { Telemetria.Atributos.Sistema, sistema },
            { Telemetria.Atributos.FaixaPrazo, Telemetria.FaixaDePrazo(prazoMeses) },
        };
        _simulacoes.Add(1, tags);
        _duracaoSimulacao.Record(duracaoDoCalculo.TotalSeconds, tags);
    }

    /// <summary>Mudança de estado de proposta. <paramref name="resultado"/>: criada | aprovada | recusada | contratada.</summary>
    public void RegistrarProposta(string resultado, string? motivo = null)
    {
        var tags = new TagList { { Telemetria.Atributos.Resultado, resultado } };
        if (!string.IsNullOrWhiteSpace(motivo))
            tags.Add(Telemetria.Atributos.Motivo, motivo);
        _propostas.Add(1, tags);
    }

    /// <summary>Contrato assinado: soma o principal ao total contratado.</summary>
    public void RegistrarContratacao(decimal principal)
    {
        _valorContratado.Add((double)principal,
            new TagList { { Telemetria.Atributos.FaixaValor, Telemetria.FaixaDeValor(principal) } });
        RegistrarProposta("contratada");
    }

    /// <summary>
    /// Desembolso finalizado. <paramref name="desdeOContrato"/> = agora - ContratoAssinado.OcorridoEm.
    /// <paramref name="resultado"/>: "concluido" ou "falhou"; <paramref name="motivo"/>: código curto
    /// ("gateway_indisponivel", "chave_pix_invalida"...). O SLO conta só falhas técnicas.
    /// </summary>
    public void RegistrarDesembolso(string resultado, TimeSpan desdeOContrato, string? motivo = null)
    {
        var tags = new TagList { { Telemetria.Atributos.Resultado, resultado } };
        if (!string.IsNullOrWhiteSpace(motivo))
            tags.Add(Telemetria.Atributos.Motivo, motivo);
        _desembolsos.Add(1, tags);
        _duracaoDesembolso.Record(Math.Max(0, desdeOContrato.TotalSeconds), tags);
    }
}
