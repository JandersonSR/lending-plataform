using CreditFlow.BuildingBlocks.Mensageria;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OpenTelemetry;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace CreditFlow.BuildingBlocks.Observabilidade;

/// <summary>
/// Os três sinais (traces, métricas, logs) com OpenTelemetry, exportados por OTLP para o
/// OpenTelemetry Collector do cluster (ou para o grafana/otel-lgtm no ambiente local).
///
/// O destino vem das variáveis padrão do OpenTelemetry, que o chart Helm define:
///   OTEL_EXPORTER_OTLP_ENDPOINT (ex.: http://otel-collector.observabilidade.svc:4317)
///   OTEL_EXPORTER_OTLP_PROTOCOL (grpc | http/protobuf)
///   OTEL_SERVICE_NAME (creditflow-api-simulacao...), OTEL_RESOURCE_ATTRIBUTES (pod, namespace, nó)
/// Sem OTEL_EXPORTER_OTLP_ENDPOINT, a instrumentação continua ligada mas nada é exportado
/// (rodar testes e o "dotnet run" sem coletor não gera erro nem atraso).
/// </summary>
public static class ObservabilidadeExtensions
{
    private static readonly string[] CaminhosIgnorados = ["/healthz", "/metrics", "/favicon.ico"];

    public static TBuilder AddObservabilidade<TBuilder>(this TBuilder builder) where TBuilder : IHostApplicationBuilder
    {
        ArgumentNullException.ThrowIfNull(builder);
        var cfg = builder.Configuration;

        var papel = cfg["CreditFlow:Papel"] ?? "api";
        var modulos = cfg["CreditFlow:Modulos"] ?? "todos";
        var nomeDoServico = cfg["OTEL_SERVICE_NAME"] ?? $"creditflow-{papel.ToLowerInvariant()}";

        builder.Services.AddSingleton<MetricasDeCredito>();

        // Logs: o provedor do OpenTelemetry entra ao lado do console. IncludeScopes leva os
        // escopos de log (ex.: correlation id) como atributos; o TraceId/SpanId vai sozinho.
        builder.Logging.AddOpenTelemetry(o =>
        {
            o.IncludeFormattedMessage = true;
            o.IncludeScopes = true;
            o.ParseStateValues = true;
        });

        var otel = builder.Services.AddOpenTelemetry()
            .ConfigureResource(r => r
                .AddService(serviceName: nomeDoServico, serviceNamespace: "creditflow", serviceVersion: Telemetria.Versao)
                .AddAttributes(
                [
                    new("creditflow.papel", papel),
                    new("creditflow.modulos", modulos),
                    new("deployment.environment.name", builder.Environment.EnvironmentName),
                ]))
            .WithTracing(t => t
                .AddSource(Telemetria.NomeDaFonte)
                .AddSource(Rastreamento.NomeDaFonte)   // spans de publicação/consumo no Kafka (Mensageria)
                .AddSource("Npgsql")                    // Npgsql emite spans nativamente
                .AddSource("MongoDB.Driver")            // MongoDB.Driver 3.13+ (MongoTelemetry.ActivitySourceName)
                .AddAspNetCoreInstrumentation(o =>
                {
                    o.Filter = contexto => !Ignorado(contexto.Request.Path);
                    o.RecordException = true;
                })
                .AddHttpClientInstrumentation(o => o.RecordException = true))
            .WithMetrics(m => m
                .AddMeter(Telemetria.NomeDaFonte)
                .AddMeter(Rastreamento.NomeDaFonte)
                .AddMeter("Npgsql")
                .AddAspNetCoreInstrumentation()        // http.server.request.duration (base do SLO da simulação)
                .AddHttpClientInstrumentation()
                .AddRuntimeInstrumentation()           // GC, heap, threads do pool, exceções
                .AddView("creditflow.desembolso.duracao",
                    new ExplicitBucketHistogramConfiguration { Boundaries = MetricasDeCredito.LimitesDesembolsoSegundos })
                .AddView("creditflow.simulacao.duracao",
                    new ExplicitBucketHistogramConfiguration { Boundaries = MetricasDeCredito.LimitesSimulacaoSegundos }));

        if (!string.IsNullOrWhiteSpace(cfg["OTEL_EXPORTER_OTLP_ENDPOINT"]))
        {
            // Um exportador OTLP para os três sinais, configurado pelas variáveis OTEL_*.
            otel.UseOtlpExporter();
        }

        return builder;
    }

    private static bool Ignorado(PathString caminho)
    {
        foreach (var prefixo in CaminhosIgnorados)
            if (caminho.StartsWithSegments(prefixo, StringComparison.OrdinalIgnoreCase))
                return true;
        return false;
    }
}
