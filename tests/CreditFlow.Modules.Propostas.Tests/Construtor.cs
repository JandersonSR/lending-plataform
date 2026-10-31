using CreditFlow.BuildingBlocks.ValueObjects;
using CreditFlow.Modules.Propostas.Domain;

namespace CreditFlow.Modules.Propostas.Tests;

/// <summary>Dados de teste legíveis: cada teste muda só o que importa para ele.</summary>
internal static class Construtor
{
    public static readonly DateTimeOffset Agora = new(2026, 10, 30, 12, 0, 0, TimeSpan.Zero);
    public static readonly Guid Cliente = Guid.Parse("11111111-1111-1111-1111-111111111111");
    public const string Ana = "sub-ana";

    public static CondicoesDaSimulacao Simulacao(decimal valor = 10_000m, int prazo = 12, long parcelaCentavos = 95_000,
        Guid? cliente = null, DateTimeOffset? realizadaEm = null) =>
        new(Guid.NewGuid(), cliente ?? Cliente, new Dinheiro(valor), new Dinheiro(valor - 100m), prazo,
            Percentual.DePontos(2.19m), Percentual.DePontos(35m), "price",
            [.. Enumerable.Range(1, prazo).Select(n => new ParcelaPrevista(n, parcelaCentavos))],
            realizadaEm ?? Agora.AddHours(-1));

    public static Proposta Criar(int score = 650, decimal renda = 5_000m, CondicoesDaSimulacao? simulacao = null,
        bool consentimento = true, ParametrosDeAnalise? p = null)
    {
        var r = Proposta.Criar(Guid.NewGuid(), Cliente, Ana, simulacao ?? Simulacao(), score, new Dinheiro(renda),
            consentimento, p ?? new ParametrosDeAnalise(), Agora);
        Assert.True(r.Sucesso, r.Erro?.Codigo);
        return r.Valor;
    }
}
