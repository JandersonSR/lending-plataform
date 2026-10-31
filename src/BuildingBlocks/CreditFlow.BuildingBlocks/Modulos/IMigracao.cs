namespace CreditFlow.BuildingBlocks.Modulos;

/// <summary>
/// Um passo de migração de esquema de um módulo (DDL no Postgres, índices no Mongo...). Idempotente:
/// pode rodar a cada deploy. O Host roda todas com <c>CreditFlow.Host migrar</c> (o Job de migração do Helm,
/// com o usuário de banco que tem permissão de DDL) e sai, sem subir o Kestrel.
/// Registre em <see cref="IModule.Registrar"/> com <c>services.AddSingleton&lt;IMigracao, X&gt;()</c> (ou Scoped).
/// </summary>
public interface IMigracao
{
    /// <summary>Nome para o log ("clientes: schema"...).</summary>
    string Nome { get; }

    Task AplicarAsync(CancellationToken ct);
}
