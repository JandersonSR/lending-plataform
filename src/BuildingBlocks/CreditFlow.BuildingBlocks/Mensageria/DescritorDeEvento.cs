using System.Collections.Concurrent;
using System.Reflection;

namespace CreditFlow.BuildingBlocks.Mensageria;

/// <summary>Tipo, versão e tópico de um evento, lidos uma vez do atributo e guardados em cache.</summary>
public sealed record DescritorDeEvento(string Tipo, int Versao, string Topico, Type TipoClr)
{
    private static readonly ConcurrentDictionary<Type, DescritorDeEvento> Cache = new();

    public static DescritorDeEvento De<TEvento>() => De(typeof(TEvento));

    public static DescritorDeEvento De(Type tipo) => Cache.GetOrAdd(tipo, static t =>
    {
        var atributo = t.GetCustomAttribute<EventoIntegracaoAttribute>()
            ?? throw new InvalidOperationException(
                $"{t.FullName} não tem [EventoIntegracao(tipo, versao, topico)]. Todo evento publicado precisa de nome e versão estáveis.");
        return new DescritorDeEvento(atributo.Tipo, atributo.Versao, atributo.Topico, t);
    });
}
