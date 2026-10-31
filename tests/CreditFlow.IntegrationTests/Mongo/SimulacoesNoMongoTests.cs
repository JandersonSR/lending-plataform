using CreditFlow.BuildingBlocks.ValueObjects;
using CreditFlow.IntegrationTests.Infra;
using CreditFlow.Modules.Simulacao.Domain;
using CreditFlow.Modules.Simulacao.Domain.Calculo;
using CreditFlow.Modules.Simulacao.Infrastructure.Mongo;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using MongoDB.Bson;
using MongoDB.Driver;

namespace CreditFlow.IntegrationTests.Mongo;

public sealed class SimulacoesNoMongoTests(MongoFixture mongo) : IClassFixture<MongoFixture>
{
    private readonly IOptions<SimulacaoMongoOpcoes> _opcoes = Options.Create(new SimulacaoMongoOpcoes { Colecao = "simulacoes_" + Guid.NewGuid().ToString("N") });

    private static SimulacaoDeEmprestimo Nova(Guid? cliente = null) =>
        SimulacaoDeEmprestimo.Criar(10_000m, 12, SistemaDeAmortizacao.Price,
            new FaixaDeScore("B", 650, Percentual.DePontos(2.19m)), cliente,
            new DateOnly(2026, 10, 30), new DateTimeOffset(2026, 10, 30, 15, 0, 0, TimeSpan.Zero)).Valor;

    [Fact]
    public async Task Ida_e_volta_preserva_cada_centavo_e_a_tabela()
    {
        var repo = new SimulacaoRepositorioMongo(mongo.Banco, _opcoes);
        var original = Nova();

        await repo.SalvarAsync(original, CancellationToken.None);
        var lida = await repo.ObterAsync(original.Id, CancellationToken.None);

        Assert.NotNull(lida);
        Assert.Equal(original.Resultado.CetAnual.Fracao, lida.Resultado.CetAnual.Fracao); // Decimal128: exato
        Assert.Equal(original.Resultado.Parcelas.Select(p => (p.Numero, p.Vencimento, p.Valor.Valor)),
                     lida.Resultado.Parcelas.Select(p => (p.Numero, p.Vencimento, p.Valor.Valor)));
        Assert.Equal(original.CriadaEm, lida.CriadaEm); // ms exatos aqui; sub-ms seria truncado (BSON date)
    }

    [Fact]
    public async Task Documento_tem_os_tipos_certos_no_banco()
    {
        var repo = new SimulacaoRepositorioMongo(mongo.Banco, _opcoes);
        var s = Nova(Guid.NewGuid());
        await repo.SalvarAsync(s, CancellationToken.None);

        var cru = await mongo.Banco.GetCollection<BsonDocument>(_opcoes.Value.Colecao)
            .Find(new BsonDocument("_id", new BsonBinaryData(s.Id.Valor, GuidRepresentation.Standard))).SingleAsync();

        Assert.Equal(BsonType.Decimal128, cru["valor"].BsonType);
        Assert.Equal(BsonType.Binary, cru["cliente_id"].BsonType);
        Assert.Equal(BsonBinarySubType.UuidStandard, cru["cliente_id"].AsBsonBinaryData.SubType);
        Assert.Equal(BsonType.DateTime, cru["expira_em"].BsonType);
        Assert.Equal("price", cru["sistema"].AsString);
    }

    [Fact]
    public async Task Salvar_duas_vezes_e_idempotente()
    {
        var repo = new SimulacaoRepositorioMongo(mongo.Banco, _opcoes);
        var s = Nova();
        await repo.SalvarAsync(s, CancellationToken.None);
        await repo.SalvarAsync(s, CancellationToken.None); // E11000 engolido

        Assert.Equal(1, await repo.Colecao.CountDocumentsAsync(d => d.Id == s.Id.Valor));
    }

    [Fact]
    public async Task Indices_sao_criados_e_a_consulta_do_historico_usa_o_indice()
    {
        IHostedService indices = new IndicesDeSimulacao(mongo.Banco, _opcoes, NullLogger<IndicesDeSimulacao>.Instance);
        await indices.StartAsync(CancellationToken.None);
        await indices.StartAsync(CancellationToken.None); // idempotente

        var repo = new SimulacaoRepositorioMongo(mongo.Banco, _opcoes);
        var cliente = Guid.NewGuid();
        for (var i = 0; i < 3; i++) await repo.SalvarAsync(Nova(cliente), CancellationToken.None);
        await repo.SalvarAsync(Nova(), CancellationToken.None); // anônima: fora do índice parcial

        var nomes = await (await repo.Colecao.Indexes.ListAsync()).ToListAsync();
        Assert.Contains(nomes, i => i["name"] == "ttl_expira_em" && i["expireAfterSeconds"].ToInt32() == 0);
        Assert.Contains(nomes, i => i["name"] == "ix_cliente_criada_em" && i.Contains("partialFilterExpression"));

        Assert.Equal(3, (await repo.ListarDoClienteAsync(cliente, 10, CancellationToken.None)).Count);

        var plano = await mongo.Banco.RunCommandAsync<BsonDocument>(new BsonDocument
        {
            { "explain", new BsonDocument
                {
                    { "find", _opcoes.Value.Colecao },
                    { "filter", new BsonDocument("cliente_id", new BsonBinaryData(cliente, GuidRepresentation.Standard)) },
                    { "sort", new BsonDocument("criada_em", -1) },
                } },
            { "verbosity", "queryPlanner" },
        });
        Assert.Contains("ix_cliente_criada_em", plano["queryPlanner"]["winningPlan"].ToJson());
    }
}
