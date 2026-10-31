using CreditFlow.BuildingBlocks.Mensageria;
using CreditFlow.BuildingBlocks.ValueObjects;
using CreditFlow.Modules.Propostas.Domain;
using Microsoft.EntityFrameworkCore;

namespace CreditFlow.Modules.Propostas.Infrastructure.Persistencia;

/// <summary>
/// DbContext do módulo, preso ao schema "propostas". Nomes snake_case declarados um a um: o SQL de
/// referência (Sql/001_schema_propostas.sql) e este mapeamento têm de dizer a mesma coisa.
/// </summary>
internal sealed class PropostasDbContext(DbContextOptions<PropostasDbContext> options) : DbContext(options)
{
    public const string Schema = "propostas";

    public DbSet<Proposta> Propostas => Set<Proposta>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);
        modelBuilder.AplicarMensageria(Schema); // outbox, outbox_faixas, inbox (módulo 6)

        modelBuilder.Entity<Proposta>(p =>
        {
            p.ToTable("propostas", t =>
            {
                t.HasCheckConstraint("ck_propostas_prazo", "prazo_meses BETWEEN 6 AND 48");
                t.HasCheckConstraint("ck_propostas_valor", "valor_solicitado BETWEEN 1000 AND 50000");
                t.HasCheckConstraint("ck_propostas_liberado", "valor_liberado > 0 AND valor_liberado <= valor_solicitado");
            });
            p.HasKey(x => x.Id);
            p.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();
            p.Property(x => x.ClienteId).HasColumnName("cliente_id");
            p.Property(x => x.SolicitanteId).HasColumnName("solicitante_id").HasMaxLength(64).IsRequired();
            p.Property(x => x.SimulacaoId).HasColumnName("simulacao_id");

            p.Property(x => x.ValorSolicitado).HasColumnName("valor_solicitado").HasPrecision(14, 2)
                .HasConversion(d => d.Valor, v => new Dinheiro(v));
            p.Property(x => x.ValorLiberado).HasColumnName("valor_liberado").HasPrecision(14, 2)
                .HasConversion(d => d.Valor, v => new Dinheiro(v));
            p.Property(x => x.PrazoMeses).HasColumnName("prazo_meses");
            p.Property(x => x.TaxaMensal).HasColumnName("taxa_mensal").HasPrecision(12, 10)
                .HasConversion(t => t.Fracao, v => Percentual.DeFracao(v));
            p.Property(x => x.CetAnual).HasColumnName("cet_anual").HasPrecision(12, 10)
                .HasConversion(t => t.Fracao, v => Percentual.DeFracao(v));
            p.Property(x => x.Sistema).HasColumnName("sistema").HasMaxLength(10).IsRequired();

            p.Property(x => x.ScoreNaAnalise).HasColumnName("score_na_analise");
            p.Property(x => x.RendaNaAnalise).HasColumnName("renda_na_analise").HasPrecision(14, 2)
                .HasConversion(d => d.Valor, v => new Dinheiro(v));
            p.Property(x => x.Comprometimento).HasColumnName("comprometimento").HasPrecision(8, 4);

            p.Property(x => x.Status).HasColumnName("status").HasConversion<string>().HasMaxLength(20);
            p.Property(x => x.DecisaoAutomatica).HasColumnName("decisao_automatica");
            p.Property(x => x.CodigoMotivo).HasColumnName("codigo_motivo").HasMaxLength(60);
            p.Property(x => x.Justificativa).HasColumnName("justificativa").HasMaxLength(1000);
            p.Property(x => x.AnalisadoPor).HasColumnName("analisado_por").HasMaxLength(120);
            p.Property(x => x.ChavePix).HasColumnName("chave_pix").HasMaxLength(77);

            p.Property(x => x.CriadaEm).HasColumnName("criada_em");
            p.Property(x => x.DecididaEm).HasColumnName("decidida_em");
            p.Property(x => x.ValidaAte).HasColumnName("valida_ate");
            p.Property(x => x.ContratadaEm).HasColumnName("contratada_em");
            p.Property(x => x.FinalizadaEm).HasColumnName("finalizada_em");

            // uint + IsRowVersion = a coluna de sistema xmin no Npgsql (não entra no CREATE TABLE).
            // O UPDATE gerado vira "... WHERE id = @id AND xmin = @versao" e lê o xmin novo com RETURNING.
            p.Property(x => x.Versao).IsRowVersion();

            // Cronograma da simulação como jsonb: é lido e gravado sempre junto com a proposta, nunca
            // consultado sozinho. Uma tabela de parcelas aqui seria normalização sem consumidor.
            p.OwnsMany(x => x.Parcelas, parcela =>
            {
                parcela.ToJson("parcelas");
                parcela.Property(x => x.Numero).HasJsonPropertyName("numero");
                parcela.Property(x => x.ValorCentavos).HasJsonPropertyName("valor_centavos");
            });
            p.Navigation(x => x.Parcelas).UsePropertyAccessMode(PropertyAccessMode.Field);

            p.Ignore(x => x.EventosDeDominio);
            p.Ignore(x => x.MaiorParcela);

            // No máximo UMA proposta ativa por cliente: quem garante entre réplicas é o banco.
            p.HasIndex(x => x.ClienteId)
                .IsUnique()
                .HasFilter("status IN ('EmAnalise', 'Aprovada', 'Contratada')")
                .HasDatabaseName("ux_propostas_cliente_ativa");
            // Fila do analista e "minhas propostas".
            p.HasIndex(x => new { x.Status, x.CriadaEm }).HasDatabaseName("ix_propostas_status_criada_em");
            p.HasIndex(x => new { x.SolicitanteId, x.CriadaEm }).HasDatabaseName("ix_propostas_solicitante_criada_em");
        });
    }
}
