using CreditFlow.BuildingBlocks.ValueObjects;
using CreditFlow.Modules.Clientes.Domain;
using Microsoft.EntityFrameworkCore;

namespace CreditFlow.Modules.Clientes.Infrastructure.Postgres;

/// <summary>
/// DbContext do módulo, preso ao schema "clientes". Nenhum outro módulo enxerga este tipo (é internal),
/// e nenhum DbContext de outro módulo mapeia tabelas daqui: o banco é compartilhado, o schema não.
/// </summary>
internal sealed class ClientesDbContext(DbContextOptions<ClientesDbContext> options) : DbContext(options)
{
    public const string Schema = "clientes";

    public DbSet<Cliente> Clientes => Set<Cliente>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);

        modelBuilder.Entity<Cliente>(c =>
        {
            c.ToTable("clientes");
            c.HasKey(x => x.Id);
            c.Property(x => x.Id).HasColumnName("id")
                .HasConversion(id => id.Valor, valor => new ClienteId(valor))
                .ValueGeneratedNever();
            c.Property(x => x.Nome).HasColumnName("nome").HasMaxLength(120).IsRequired();
            c.Property(x => x.Cpf).HasColumnName("cpf").HasMaxLength(11).IsRequired()
                .HasConversion(cpf => cpf.Digitos, digitos => Cpf.Criar(digitos, "cpf").Valor);
            c.HasIndex(x => x.Cpf).IsUnique().HasDatabaseName("uq_clientes_cpf");
            c.Property(x => x.UsuarioId).HasColumnName("usuario_id").HasMaxLength(64);
            c.HasIndex(x => x.UsuarioId).IsUnique().HasDatabaseName("uq_clientes_usuario");
            c.Property(x => x.RendaMensal).HasColumnName("renda_mensal").HasPrecision(14, 2)
                .HasConversion(d => d.Valor, valor => new Dinheiro(valor));
            c.Property(x => x.Score).HasColumnName("score");
            c.Property(x => x.CadastradoEm).HasColumnName("cadastrado_em");
            c.Ignore(x => x.PodeUsarParaAnaliseDeCredito);

            c.OwnsMany(x => x.Consentimentos, s =>
            {
                s.ToTable("consentimentos");
                s.WithOwner().HasForeignKey("ClienteId");
                s.Property<ClienteId>("ClienteId").HasColumnName("cliente_id")
                    .HasConversion(id => id.Valor, valor => new ClienteId(valor));
                s.HasKey(x => x.Id);
                s.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();
                s.Property(x => x.Finalidade).HasColumnName("finalidade").HasConversion<string>().HasMaxLength(40);
                s.Property(x => x.VersaoDoTermo).HasColumnName("versao_do_termo").HasMaxLength(40);
                s.Property(x => x.ConcedidoEm).HasColumnName("concedido_em");
                s.Property(x => x.RevogadoEm).HasColumnName("revogado_em");
                s.Ignore(x => x.Ativo);
            });
            c.Navigation(x => x.Consentimentos).UsePropertyAccessMode(PropertyAccessMode.Field);
        });
    }
}
