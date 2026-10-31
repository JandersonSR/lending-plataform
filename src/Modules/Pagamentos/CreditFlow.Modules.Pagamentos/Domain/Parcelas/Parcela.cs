using CreditFlow.BuildingBlocks;

namespace CreditFlow.Modules.Pagamentos.Domain.Parcelas;

public enum StatusParcela
{
    Aberta,
    Paga,
}

/// <summary>
/// Agregado Parcela: uma prestação do contrato. Nasce quando o desembolso conclui (o cronograma veio
/// no ContratoAssinado, calculado pela Simulação) e morre paga. Cada parcela é um agregado próprio, e
/// não uma lista dentro do Desembolso: baixar a parcela 7 não precisa travar nem carregar as outras 47.
/// </summary>
public sealed class Parcela
{
    private Parcela() { } // EF

    public Guid Id { get; private set; }
    public Guid DesembolsoId { get; private set; }
    public Guid PropostaId { get; private set; }
    public Guid ClienteId { get; private set; }
    public string? UsuarioId { get; private set; }
    public int Numero { get; private set; }
    public long ValorCentavos { get; private set; }
    public DateOnly Vencimento { get; private set; }
    public StatusParcela Status { get; private set; }
    public long? ValorPagoCentavos { get; private set; }
    public DateTimeOffset? PagaEm { get; private set; }
    public int Versao { get; private set; }

    public static Parcela Criar(Guid desembolsoId, Guid propostaId, Guid clienteId, string? usuarioId,
        int numero, long valorCentavos, DateOnly vencimento)
    {
        if (numero <= 0) throw new ArgumentOutOfRangeException(nameof(numero));
        if (valorCentavos <= 0) throw new ArgumentOutOfRangeException(nameof(valorCentavos));

        return new Parcela
        {
            Id = Guid.CreateVersion7(),
            DesembolsoId = desembolsoId,
            PropostaId = propostaId,
            ClienteId = clienteId,
            UsuarioId = usuarioId,
            Numero = numero,
            ValorCentavos = valorCentavos,
            Vencimento = vencimento,
            Status = StatusParcela.Aberta,
        };
    }

    /// <summary>
    /// Baixa a parcela. Didático: sem pagamento parcial, sem juros de mora; o valor tem de ser o da parcela.
    /// (Na vida real: mora, multa, amortização antecipada... confira as regras do contrato e do Bacen.)
    /// </summary>
    public Result Pagar(long valorCentavos, DateTimeOffset agora)
    {
        if (Status == StatusParcela.Paga)
            return Erro.Conflito("parcela.ja_paga", $"A parcela {Numero} já foi paga em {PagaEm:O}.");
        if (valorCentavos != ValorCentavos)
            return Erro.Regra("parcela.valor_diferente",
                $"O valor pago ({valorCentavos} centavos) é diferente do valor da parcela ({ValorCentavos} centavos).");

        Status = StatusParcela.Paga;
        ValorPagoCentavos = valorCentavos;
        PagaEm = agora;
        Versao++;
        return Result.Ok();
    }
}
