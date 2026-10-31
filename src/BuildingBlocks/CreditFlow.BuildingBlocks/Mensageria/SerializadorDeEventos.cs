using System.Text.Json;
using System.Text.Json.Serialization;

namespace CreditFlow.BuildingBlocks.Mensageria;

/// <summary>
/// JSON dos eventos de integração. Leitor tolerante: campos desconhecidos são ignorados
/// (o padrão do System.Text.Json), então o produtor pode ACRESCENTAR campos sem quebrar
/// consumidores antigos. camelCase e enums como texto: o contrato é lido por gente e por
/// consumidores que não são .NET. Dinheiro trafega em centavos (long), nunca em double.
/// </summary>
public static class SerializadorDeEventos
{
    public static JsonSerializerOptions Opcoes { get; } = CriarOpcoes();

    private static JsonSerializerOptions CriarOpcoes()
    {
        var opcoes = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        };
        opcoes.Converters.Add(new JsonStringEnumConverter());
        opcoes.MakeReadOnly(populateMissingResolver: true);
        return opcoes;
    }

    public static string Serializar(object evento, Type tipo) => JsonSerializer.Serialize(evento, tipo, Opcoes);

    public static object Desserializar(ReadOnlySpan<byte> utf8, Type tipo) =>
        JsonSerializer.Deserialize(utf8, tipo, Opcoes)
        ?? throw new ErroPermanenteException($"O corpo da mensagem desserializou para null ({tipo.Name}).");
}
