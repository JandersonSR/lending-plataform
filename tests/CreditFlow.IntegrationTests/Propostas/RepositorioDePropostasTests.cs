using CreditFlow.BuildingBlocks.ValueObjects;
using CreditFlow.IntegrationTests.Infra;
using CreditFlow.Modules.Propostas.Domain;
using CreditFlow.Modules.Propostas.Infrastructure.Persistencia;
using Microsoft.EntityFrameworkCore;

namespace CreditFlow.IntegrationTests.Propostas;

/// <summary>O que só o banco de verdade prova: xmin, índice parcial, jsonb, conversões.</summary>
public sealed class RepositorioDePropostasTests(PostgresFixture pg) : IClassFixture<PostgresFixture>
{
    private static readonly DateTimeOffset Agora = new(2026, 10, 30, 12, 0, 0, TimeSpan.Zero);

    private static Proposta NovaEmAnalise(Guid cliente)
    {
        var simulacao = new CondicoesDaSimulacao(Guid.NewGuid(), cliente, new Dinheiro(10_000m), new Dinheiro(9_900m), 12,
            Percentual.DePontos(2.19m), Percentual.DePontos(35m), "price",
            [.. Enumerable.Range(1, 12).Select(n => new ParcelaPrevista(n, 95_000))], Agora);
        var r = Proposta.Criar(Guid.CreateVersion7(), cliente, "sub-ana", simulacao, 650, new Dinheiro(5_000m), true, new ParametrosDeAnalise(), Agora);
        return r.Valor;
    }

    [Fact]
    public async Task Grava_e_le_igual_inclusive_o_cronograma_em_jsonb()
    {
        var p = NovaEmAnalise(Guid.NewGuid());
        await using (var db = pg.NovoContexto())
            Assert.True(await new PropostaRepositorioEf(db).AdicionarAsync(p, CancellationToken.None));

        await using var leitura = pg.NovoContexto();
        var lida = await new PropostaRepositorioEf(leitura).ObterAsync(p.Id, CancellationToken.None);

        Assert.NotNull(lida);
        Assert.Equal(StatusProposta.EmAnalise, lida.Status);
        Assert.Equal(12, lida.Parcelas.Count);
        Assert.Equal(950m, lida.MaiorParcela.Valor);
        Assert.Equal(0.0219m, lida.TaxaMensal.Fracao);
        Assert.NotEqual(0u, lida.Versao); // xmin preenchido na leitura
    }

    [Fact]
    public async Task Segunda_proposta_ativa_do_mesmo_cliente_e_barrada_pelo_indice_parcial()
    {
        var cliente = Guid.NewGuid();
        await using var db = pg.NovoContexto();
        var repo = new PropostaRepositorioEf(db);

        Assert.True(await repo.AdicionarAsync(NovaEmAnalise(cliente), CancellationToken.None));
        Assert.False(await repo.AdicionarAsync(NovaEmAnalise(cliente), CancellationToken.None));
    }

    [Fact]
    public async Task Dois_analistas_ao_mesmo_tempo_o_segundo_recebe_conflito()
    {
        var p = NovaEmAnalise(Guid.NewGuid());
        await using (var db = pg.NovoContexto())
            await new PropostaRepositorioEf(db).AdicionarAsync(p, CancellationToken.None);

        // Duas "réplicas", cada uma com seu DbContext, leem a mesma versão.
        await using var dbA = pg.NovoContexto();
        await using var dbB = pg.NovoContexto();
        var repoA = new PropostaRepositorioEf(dbA);
        var repoB = new PropostaRepositorioEf(dbB);
        var vistaPorA = (await repoA.ObterAsync(p.Id, CancellationToken.None))!;
        var vistaPorB = (await repoB.ObterAsync(p.Id, CancellationToken.None))!;
        Assert.Equal(vistaPorA.Versao, vistaPorB.Versao);

        vistaPorA.AprovarManualmente("carla", "Renda comprovada por holerite.", Agora, 7);
        Assert.True(await repoA.SalvarAsync(vistaPorA, null, CancellationToken.None));

        vistaPorB.RecusarManualmente("alice", "Endividamento alto em outras instituições.", Agora);
        Assert.False(await repoB.SalvarAsync(vistaPorB, null, CancellationToken.None)); // WHERE xmin = <velho> → 0 linhas

        await using var leitura = pg.NovoContexto();
        var final = await leitura.Propostas.AsNoTracking().SingleAsync(x => x.Id == p.Id);
        Assert.Equal(StatusProposta.Aprovada, final.Status);
        Assert.Equal("carla", final.AnalisadoPor);
    }

    [Fact]
    public async Task If_match_com_versao_velha_e_conflito_mesmo_lendo_a_versao_nova()
    {
        var p = NovaEmAnalise(Guid.NewGuid());
        await using (var db = pg.NovoContexto())
            await new PropostaRepositorioEf(db).AdicionarAsync(p, CancellationToken.None);

        uint versaoNaTela;
        await using (var db = pg.NovoContexto())
            versaoNaTela = (await new PropostaRepositorioEf(db).ObterAsync(p.Id, CancellationToken.None))!.Versao;

        // Enquanto o analista lê a tela, outra pessoa anota algo na proposta (o status continua EmAnalise).
        await using (var db = pg.NovoContexto())
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE propostas.propostas SET justificativa = 'Cliente ligou: renda mudou' WHERE id = {p.Id}");

        // A requisição do analista lê a versão NOVA, e a regra deixaria aprovar...
        await using var dbAnalista = pg.NovoContexto();
        var repo = new PropostaRepositorioEf(dbAnalista);
        var atual = (await repo.ObterAsync(p.Id, CancellationToken.None))!;
        Assert.NotEqual(versaoNaTela, atual.Versao);
        Assert.True(atual.AprovarManualmente("carla", "Renda comprovada por holerite.", Agora, 7).Sucesso);

        // ...mas o If-Match diz "decido sobre a versão que eu VI": WHERE xmin = <versão da tela> → conflito.
        Assert.False(await repo.SalvarAsync(atual, versaoNaTela, CancellationToken.None));
    }
}
