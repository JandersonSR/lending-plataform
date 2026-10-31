namespace CreditFlow.Modules.Simulacao.Domain.Calculo;

/// <summary>
/// <see cref="Math.Pow(double, double)"/> só existe para double. Em dinheiro usamos decimal, então a
/// potência inteira é feita por multiplicação (exponenciação rápida: log2(n) multiplicações).
/// Com expoente até 48 e base perto de 1, os 28 dígitos do decimal sobram.
/// </summary>
internal static class MatematicaDecimal
{
    public static decimal Potencia(decimal @base, int expoente)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(expoente);
        var resultado = 1m;
        var fator = @base;
        var e = expoente;
        while (e > 0)
        {
            if ((e & 1) == 1)
                resultado *= fator;
            e >>= 1;
            if (e > 0)
                fator *= fator;
        }
        return resultado;
    }

    /// <summary>Arredondamento bancário para centavos (meio para o par).</summary>
    public static decimal Centavos(decimal valor) => decimal.Round(valor, 2, MidpointRounding.ToEven);
}
