using Confluent.Kafka;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CreditFlow.BuildingBlocks.Mensageria;

/// <summary>Monta as configurações de produtor, consumidor e admin a partir de <see cref="KafkaOpcoes"/>.</summary>
public sealed class FabricaDeClientesKafka(IOptions<KafkaOpcoes> opcoes, ILoggerFactory logs)
{
    private readonly KafkaOpcoes _opcoes = opcoes.Value;
    private readonly List<IProducer<string, byte[]>> _criados = [];
    private readonly Lock _trava = new();

    internal IReadOnlyList<IProducer<string, byte[]>> ProdutoresCriados { get { lock (_trava) return [.. _criados]; } }

    public KafkaOpcoes Opcoes => _opcoes;

    public ProducerConfig ConfigDoProdutor(string sufixo) => Aplicar(new ProducerConfig
    {
        ClientId = $"{_opcoes.ClientId}-{sufixo}-{Environment.MachineName}",
        // acks=all: o líder só confirma depois que TODAS as réplicas em sincronia (ISR) gravaram.
        // Com min.insync.replicas=2 no tópico (Strimzi), a queda de um broker não perde mensagem confirmada.
        Acks = Acks.All,
        // Idempotência: o broker descarta reenvios internos do produtor (número de sequência por partição),
        // então uma falha de rede no meio de um envio não duplica nem embaralha a ordem na partição.
        EnableIdempotence = true,
        // Espera até 5 ms para juntar mensagens no mesmo lote: troca latência mínima por vazão.
        LingerMs = 5,
        CompressionType = CompressionType.Lz4,
        // Teto do envio, incluindo as novas tentativas internas. Depois disso, ProduceAsync lança.
        MessageTimeoutMs = _opcoes.TimeoutDeEntregaMs,
    });

    public ConsumerConfig ConfigDoConsumidor(string grupo) => Aplicar(new ConsumerConfig
    {
        GroupId = grupo,
        ClientId = $"{_opcoes.ClientId}-{grupo}-{Environment.MachineName}",
        // Grupo novo começa do início do que está retido: o primeiro deploy processa o que já existia.
        AutoOffsetReset = AutoOffsetReset.Earliest,
        // Nada é confirmado sozinho: o offset só é guardado (StoreOffset) DEPOIS de processar
        // e só é enviado ao broker pelo Commit() explícito do ConsumidorKafka.
        EnableAutoCommit = false,
        EnableAutoOffsetStore = false,
        // Rebalanceamento incremental: quando o KEDA sobe ou desce uma réplica, só as partições
        // que mudam de dono param; as outras seguem consumindo (o modo "eager" pararia todas).
        PartitionAssignmentStrategy = PartitionAssignmentStrategy.CooperativeSticky,
        // Se o laço ficar mais que isto sem chamar Consume(), o consumidor é expulso do grupo.
        // O ConsumidorKafka nunca espera mais de 30 s entre dois Consume() (ver JanelaDeRetryEmMemoria).
        MaxPollIntervalMs = 300_000,
        SessionTimeoutMs = 45_000,
    });

    public AdminClientConfig ConfigDoAdmin() => Aplicar(new AdminClientConfig());

    public IProducer<string, byte[]> CriarProdutor(string sufixo)
    {
        var log = logs.CreateLogger("CreditFlow.Mensageria.Produtor");
        var produtor = new ProducerBuilder<string, byte[]>(ConfigDoProdutor(sufixo))
            .SetLogHandler((_, m) => log.Log(Nivel(m.Level), "librdkafka {Facility}: {Mensagem}", m.Facility, m.Message))
            .SetErrorHandler((_, e) => log.Log(e.IsFatal ? LogLevel.Critical : LogLevel.Warning,
                "Erro do produtor Kafka {Codigo}: {Motivo}", e.Code, e.Reason))
            .Build();
        lock (_trava) _criados.Add(produtor);
        return produtor;
    }

    private T Aplicar<T>(T config) where T : ClientConfig
    {
        if (string.IsNullOrWhiteSpace(_opcoes.BootstrapServers))
            throw new InvalidOperationException("Kafka:BootstrapServers (ou ConnectionStrings:Kafka) não configurado.");

        config.BootstrapServers = _opcoes.BootstrapServers;
        if (!string.IsNullOrWhiteSpace(_opcoes.SecurityProtocol))
            config.SecurityProtocol = Enum.Parse<SecurityProtocol>(_opcoes.SecurityProtocol, ignoreCase: true);
        if (!string.IsNullOrWhiteSpace(_opcoes.SaslMechanism))
            config.SaslMechanism = Enum.Parse<SaslMechanism>(_opcoes.SaslMechanism, ignoreCase: true);
        if (!string.IsNullOrWhiteSpace(_opcoes.SaslUsername))
        {
            config.SaslUsername = _opcoes.SaslUsername;
            config.SaslPassword = _opcoes.SaslPassword;
        }
        if (!string.IsNullOrWhiteSpace(_opcoes.SslCaPem))
            config.SslCaPem = _opcoes.SslCaPem;
        return config;
    }

    internal static LogLevel Nivel(SyslogLevel nivel) => nivel switch
    {
        SyslogLevel.Emergency or SyslogLevel.Alert or SyslogLevel.Critical => LogLevel.Critical,
        SyslogLevel.Error => LogLevel.Error,
        SyslogLevel.Warning => LogLevel.Warning,
        SyslogLevel.Notice or SyslogLevel.Info => LogLevel.Information,
        _ => LogLevel.Debug,
    };
}
