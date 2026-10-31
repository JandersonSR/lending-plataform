using Confluent.Kafka;
using Confluent.Kafka.Admin;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace CreditFlow.BuildingBlocks.Mensageria;

/// <summary>
/// SÓ para o ambiente local (Kafka:CriarTopicosAoIniciar=true): cria os tópicos e as DLTs dos consumidores
/// deste processo com o número de partições certo. No AKS, auto.create.topics.enable=false e os tópicos
/// são KafkaTopic do Strimzi, versionados no Git: partições e retenção são decisão revisada, não efeito colateral.
/// </summary>
internal sealed partial class CriadorDeTopicos(FabricaDeClientesKafka fabrica, ILogger<CriadorDeTopicos> log) : IHostedService
{
    public async Task StartAsync(CancellationToken ct)
    {
        var opcoes = fabrica.Opcoes;
        var nomes = Topicos.Todos.Concat(RegistroDeConsumidores.Todos().Select(Topicos.DeMensagensMortas)).Distinct().ToList();
        using var admin = new AdminClientBuilder(fabrica.ConfigDoAdmin()).Build();
        try
        {
            await admin.CreateTopicsAsync(nomes.Select(n => new TopicSpecification
            {
                Name = n,
                NumPartitions = n.EndsWith(".dlt", StringComparison.Ordinal) ? 1 : opcoes.ParticoesPorTopico,
                ReplicationFactor = opcoes.FatorDeReplicacao,
            }));
            LogCriados(log, string.Join(", ", nomes));
        }
        catch (CreateTopicsException ex)
        {
            foreach (var r in ex.Results.Where(r => r.Error.Code is not (ErrorCode.NoError or ErrorCode.TopicAlreadyExists)))
                LogFalha(log, r.Topic, r.Error.Reason);
        }
        catch (KafkaException ex)
        {
            // Kafka fora na subida local: a aplicação sobe assim mesmo; relay e consumidor tentam de novo sozinhos.
            LogFalha(log, "(todos)", ex.Error.Reason);
        }
    }

    public Task StopAsync(CancellationToken ct) => Task.CompletedTask;

    [LoggerMessage(Level = LogLevel.Information, Message = "Tópicos garantidos: {Topicos}")]
    private static partial void LogCriados(ILogger log, string topicos);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Não foi possível criar o tópico {Topico}: {Motivo}")]
    private static partial void LogFalha(ILogger log, string topico, string motivo);
}
