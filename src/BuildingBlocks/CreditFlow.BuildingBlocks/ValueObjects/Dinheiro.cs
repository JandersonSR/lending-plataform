using System.Globalization;

namespace CreditFlow.BuildingBlocks.ValueObjects;

/// <summary>
/// Valor em reais (BRL), sempre em <see cref="decimal"/>. Nunca double: 0.1 + 0.2 em double é
/// 0.30000000000000004, e em dinheiro isso vira centavo sumindo.
/// O valor guardado pode ter mais de 2 casas (resultado intermediário de juros); quem decide quando
/// arredondar é o domínio, chamando <see cref="EmCentavos"/>.
/// </summary>
public readonly record struct Dinheiro : IComparable<Dinheiro>
{
    private static readonly CultureInfo PtBr = CultureInfo.GetCultureInfo("pt-BR");

    public Dinheiro(decimal valor) => Valor = valor;

    public decimal Valor { get; }

    public static Dinheiro Zero { get; } = new(0m);

    /// <summary>Cria a partir de um valor informado por alguém (API, formulário): no máximo 2 casas e não negativo.</summary>
    public static Result<Dinheiro> Criar(decimal valor, string campo = "valor")
    {
        if (valor < 0)
            return Erro.Validacao("dinheiro.negativo", "O valor não pode ser negativo.", campo);
        if (decimal.Round(valor, 2) != valor)
            return Erro.Validacao("dinheiro.casas", "O valor aceita no máximo 2 casas decimais (centavos).", campo);
        return new Dinheiro(valor);
    }

    public static Dinheiro DeCentavos(long centavos) => new(centavos / 100m);

    /// <summary>
    /// Arredonda para centavos com arredondamento bancário (meio para o par: 0,125 → 0,12 e 0,135 → 0,14).
    /// Em milhões de parcelas, o "meio para cima" puxa a soma sempre para o mesmo lado; o bancário não.
    /// </summary>
    public Dinheiro EmCentavos() => new(decimal.Round(Valor, 2, MidpointRounding.ToEven));

    public long Centavos => (long)decimal.Round(Valor * 100m, 0, MidpointRounding.ToEven);

    public bool EhZero => Valor == 0m;

    public static Dinheiro operator +(Dinheiro a, Dinheiro b) => new(a.Valor + b.Valor);
    public static Dinheiro operator -(Dinheiro a, Dinheiro b) => new(a.Valor - b.Valor);
    public static Dinheiro operator *(Dinheiro a, decimal fator) => new(a.Valor * fator);
    public static Dinheiro operator /(Dinheiro a, decimal divisor) => new(a.Valor / divisor);
    public static bool operator <(Dinheiro a, Dinheiro b) => a.Valor < b.Valor;
    public static bool operator >(Dinheiro a, Dinheiro b) => a.Valor > b.Valor;
    public static bool operator <=(Dinheiro a, Dinheiro b) => a.Valor <= b.Valor;
    public static bool operator >=(Dinheiro a, Dinheiro b) => a.Valor >= b.Valor;

    public int CompareTo(Dinheiro other) => Valor.CompareTo(other.Valor);

    /// <summary>Formato de apresentação: "R$ 1.234,56".</summary>
    public override string ToString() => EmCentavos().Valor.ToString("C2", PtBr);
}
