using CreditFlow.BuildingBlocks.Auth;
using CreditFlow.Modules.Propostas.Application;
using CreditFlow.Modules.Propostas.Domain;

namespace CreditFlow.Modules.Propostas.Tests;

public sealed class ConsultarPropostaTests
{
    private sealed class Usuario(string id, params string[] papeis) : ICurrentUser
    {
        public bool Autenticado => true;
        public string? Id => id;
        public string? Nome => id;
        public string? ClienteOAuth => "creditflow-web";
        public bool EhMaquina => false;
        public IReadOnlyCollection<string> Papeis => papeis;
        public bool TemPapel(string papel) => papeis.Contains(papel);
    }

    [Theory]
    [InlineData("sub-ana", Papeis.Cliente, true)]       // dona
    [InlineData("sub-bruno", Papeis.Cliente, false)]    // outro cliente: 404
    [InlineData("sub-carla", Papeis.Analista, true)]
    [InlineData("sub-alice", Papeis.Admin, true)]
    public void So_a_dona_e_a_analise_veem(string sub, string papel, bool ve)
    {
        var proposta = Construtor.Criar();
        Assert.Equal(ve, ConsultarProposta.PodeVer(new Usuario(sub, papel), proposta));
    }
}
