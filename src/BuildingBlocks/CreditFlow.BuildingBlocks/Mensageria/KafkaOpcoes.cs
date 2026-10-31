namespace CreditFlow.BuildingBlocks.Mensageria;

/// <summary>Seção <c>Kafka</c> da configuração. <c>BootstrapServers</c> cai para <c>ConnectionStrings:Kafka</c> se vazio.</summary>
public sealed class KafkaOpcoes
{
    public const string Secao = "Kafka";

    public string BootstrapServers { get; set; } = "";

    /// <summary>Prefixo do client.id (aparece nos logs do broker e no Kafka UI). O host acrescenta o nome do pod.</summary>
    public string ClientId { get; set; } = "creditflow";

    /// <summary>Cria os tópicos e DLTs ao iniciar (só no ambiente local; no AKS quem cria é o Strimzi, via KafkaTopic).</summary>
    public bool CriarTopicosAoIniciar { get; set; }

    public int ParticoesPorTopico { get; set; } = 6;

    public short FatorDeReplicacao { get; set; } = 1;

    /// <summary>Tempo máximo para uma mensagem ser entregue (inclui as novas tentativas internas do produtor).</summary>
    public int TimeoutDeEntregaMs { get; set; } = 30_000;

    // Segurança (Strimzi: listener TLS 9093 com SCRAM-SHA-512). Vazio = PLAINTEXT (docker compose local).
    // Mesmas chaves que o chart Helm injeta: Kafka__SecurityProtocol, Kafka__SaslMechanism,
    // Kafka__SaslUsername, Kafka__SaslPassword (Secret do KafkaUser) e Kafka__SslCaPem (CA do cluster, em PEM).

    /// <summary>Plaintext | Ssl | SaslPlaintext | SaslSsl.</summary>
    public string? SecurityProtocol { get; set; }

    /// <summary>ScramSha512 | ScramSha256 | Plain.</summary>
    public string? SaslMechanism { get; set; }

    public string? SaslUsername { get; set; }
    public string? SaslPassword { get; set; }

    /// <summary>O CONTEÚDO do certificado da CA do cluster (PEM), não um caminho.</summary>
    public string? SslCaPem { get; set; }
}
