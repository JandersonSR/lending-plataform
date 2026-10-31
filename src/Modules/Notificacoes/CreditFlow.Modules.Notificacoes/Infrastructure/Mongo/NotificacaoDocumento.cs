using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace CreditFlow.Modules.Notificacoes.Infrastructure.Mongo;

/// <summary>Formato no MongoDB (coleção "notificacoes"). Enums como texto, datas em UTC.</summary>
internal sealed class NotificacaoDocumento
{
    [BsonId, BsonGuidRepresentation(GuidRepresentation.Standard)]
    public Guid Id { get; set; }

    [BsonElement("evento_id"), BsonGuidRepresentation(GuidRepresentation.Standard)]
    public Guid EventoId { get; set; }

    [BsonElement("canal")]
    public string Canal { get; set; } = "";

    [BsonElement("cliente_id"), BsonGuidRepresentation(GuidRepresentation.Standard)]
    public Guid ClienteId { get; set; }

    [BsonElement("proposta_id"), BsonGuidRepresentation(GuidRepresentation.Standard), BsonIgnoreIfNull]
    public Guid? PropostaId { get; set; }

    [BsonElement("tipo")]
    public string Tipo { get; set; } = "";

    [BsonElement("titulo")]
    public string Titulo { get; set; } = "";

    [BsonElement("mensagem")]
    public string Mensagem { get; set; } = "";

    [BsonElement("status")]
    public string Status { get; set; } = "";

    [BsonElement("tentativas")]
    public int Tentativas { get; set; }

    [BsonElement("ultimo_erro"), BsonIgnoreIfNull]
    public string? UltimoErro { get; set; }

    /// <summary>Também é o campo do TTL (índice com expireAfter de 180 dias).</summary>
    [BsonElement("criada_em")]
    public DateTime CriadaEm { get; set; }

    [BsonElement("enviada_em"), BsonIgnoreIfNull]
    public DateTime? EnviadaEm { get; set; }

    [BsonExtraElements]
    public BsonDocument? Extras { get; set; }
}
