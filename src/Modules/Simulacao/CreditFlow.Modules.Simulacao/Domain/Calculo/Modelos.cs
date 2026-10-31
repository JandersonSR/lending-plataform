using CreditFlow.BuildingBlocks.ValueObjects;

namespace CreditFlow.Modules.Simulacao.Domain.Calculo;

public enum SistemaDeAmortizacao
{
    /// <summary>Tabela Price (sistema francês): parcelas iguais; amortização cresce, juros caem.</summary>
    Price = 1,
    /// <summary>SAC: amortização constante; parcelas começam maiores e caem.</summary>
    Sac = 2,
}

/// <summary>
/// Regras de IOF para pessoa física (DIDÁTICAS: confira a regulamentação vigente da Receita Federal,
/// Decreto 6.306/2007 e alterações, antes de usar em qualquer coisa real).
/// IOF diário sobre cada amortização × dias até o vencimento (limitado a <see cref="LimiteDeDias"/>)
/// + IOF adicional sobre o principal.
/// </summary>
public sealed record RegrasDeIof(decimal AliquotaDiaria, int LimiteDeDias, decimal AliquotaAdicional)
{
    /// <summary>0,0082% ao dia, até 365 dias, + 0,38% adicional.</summary>
    public static RegrasDeIof PessoaFisica { get; } = new(0.000082m, 365, 0.0038m);

    /// <summary>Sem IOF: útil em teste (o CET deve bater com a taxa de juros).</summary>
    public static RegrasDeIof Isento { get; } = new(0m, 365, 0m);
}

public sealed record ParametrosDoCalculo(
    Dinheiro Principal,
    int PrazoMeses,
    Percentual TaxaMensal,
    SistemaDeAmortizacao Sistema,
    DateOnly DataContratacao,
    RegrasDeIof Iof);

/// <summary>Uma linha da tabela. Todos os valores em centavos exatos (já arredondados).</summary>
public sealed record ParcelaCalculada(
    int Numero,
    DateOnly Vencimento,
    Dinheiro Valor,
    Dinheiro Juros,
    Dinheiro Amortizacao,
    Dinheiro SaldoDevedor);

public sealed record IofCalculado(Dinheiro Diario, Dinheiro Adicional)
{
    public Dinheiro Total => Diario + Adicional;
}

public sealed record ResultadoDoCalculo(
    IReadOnlyList<ParcelaCalculada> Parcelas,
    IofCalculado Iof,
    Dinheiro ValorLiberado,
    Dinheiro TotalJuros,
    Dinheiro TotalPago,
    Percentual CetMensal,
    Percentual CetAnual);
