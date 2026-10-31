namespace CreditFlow.BuildingBlocks.Mensageria;

/// <summary>Seção <c>Mensageria:Outbox</c>.</summary>
public sealed class OutboxOpcoes
{
    public const string Secao = "Mensageria:Outbox";

    /// <summary>Quantas faixas um relay pega por ciclo. 4 de 16 = até 4 relays trabalhando em paralelo.</summary>
    public int FaixasPorCiclo { get; set; } = 4;

    /// <summary>Máximo de mensagens por ciclo (por transação).</summary>
    public int TamanhoDoLote { get; set; } = 200;

    /// <summary>Espera quando não há nada pendente. É a latência típica outbox → Kafka quando o sistema está ocioso.</summary>
    public TimeSpan IntervaloOcioso { get; set; } = TimeSpan.FromMilliseconds(500);

    /// <summary>Teto do recuo exponencial depois de falhas de publicação (Kafka fora).</summary>
    public TimeSpan EsperaMaximaAposFalha { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>Publicadas há mais que isto são apagadas. A outbox não é histórico: o histórico é o tópico.</summary>
    public TimeSpan RetencaoDasPublicadas { get; set; } = TimeSpan.FromDays(3);

    /// <summary>
    /// Inbox: registros mais antigos que isto são apagados. Tem de ser MAIOR que a retenção do tópico
    /// (7 dias no Strimzi): senão um reprocessamento desde o início do tópico repetiria efeitos.
    /// </summary>
    public TimeSpan RetencaoDaInbox { get; set; } = TimeSpan.FromDays(14);

    public TimeSpan IntervaloDeLimpeza { get; set; } = TimeSpan.FromMinutes(10);
}
