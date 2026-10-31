using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace CreditFlow.BuildingBlocks.Modulos;

/// <summary>
/// Contrato entre um módulo de negócio e o Host. O Host não conhece nada de dentro do módulo:
/// só pede que ele registre seus serviços e mapeie seus endpoints.
/// </summary>
public interface IModule
{
    /// <summary>Nome estável, usado em CREDITFLOW__MODULOS ("Clientes", "Simulacao"...).</summary>
    string Nome { get; }

    /// <summary>Registra serviços, opções, DbContext, hosted services. Chamado só se o módulo está no perfil.</summary>
    void Registrar(IServiceCollection services, IConfiguration configuration);

    /// <summary>Mapeia os endpoints HTTP. Chamado só se o módulo está no perfil e o papel é <c>api</c>.</summary>
    void Mapear(IEndpointRouteBuilder endpoints);
}

/// <summary>
/// Opcional. Um módulo que oferece interfaces síncronas em <c>.Contracts</c> implementa isto para que
/// elas continuem disponíveis num processo onde ele NÃO está ativo (ex.: o pod api-simulacao,
/// que roda só Simulacao, mas consulta o perfil de crédito em Clientes).
/// Aqui entra só o necessário para atender o contrato: nada de endpoints, consumidores ou jobs.
/// </summary>
public interface IModuloComContratos
{
    void RegistrarContratos(IServiceCollection services, IConfiguration configuration);
}
