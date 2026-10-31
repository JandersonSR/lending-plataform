using System.Text;
using System.Text.Json;

namespace CreditFlow.BuildingBlocks.Mensageria;

/// <summary>
/// Uma linha da outbox: o evento gravado na MESMA transação da mudança de estado que o gerou.
/// Se a transação faz rollback, o evento some junto; se faz commit, o relay publica depois.
/// </summary>
public sealed class MensagemOutbox
{
    /// <summary>
    /// Número de faixas (lanes). Mensagens da mesma chave caem sempre na mesma faixa, e cada faixa
    /// só é trabalhada por um relay de cada vez (FOR UPDATE SKIP LOCKED na tabela de faixas):
    /// é o que preserva a ordem por chave com vários relays em paralelo.
    /// Mudar este número com mensagens pendentes pode reordenar uma chave na transição: drene antes.
    /// </summary>
    public const int NumeroDeFaixas = 16;

    private MensagemOutbox() { } // EF

    public long Id { get; private set; }
    public Guid EventoId { get; private set; }
    public string Tipo { get; private set; } = "";
    public int Versao { get; private set; }
    public string Topico { get; private set; } = "";
    public string Chave { get; private set; } = "";
    public short Faixa { get; private set; }
    public string Conteudo { get; private set; } = "";
    public string Cabecalhos { get; private set; } = "{}";
    public DateTimeOffset OcorridoEm { get; private set; }
    public DateTimeOffset CriadaEm { get; private set; }
    public DateTimeOffset? PublicadaEm { get; private set; }
    public int Tentativas { get; private set; }
    public string? UltimoErro { get; private set; }

    public static MensagemOutbox Criar<TEvento>(TEvento evento, string chave, DateTimeOffset agora)
        where TEvento : class, IEventoIntegracao
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(chave);
        var descritor = DescritorDeEvento.De<TEvento>();
        var (traceParent, traceState) = Rastreamento.Capturar();
        var cabecalhos = new Dictionary<string, string>();
        if (traceParent is not null) cabecalhos[global::CreditFlow.BuildingBlocks.Mensageria.Cabecalhos.TraceParent] = traceParent;
        if (!string.IsNullOrEmpty(traceState)) cabecalhos[global::CreditFlow.BuildingBlocks.Mensageria.Cabecalhos.TraceState] = traceState;

        return new MensagemOutbox
        {
            EventoId = evento.EventoId,
            Tipo = descritor.Tipo,
            Versao = descritor.Versao,
            Topico = descritor.Topico,
            Chave = chave,
            Faixa = FaixaDa(chave),
            Conteudo = SerializadorDeEventos.Serializar(evento, typeof(TEvento)),
            Cabecalhos = JsonSerializer.Serialize(cabecalhos),
            OcorridoEm = evento.OcorridoEm,
            CriadaEm = agora,
        };
    }

    /// <summary>
    /// FNV-1a de 32 bits sobre os bytes UTF-8 da chave. Estável entre processos e versões do .NET
    /// (string.GetHashCode é aleatório por processo e NÃO serve aqui).
    /// </summary>
    public static short FaixaDa(string chave)
    {
        var hash = 2166136261u;
        foreach (var b in Encoding.UTF8.GetBytes(chave))
        {
            hash ^= b;
            hash *= 16777619u;
        }
        return (short)(hash % NumeroDeFaixas);
    }
}

/// <summary>Uma faixa da outbox. A tabela tem exatamente <see cref="MensagemOutbox.NumeroDeFaixas"/> linhas e só serve para ser travada.</summary>
public sealed class FaixaOutbox
{
    public short Faixa { get; init; }
}

/// <summary>
/// Registro de "este consumidor já processou este evento". Gravado na mesma transação do efeito:
/// se o efeito faz rollback, o registro some junto e a reentrega processa de novo.
/// </summary>
public sealed class MensagemInbox
{
    public string Consumidor { get; init; } = "";
    public Guid EventoId { get; init; }
    public string Tipo { get; init; } = "";
    public DateTimeOffset ProcessadaEm { get; init; }
}
