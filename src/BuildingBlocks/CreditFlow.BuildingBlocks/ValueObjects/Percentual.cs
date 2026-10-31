using System.Globalization;

namespace CreditFlow.BuildingBlocks.ValueObjects;

/// <summary>
/// Uma taxa. Guardada como FRAÇÃO (2% = 0,02), que é o que entra nas fórmulas; exibida em pontos
/// percentuais ("2,00%"). Ter os dois nomes explícitos (<see cref="DePontos"/>, <see cref="DeFracao"/>)
/// evita o erro clássico de multiplicar por 2 em vez de 0,02.
/// </summary>
public readonly record struct Percentual : IComparable<Percentual>
{
    private static readonly CultureInfo PtBr = CultureInfo.GetCultureInfo("pt-BR");

    private Percentual(decimal fracao) => Fracao = fracao;

    /// <summary>0,02 para 2%.</summary>
    public decimal Fracao { get; }

    /// <summary>2 para 2%.</summary>
    public decimal Pontos => Fracao * 100m;

    public static Percentual Zero { get; } = new(0m);

    public static Percentual DeFracao(decimal fracao) => new(fracao);

    public static Percentual DePontos(decimal pontos) => new(pontos / 100m);

    public static Result<Percentual> Criar(decimal pontos, string campo = "taxa") =>
        pontos < 0
            ? Erro.Validacao("percentual.negativo", "A taxa não pode ser negativa.", campo)
            : DePontos(pontos);

    public int CompareTo(Percentual other) => Fracao.CompareTo(other.Fracao);

    public static bool operator <(Percentual a, Percentual b) => a.Fracao < b.Fracao;
    public static bool operator >(Percentual a, Percentual b) => a.Fracao > b.Fracao;
    public static bool operator <=(Percentual a, Percentual b) => a.Fracao <= b.Fracao;
    public static bool operator >=(Percentual a, Percentual b) => a.Fracao >= b.Fracao;

    /// <summary>Pontos percentuais arredondados para apresentação (padrão: 4 casas, como no CET).</summary>
    public decimal PontosArredondados(int casas = 4) => decimal.Round(Pontos, casas, MidpointRounding.ToEven);

    public override string ToString() => PontosArredondados(2).ToString("0.00", PtBr) + "%";
}
