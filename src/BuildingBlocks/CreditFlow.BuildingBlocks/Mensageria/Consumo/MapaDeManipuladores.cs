using Microsoft.Extensions.DependencyInjection;

namespace CreditFlow.BuildingBlocks.Mensageria;

/// <summary>
/// Os eventos que um consumidor entende: (tipo, versão) → tipo .NET → manipulador.
/// Tipo que não está aqui é ignorado (o tópico tem eventos que não interessam a este grupo).
/// Tipo que está aqui com versão que não está: falha permanente → DLT (o produtor está à frente do consumidor).
/// </summary>
public sealed class MapaDeManipuladores
{
    private readonly Dictionary<(string Tipo, int Versao), Entrada> _entradas = new();
    private readonly HashSet<string> _tipos = new(StringComparer.Ordinal);

    internal sealed record Entrada(DescritorDeEvento Descritor,
        Func<IServiceProvider, object, ContextoDaMensagem, CancellationToken, Task> Despachar);

    public IEnumerable<DescritorDeEvento> Eventos => _entradas.Values.Select(e => e.Descritor);

    internal void Adicionar<TEvento>() where TEvento : class, IEventoIntegracao
    {
        var descritor = DescritorDeEvento.De<TEvento>();
        if (!_entradas.TryAdd((descritor.Tipo, descritor.Versao), new Entrada(descritor,
                static (sp, evento, ctx, ct) => sp.GetRequiredService<IManipuladorDeEvento<TEvento>>().ManipularAsync((TEvento)evento, ctx, ct))))
            throw new InvalidOperationException($"Já existe manipulador para {descritor.Tipo} v{descritor.Versao} neste consumidor.");
        _tipos.Add(descritor.Tipo);
    }

    internal bool Conhece(string tipo) => _tipos.Contains(tipo);

    internal Entrada? Obter(string tipo, int versao) => _entradas.GetValueOrDefault((tipo, versao));
}
