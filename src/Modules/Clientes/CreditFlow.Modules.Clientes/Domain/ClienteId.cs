namespace CreditFlow.Modules.Clientes.Domain;

/// <summary>Id fortemente tipado: o compilador não deixa passar um SimulacaoId onde se espera um ClienteId.</summary>
public readonly record struct ClienteId(Guid Valor)
{
    /// <summary>Guid versão 7: ordenado pelo tempo, o que é bom para índice B-tree no Postgres.</summary>
    public static ClienteId Novo() => new(Guid.CreateVersion7());

    public override string ToString() => Valor.ToString();
}
