using CreditFlow.BuildingBlocks.Modulos;
using CreditFlow.Modules.Clientes.Api;
using CreditFlow.Modules.Clientes.Application;
using CreditFlow.Modules.Clientes.Contracts;
using CreditFlow.Modules.Clientes.Infrastructure.Memoria;
using CreditFlow.Modules.Clientes.Infrastructure.Postgres;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace CreditFlow.Modules.Clientes;

/// <summary>
/// Ponto de entrada do módulo para o Host. É o único tipo que o Host precisa conhecer.
/// Persistência: Postgres (schema "clientes") se houver ConnectionStrings:Postgres; senão, memória.
/// </summary>
public sealed class ClientesModule : IModule, IModuloComContratos
{
    public string Nome => "Clientes";

    public void Registrar(IServiceCollection services, IConfiguration configuration)
    {
        RegistrarContratos(services, configuration);
        services.AddScoped<CadastrarCliente>();
        services.AddScoped<ObterCliente>();
        services.AddScoped<ObterClienteDoUsuario>();
        services.AddScoped<RevogarConsentimento>();

        if (UsaPostgres(configuration))
        {
            services.AddSingleton<MigracaoDoSchemaClientes>();
            services.AddSingleton<IMigracao>(sp => sp.GetRequiredService<MigracaoDoSchemaClientes>());
            if (configuration.GetValue<bool>("Clientes:CriarSchemaAoIniciar"))
                services.AddHostedService<InicializadorDoSchema>();
        }
    }

    /// <summary>
    /// O que é preciso para atender <see cref="IConsultaDeClientes"/>, mesmo num processo em que Clientes
    /// não está ativo (ex.: api-simulacao). Lê o mesmo schema; não sobe endpoints nem jobs.
    /// </summary>
    public void RegistrarContratos(IServiceCollection services, IConfiguration configuration)
    {
        services.TryAddSingleton(TimeProvider.System);

        if (UsaPostgres(configuration))
        {
            services.AddDbContext<ClientesDbContext>(o => o.UseNpgsql(
                configuration.GetConnectionString("Postgres"),
                npgsql => npgsql.MigrationsHistoryTable("__ef_migracoes", ClientesDbContext.Schema)));
            services.TryAddScoped<IClienteRepositorio, ClienteRepositorioEf>();
        }
        else
        {
            services.TryAddSingleton<IClienteRepositorio, ClienteRepositorioEmMemoria>();
        }

        services.TryAddScoped<IConsultaDeClientes, ConsultaDeClientes>();
    }

    public void Mapear(IEndpointRouteBuilder endpoints) => ClientesEndpoints.Mapear(endpoints);

    private static bool UsaPostgres(IConfiguration configuration) =>
        !string.IsNullOrWhiteSpace(configuration.GetConnectionString("Postgres"))
        && !string.Equals(configuration["Clientes:Persistencia"], "Memoria", StringComparison.OrdinalIgnoreCase);
}
