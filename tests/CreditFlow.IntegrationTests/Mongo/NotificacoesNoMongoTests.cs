using CreditFlow.IntegrationTests.Infra;
using CreditFlow.Modules.Notificacoes.Application;
using CreditFlow.Modules.Notificacoes.Contracts;
using CreditFlow.Modules.Notificacoes.Domain;
using CreditFlow.Modules.Notificacoes.Infrastructure.Mongo;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;

namespace CreditFlow.IntegrationTests.Mongo;

public sealed class NotificacoesNoMongoTests(MongoFixture mongo) : IClassFixture<MongoFixture>
{
    private sealed class EnviadorQueConta : IEnviadorDeNotificacoes
    {
        public int Envios;
        public Task EnviarAsync(Notificacao notificacao, CancellationToken ct) { Interlocked.Increment(ref Envios); return Task.CompletedTask; }
    }

    [Fact]
    public async Task Mesmo_evento_entregue_tres_vezes_envia_uma_vez_por_canal()
    {
        IHostedService indices = new IndicesDeNotificacoes(mongo.Banco, NullLogger<IndicesDeNotificacoes>.Instance);
        await indices.StartAsync(CancellationToken.None);

        var repo = new NotificacaoRepositorioMongo(mongo.Banco);
        var enviador = new EnviadorQueConta();
        var notificar = new Notificar(repo, enviador, TimeProvider.System);
        var evento = Guid.NewGuid();
        var cliente = Guid.NewGuid();

        // Três entregas do mesmo evento, duas em paralelo (duas réplicas) e uma depois (reentrega).
        await Task.WhenAll(
            notificar.ExecutarAsync(evento, cliente, null, "teste", "t", "m", CancellationToken.None),
            notificar.ExecutarAsync(evento, cliente, null, "teste", "t", "m", CancellationToken.None));
        await notificar.ExecutarAsync(evento, cliente, null, "teste", "t", "m", CancellationToken.None);

        var lista = await repo.ListarDoClienteAsync(cliente, 10, CancellationToken.None);
        Assert.Equal(2, lista.Count); // e-mail e SMS
        Assert.All(lista, n => Assert.Equal(StatusDaNotificacao.Enviada, n.Status));
        // Em paralelo, as duas réplicas podem ver "Pendente" e enviar: pelo menos uma vez, no máximo duas por canal.
        Assert.InRange(enviador.Envios, 2, 4);
        Assert.Contains(lista, n => n.Canal == CanalDeNotificacao.Sms);
    }
}
