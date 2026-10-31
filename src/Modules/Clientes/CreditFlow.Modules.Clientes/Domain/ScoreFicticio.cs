using CreditFlow.BuildingBlocks.ValueObjects;

namespace CreditFlow.Modules.Clientes.Domain;

/// <summary>
/// Score FICTÍCIO de 0 a 1000, determinístico (o mesmo CPF e renda dão sempre o mesmo score), para
/// os testes e as demonstrações serem reproduzíveis. Num sistema real viria de um bureau
/// (Serasa, Boa Vista...) por uma porta de saída, com cache e fallback.
/// </summary>
public static class ScoreFicticio
{
    public const int Minimo = 0;
    public const int Maximo = 1000;

    public static int Calcular(Cpf cpf, Dinheiro rendaMensal)
    {
        ArgumentNullException.ThrowIfNull(cpf);

        // Base de 300 a 800 a partir dos 9 primeiros dígitos (espalha bem e não depende do verificador).
        var soma = 0;
        for (var i = 0; i < 9; i++)
            soma += (cpf.Digitos[i] - '0') * (i + 3);
        var baseDoCpf = 300 + soma * 37 % 501;

        var bonusDeRenda = rendaMensal.Valor switch
        {
            >= 10_000m => 150,
            >= 5_000m => 80,
            >= 2_500m => 30,
            _ => 0,
        };

        return Math.Clamp(baseDoCpf + bonusDeRenda, Minimo, Maximo);
    }
}
