namespace CreditFlow.BuildingBlocks.Mensageria;

/// <summary>Configuração de UM consumidor (um grupo). Sobrescrevível em <c>Mensageria:Consumidores:{grupo}</c>.</summary>
public sealed class ConsumidorOpcoes
{
    public string Grupo { get; set; } = "";
    public List<string> Topicos { get; set; } = [];

    /// <summary>Primeira espera entre tentativas; dobra a cada falha (com jitter) até <see cref="EsperaMaxima"/>.</summary>
    public TimeSpan EsperaInicial { get; set; } = TimeSpan.FromMilliseconds(500);
    public TimeSpan EsperaMaxima { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Quanto o laço pode ficar repetindo uma mensagem SEM voltar ao Consume(). Passado isso, ele faz Seek
    /// para o mesmo offset e volta ao Consume(), que devolve a mesma mensagem. Fica muito abaixo do
    /// max.poll.interval.ms (300 s): o consumidor nunca é expulso do grupo por estar "pensando".
    /// </summary>
    public TimeSpan JanelaDeRetryEmMemoria { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>Falha desconhecida que persiste por tantas tentativas vai para a DLT.</summary>
    public int MaxTentativasAntesDaDlt { get; set; } = 10;

    /// <summary>Envia os offsets guardados ao broker a cada N mensagens ou a cada intervalo, o que vier antes.</summary>
    public int CommitACadaMensagens { get; set; } = 100;
    public TimeSpan CommitACadaIntervalo { get; set; } = TimeSpan.FromSeconds(1);
}
