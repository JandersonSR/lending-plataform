using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace CreditFlow.BuildingBlocks.Modulos;

public static class ModulosExtensions
{
    /// <summary>
    /// Registra os módulos do perfil. Módulos fora do perfil que oferecem contratos síncronos
    /// (<see cref="IModuloComContratos"/>) registram só o necessário para atender esses contratos.
    /// </summary>
    public static IServiceCollection AddModulos(
        this IServiceCollection services, IConfiguration configuration,
        IReadOnlyCollection<IModule> catalogo, PerfilDeExecucao perfil)
    {
        ArgumentNullException.ThrowIfNull(catalogo);
        ArgumentNullException.ThrowIfNull(perfil);

        services.AddSingleton(perfil);
        foreach (var modulo in catalogo)
        {
            if (perfil.Executa(modulo.Nome))
                modulo.Registrar(services, configuration);
            else if (modulo is IModuloComContratos comContratos)
                comContratos.RegistrarContratos(services, configuration);
        }
        return services;
    }

    /// <summary>Mapeia os endpoints dos módulos ativos. Num processo <c>worker</c>, não mapeia nada de negócio.</summary>
    public static IEndpointRouteBuilder MapModulos(
        this IEndpointRouteBuilder endpoints, IReadOnlyCollection<IModule> catalogo, PerfilDeExecucao perfil)
    {
        ArgumentNullException.ThrowIfNull(catalogo);
        ArgumentNullException.ThrowIfNull(perfil);

        if (!perfil.EhApi)
            return endpoints;

        foreach (var modulo in catalogo.Where(m => perfil.Executa(m.Nome)))
            modulo.Mapear(endpoints);
        return endpoints;
    }
}
