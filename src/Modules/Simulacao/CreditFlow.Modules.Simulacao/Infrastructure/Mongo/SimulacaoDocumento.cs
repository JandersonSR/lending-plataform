using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace CreditFlow.Modules.Simulacao.Infrastructure.Mongo;

/// <summary>
/// Formato da simulação no MongoDB (coleção "simulacoes"). É um tipo SÓ de persistência: o agregado
/// não conhece BSON, e mudar o documento não muda o domínio. Um documento = uma simulação com a
/// tabela inteira embutida: é lida sempre inteira, nunca parcela a parcela.
/// Nomes de campo curtos e em snake_case: o nome se repete em CADA documento (não há catálogo de colunas).
/// </summary>
internal sealed class SimulacaoDocumento
{
    /// <summary>Versão do formato. Leitura tolerante: documentos antigos continuam legíveis depois de uma mudança.</summary>
    public const int VersaoAtual = 1;

    [BsonId, BsonGuidRepresentation(GuidRepresentation.Standard)]
    public Guid Id { get; set; }

    [BsonElement("v")]
    public int VersaoDoDocumento { get; set; } = VersaoAtual;

    [BsonElement("cliente_id"), BsonGuidRepresentation(GuidRepresentation.Standard), BsonIgnoreIfNull]
    public Guid? ClienteId { get; set; }

    /// <summary>Decimal128: decimal exato. Nunca double para dinheiro (o padrão do driver para decimal é string).</summary>
    [BsonElement("valor"), BsonRepresentation(BsonType.Decimal128)]
    public decimal Valor { get; set; }

    [BsonElement("prazo_meses")]
    public int PrazoMeses { get; set; }

    /// <summary>"price" | "sac". Texto, não número do enum: reordenar o enum não corrompe o histórico.</summary>
    [BsonElement("sistema")]
    public string Sistema { get; set; } = "";

    [BsonElement("faixa")]
    public FaixaDocumento Faixa { get; set; } = new();

    /// <summary>"yyyy-MM-dd". Data sem hora não tem fuso; como DateTime viraria meia-noite UTC e "mudaria de dia" em Brasília.</summary>
    [BsonElement("data_contratacao")]
    public string DataContratacao { get; set; } = "";

    [BsonElement("criada_em")]
    public DateTime CriadaEm { get; set; }

    /// <summary>Campo do índice TTL: o MongoDB apaga o documento depois desta data.</summary>
    [BsonElement("expira_em")]
    public DateTime ExpiraEm { get; set; }

    [BsonElement("iof_diario"), BsonRepresentation(BsonType.Decimal128)]
    public decimal IofDiario { get; set; }

    [BsonElement("iof_adicional"), BsonRepresentation(BsonType.Decimal128)]
    public decimal IofAdicional { get; set; }

    [BsonElement("valor_liberado"), BsonRepresentation(BsonType.Decimal128)]
    public decimal ValorLiberado { get; set; }

    [BsonElement("total_juros"), BsonRepresentation(BsonType.Decimal128)]
    public decimal TotalJuros { get; set; }

    [BsonElement("total_pago"), BsonRepresentation(BsonType.Decimal128)]
    public decimal TotalPago { get; set; }

    [BsonElement("cet_mensal"), BsonRepresentation(BsonType.Decimal128)]
    public decimal CetMensal { get; set; }

    [BsonElement("cet_anual"), BsonRepresentation(BsonType.Decimal128)]
    public decimal CetAnual { get; set; }

    [BsonElement("parcelas")]
    public List<ParcelaDocumento> Parcelas { get; set; } = [];

    /// <summary>Campos que uma versão nova gravou e esta não conhece: preservados, não descartados nem com erro.</summary>
    [BsonExtraElements]
    public BsonDocument? Extras { get; set; }
}

internal sealed class FaixaDocumento
{
    [BsonElement("nome")]
    public string Nome { get; set; } = "";

    [BsonElement("score_minimo")]
    public int ScoreMinimo { get; set; }

    /// <summary>Fração (0,0199 = 1,99% a.m.).</summary>
    [BsonElement("taxa_mensal"), BsonRepresentation(BsonType.Decimal128)]
    public decimal TaxaMensal { get; set; }
}

internal sealed class ParcelaDocumento
{
    [BsonElement("n")]
    public int Numero { get; set; }

    [BsonElement("venc")]
    public string Vencimento { get; set; } = "";

    [BsonElement("valor"), BsonRepresentation(BsonType.Decimal128)]
    public decimal Valor { get; set; }

    [BsonElement("juros"), BsonRepresentation(BsonType.Decimal128)]
    public decimal Juros { get; set; }

    [BsonElement("amort"), BsonRepresentation(BsonType.Decimal128)]
    public decimal Amortizacao { get; set; }

    [BsonElement("saldo"), BsonRepresentation(BsonType.Decimal128)]
    public decimal SaldoDevedor { get; set; }
}
