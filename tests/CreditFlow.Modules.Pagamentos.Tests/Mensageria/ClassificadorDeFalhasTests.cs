using CreditFlow.BuildingBlocks.Mensageria;
using CreditFlow.Modules.Pagamentos.Application.Gateway;
using Microsoft.EntityFrameworkCore;

namespace CreditFlow.Modules.Pagamentos.Tests.Mensageria;

public class ClassificadorDeFalhasTests
{
    public static TheoryData<Exception, TipoDeFalha> Casos => new()
    {
        { new ErroPermanenteException("x"), TipoDeFalha.Permanente },
        { new NotSupportedException("versão"), TipoDeFalha.Permanente },
        { new GatewayPixIndisponivelException("503"), TipoDeFalha.Transitoria },
        { new TimeoutException(), TipoDeFalha.Transitoria },
        { new DbUpdateConcurrencyException("outro pod"), TipoDeFalha.Transitoria },
        // embrulhada: o classificador olha as internas
        { new InvalidOperationException("fora", new TimeoutException()), TipoDeFalha.Transitoria },
        { new NullReferenceException(), TipoDeFalha.Desconhecida },
        { new InvalidOperationException("bug"), TipoDeFalha.Desconhecida },
    };

    [Theory]
    [MemberData(nameof(Casos))]
    public void Classifica(Exception ex, TipoDeFalha esperado) => Assert.Equal(esperado, ClassificadorDeFalhas.Classificar(ex));
}
