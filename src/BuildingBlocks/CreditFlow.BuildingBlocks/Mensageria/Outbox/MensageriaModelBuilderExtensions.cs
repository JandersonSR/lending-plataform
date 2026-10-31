using Microsoft.EntityFrameworkCore;

namespace CreditFlow.BuildingBlocks.Mensageria;

public static class MensageriaModelBuilderExtensions
{
    /// <summary>
    /// Mapeia outbox, faixas e inbox no schema do módulo. Chame no <c>OnModelCreating</c> do DbContext
    /// do módulo: <c>modelBuilder.AplicarMensageria("pagamentos");</c>. A migração do módulo cria as tabelas.
    /// Os nomes de coluna são explícitos (snake_case) porque o relay e a inbox usam SQL escrito à mão.
    /// </summary>
    public static ModelBuilder AplicarMensageria(this ModelBuilder modelBuilder, string schema)
    {
        modelBuilder.Entity<MensagemOutbox>(b =>
        {
            b.ToTable("outbox", schema);
            b.HasKey(x => x.Id);
            b.Property(x => x.Id).HasColumnName("id").ValueGeneratedOnAdd();
            b.Property(x => x.EventoId).HasColumnName("evento_id");
            b.Property(x => x.Tipo).HasColumnName("tipo").IsRequired();
            b.Property(x => x.Versao).HasColumnName("versao");
            b.Property(x => x.Topico).HasColumnName("topico").IsRequired();
            b.Property(x => x.Chave).HasColumnName("chave").IsRequired();
            b.Property(x => x.Faixa).HasColumnName("faixa");
            b.Property(x => x.Conteudo).HasColumnName("conteudo").HasColumnType("jsonb").IsRequired();
            b.Property(x => x.Cabecalhos).HasColumnName("cabecalhos").HasColumnType("jsonb").IsRequired();
            b.Property(x => x.OcorridoEm).HasColumnName("ocorrido_em");
            b.Property(x => x.CriadaEm).HasColumnName("criada_em");
            b.Property(x => x.PublicadaEm).HasColumnName("publicada_em");
            b.Property(x => x.Tentativas).HasColumnName("tentativas");
            b.Property(x => x.UltimoErro).HasColumnName("ultimo_erro");
            b.HasIndex(x => x.EventoId).IsUnique().HasDatabaseName("uq_outbox_evento");
            // Índice parcial: só as pendentes. Fica pequeno mesmo com milhões de linhas publicadas.
            b.HasIndex(x => new { x.Faixa, x.Id }).HasDatabaseName("ix_outbox_pendentes").HasFilter("publicada_em IS NULL");
            b.HasIndex(x => x.PublicadaEm).HasDatabaseName("ix_outbox_publicada_em").HasFilter("publicada_em IS NOT NULL");
        });

        modelBuilder.Entity<FaixaOutbox>(b =>
        {
            b.ToTable("outbox_faixas", schema);
            b.HasKey(x => x.Faixa);
            b.Property(x => x.Faixa).HasColumnName("faixa").ValueGeneratedNever();
            b.HasData(Enumerable.Range(0, MensagemOutbox.NumeroDeFaixas).Select(i => new FaixaOutbox { Faixa = (short)i }));
        });

        modelBuilder.Entity<MensagemInbox>(b =>
        {
            b.ToTable("inbox", schema);
            b.HasKey(x => new { x.Consumidor, x.EventoId });
            b.Property(x => x.Consumidor).HasColumnName("consumidor");
            b.Property(x => x.EventoId).HasColumnName("evento_id");
            b.Property(x => x.Tipo).HasColumnName("tipo").IsRequired();
            b.Property(x => x.ProcessadaEm).HasColumnName("processada_em");
            b.HasIndex(x => x.ProcessadaEm).HasDatabaseName("ix_inbox_processada_em");
        });

        return modelBuilder;
    }
}
