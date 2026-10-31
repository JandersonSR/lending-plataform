using Microsoft.EntityFrameworkCore;

namespace CreditFlow.BuildingBlocks.Mensageria;

public static class OutboxDbContextExtensions
{
    /// <summary>
    /// Acrescenta o evento à outbox do DbContext. NÃO publica nada: a linha vai para o banco no mesmo
    /// <c>SaveChangesAsync</c> (portanto na mesma transação) que a mudança do agregado. É isso que mata
    /// o "dual write": ou o estado e o evento ficam gravados juntos, ou nenhum dos dois.
    /// </summary>
    /// <param name="chave">Id do agregado. Define a partição no Kafka e a faixa na outbox: mesma chave, mesma ordem.</param>
    public static MensagemOutbox AdicionarAoOutbox<TEvento>(this DbContext db, TEvento evento, string chave, DateTimeOffset agora)
        where TEvento : class, IEventoIntegracao
    {
        var mensagem = MensagemOutbox.Criar(evento, chave, agora);
        db.Set<MensagemOutbox>().Add(mensagem);
        return mensagem;
    }

    /// <summary>Nome qualificado e entre aspas de uma tabela mapeada, para o SQL escrito à mão (ex.: "pagamentos"."outbox").</summary>
    internal static string TabelaDe<TEntidade>(this DbContext db)
    {
        var entidade = db.Model.FindEntityType(typeof(TEntidade))
            ?? throw new InvalidOperationException(
                $"{typeof(TEntidade).Name} não está mapeada em {db.GetType().Name}. Chame modelBuilder.AplicarMensageria(schema) no OnModelCreating.");
        var tabela = entidade.GetTableName()!;
        var schema = entidade.GetSchema();
        return schema is null ? $"\"{tabela}\"" : $"\"{schema}\".\"{tabela}\"";
    }
}
