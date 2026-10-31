using CreditFlow.BuildingBlocks;
using CreditFlow.BuildingBlocks.ValueObjects;

namespace CreditFlow.Modules.Simulacao.Domain;

public readonly record struct SimulacaoId(Guid Valor)
{
    public static SimulacaoId Novo() => new(Guid.CreateVersion7());
    public override string ToString() => Valor.ToString();
}

/// <summary>Valor pedido: de R$ 1.000,00 a R$ 50.000,00, em centavos exatos.</summary>
public readonly record struct ValorDoEmprestimo
{
    public const decimal Minimo = 1_000m;
    public const decimal Maximo = 50_000m;

    private ValorDoEmprestimo(Dinheiro valor) => Valor = valor;

    public Dinheiro Valor { get; }

    public static Result<ValorDoEmprestimo> Criar(decimal valor)
    {
        var dinheiro = Dinheiro.Criar(valor, "valor");
        if (dinheiro.Falhou)
            return dinheiro.Erro;
        if (valor is < Minimo or > Maximo)
            return SimulacaoErros.ValorForaDaFaixa;
        return new ValorDoEmprestimo(dinheiro.Valor);
    }
}

/// <summary>Prazo: de 6 a 48 meses.</summary>
public readonly record struct PrazoEmMeses
{
    public const int Minimo = 6;
    public const int Maximo = 48;

    private PrazoEmMeses(int meses) => Meses = meses;

    public int Meses { get; }

    public static Result<PrazoEmMeses> Criar(int meses) =>
        meses is < Minimo or > Maximo ? SimulacaoErros.PrazoForaDaFaixa : new PrazoEmMeses(meses);
}

/// <summary>
/// Faixa de score e a taxa mensal que ela paga. Quem tem score maior oferece menos risco e paga menos.
/// <see cref="ScoreMinimo"/> é inclusivo: score 800 cai na faixa de mínimo 800.
/// </summary>
public sealed record FaixaDeScore(string Nome, int ScoreMinimo, Percentual TaxaMensal);

/// <summary>Escolhe a faixa de um score. Sem score (cliente anônimo ou sem consentimento): a faixa de maior risco.</summary>
public static class SeletorDeFaixa
{
    public static FaixaDeScore Escolher(IReadOnlyList<FaixaDeScore> faixas, int? score)
    {
        ArgumentNullException.ThrowIfNull(faixas);
        if (faixas.Count == 0)
            throw new InvalidOperationException("A tabela de taxas está vazia.");

        var ordenadas = faixas.OrderByDescending(f => f.ScoreMinimo).ToList();
        if (score is null)
            return ordenadas[^1];
        return ordenadas.FirstOrDefault(f => score.Value >= f.ScoreMinimo) ?? ordenadas[^1];
    }
}

public static class SimulacaoErros
{
    public static readonly Erro ValorForaDaFaixa = Erro.Validacao(
        "simulacao.valor_fora_da_faixa", $"O valor deve estar entre {ValorDoEmprestimo.Minimo:N2} e {ValorDoEmprestimo.Maximo:N2}.", "valor");

    public static readonly Erro PrazoForaDaFaixa = Erro.Validacao(
        "simulacao.prazo_fora_da_faixa", $"O prazo deve estar entre {PrazoEmMeses.Minimo} e {PrazoEmMeses.Maximo} meses.", "prazoMeses");

    public static readonly Erro SistemaInvalido = Erro.Validacao(
        "simulacao.sistema_invalido", "Sistema de amortização inválido. Use 'price' ou 'sac'.", "sistema");

    public static readonly Erro ClienteNaoEncontrado = Erro.NaoEncontrado(
        "simulacao.cliente_nao_encontrado", "Cliente informado não existe.");

    public static readonly Erro NaoEncontrada = Erro.NaoEncontrado(
        "simulacao.nao_encontrada", "Simulação não encontrada.");
}
