namespace CreditFlow.Modules.Simulacao.Infrastructure.Mongo;

/// <summary>Seção <c>Simulacao:Mongo</c>. A conexão vem de <c>ConnectionStrings:Mongo</c>.</summary>
public sealed class SimulacaoMongoOpcoes
{
    public const string Secao = "Simulacao:Mongo";

    /// <summary>Usado se a connection string não trouxer o banco (mongodb://host:27017/<b>creditflow</b>).</summary>
    public string BancoPadrao { get; set; } = "creditflow";

    public string Colecao { get; set; } = "simulacoes";

    /// <summary>
    /// Quanto tempo o histórico fica. A simulação vale 7 dias para virar proposta, e a proposta COPIA as
    /// condições, então apagar depois disso não quebra nada. 90 dias atende estatística e suporte; mais
    /// que isso é dado pessoal guardado sem finalidade (LGPD).
    /// </summary>
    public int RetencaoDias { get; set; } = 90;
}
