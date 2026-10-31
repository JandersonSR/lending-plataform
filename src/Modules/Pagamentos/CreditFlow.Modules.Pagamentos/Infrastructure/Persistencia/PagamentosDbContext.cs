using CreditFlow.BuildingBlocks.Mensageria;
using CreditFlow.Modules.Pagamentos.Domain.Desembolsos;
using CreditFlow.Modules.Pagamentos.Domain.Parcelas;
using CreditFlow.Modules.Pagamentos.Infrastructure.Gateway;
using CreditFlow.Modules.Pagamentos.Infrastructure.Idempotencia;
using Microsoft.EntityFrameworkCore;

namespace CreditFlow.Modules.Pagamentos.Infrastructure.Persistencia;

/// <summary>
/// O DbContext do módulo: só o schema <c>pagamentos</c>. Nenhum outro módulo enxerga estas tabelas;
/// quem precisa de dado de Pagamentos usa IConsultaDePagamentos ou os eventos.
/// O DDL equivalente (para revisar a migração) está em Sql/001_schema_pagamentos.sql.
/// </summary>
public sealed class PagamentosDbContext(DbContextOptions<PagamentosDbContext> options) : DbContext(options)
{
    public const string Schema = "pagamentos";

    public DbSet<Desembolso> Desembolsos => Set<Desembolso>();
    public DbSet<Parcela> Parcelas => Set<Parcela>();
    public DbSet<RequisicaoIdempotente> RequisicoesIdempotentes => Set<RequisicaoIdempotente>();
    internal DbSet<TransacaoPixSimulada> TransacoesPixSimuladas => Set<TransacaoPixSimulada>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);
        modelBuilder.AplicarMensageria(Schema); // outbox, outbox_faixas, inbox

        modelBuilder.Entity<Desembolso>(b =>
        {
            b.ToTable("desembolsos");
            b.HasKey(x => x.Id);
            b.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();
            b.Property(x => x.PropostaId).HasColumnName("proposta_id");
            b.Property(x => x.ClienteId).HasColumnName("cliente_id");
            b.Property(x => x.UsuarioId).HasColumnName("usuario_id");
            b.Property(x => x.ValorCentavos).HasColumnName("valor_centavos");
            b.Property(x => x.ChavePix).HasColumnName("chave_pix").IsRequired();
            b.Property(x => x.Status).HasColumnName("status").HasConversion<string>();
            b.Property(x => x.Tentativas).HasColumnName("tentativas");
            b.Property(x => x.IdTransacaoGateway).HasColumnName("id_transacao_gateway");
            b.Property(x => x.Motivo).HasColumnName("motivo_falha").HasConversion<string>();
            b.Property(x => x.DetalheFalha).HasColumnName("detalhe_falha");
            b.Property(x => x.SolicitadoEm).HasColumnName("solicitado_em");
            b.Property(x => x.ConcluidoEm).HasColumnName("concluido_em");
            b.Property(x => x.Versao).HasColumnName("versao").IsConcurrencyToken();
            b.Ignore(x => x.Terminal);
            b.Ignore(x => x.ChaveIdempotencia);
            // A regra "um desembolso por proposta" é do BANCO, não do código: dois pods com a mesma
            // mensagem ao mesmo tempo passam os dois pelo "if (não existe)", só um passa pelo índice.
            b.HasIndex(x => x.PropostaId).IsUnique().HasDatabaseName("uq_desembolsos_proposta");
        });

        modelBuilder.Entity<Parcela>(b =>
        {
            b.ToTable("parcelas");
            b.HasKey(x => x.Id);
            b.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();
            b.Property(x => x.DesembolsoId).HasColumnName("desembolso_id");
            b.Property(x => x.PropostaId).HasColumnName("proposta_id");
            b.Property(x => x.ClienteId).HasColumnName("cliente_id");
            b.Property(x => x.UsuarioId).HasColumnName("usuario_id");
            b.Property(x => x.Numero).HasColumnName("numero");
            b.Property(x => x.ValorCentavos).HasColumnName("valor_centavos");
            b.Property(x => x.Vencimento).HasColumnName("vencimento");
            b.Property(x => x.Status).HasColumnName("status").HasConversion<string>();
            b.Property(x => x.ValorPagoCentavos).HasColumnName("valor_pago_centavos");
            b.Property(x => x.PagaEm).HasColumnName("paga_em");
            b.Property(x => x.Versao).HasColumnName("versao").IsConcurrencyToken();
            b.HasOne<Desembolso>().WithMany().HasForeignKey(x => x.DesembolsoId);
            b.HasIndex(x => new { x.PropostaId, x.Numero }).IsUnique().HasDatabaseName("uq_parcelas_proposta_numero");
        });

        modelBuilder.Entity<RequisicaoIdempotente>(b =>
        {
            b.ToTable("requisicoes_idempotentes");
            b.HasKey(x => x.Chave);
            b.Property(x => x.Chave).HasColumnName("chave");
            b.Property(x => x.HashCorpo).HasColumnName("hash_corpo").IsRequired();
            b.Property(x => x.StatusHttp).HasColumnName("status_http");
            b.Property(x => x.Resposta).HasColumnName("resposta").HasColumnType("jsonb").IsRequired();
            b.Property(x => x.CriadaEm).HasColumnName("criada_em");
        });

        modelBuilder.Entity<TransacaoPixSimulada>(b =>
        {
            b.ToTable("pix_simulado");
            b.HasKey(x => x.ChaveIdempotencia);
            b.Property(x => x.ChaveIdempotencia).HasColumnName("chave_idempotencia");
            b.Property(x => x.Situacao).HasColumnName("situacao").IsRequired();
            b.Property(x => x.IdTransacao).HasColumnName("id_transacao");
            b.Property(x => x.ChavePix).HasColumnName("chave_pix").IsRequired();
            b.Property(x => x.ValorCentavos).HasColumnName("valor_centavos");
            b.Property(x => x.CriadaEm).HasColumnName("criada_em");
        });
    }
}
