using CreditFlow.BuildingBlocks.Mensageria;

namespace CreditFlow.Modules.Pagamentos.Application.Gateway;

/// <summary>
/// O PSP (provedor de pagamento) que faz o PIX. O contrato copia o que os PSPs reais oferecem:
/// toda ordem leva uma chave de idempotência; repetir a mesma chave devolve a MESMA transação
/// (não manda o dinheiro duas vezes); e dá para perguntar "o que aconteceu com esta chave?".
/// </summary>
public interface IGatewayPix
{
    /// <summary>
    /// Envia o PIX. Lança <see cref="GatewayPixIndisponivelException"/> quando não dá para saber o
    /// resultado (fora do ar, timeout): o dinheiro PODE ter saído. Repita com a mesma chave.
    /// </summary>
    Task<RespostaPix> EnviarAsync(PedidoPix pedido, CancellationToken ct);

    /// <summary>
    /// Resolve a dúvida antes de desistir: se a chave já foi processada, devolve a transação; se não,
    /// CANCELA a chave (o PSP passa a recusar qualquer envio com ela) e devolve null. Depois disto,
    /// "não saiu dinheiro" é um fato, e não um palpite.
    /// </summary>
    Task<string?> ObterOuCancelarAsync(string chaveIdempotencia, CancellationToken ct);
}

public sealed record PedidoPix(string ChaveIdempotencia, string ChavePix, long ValorCentavos, string Descricao);

public sealed record RespostaPix(bool Aceito, string? IdTransacao, string? CodigoRecusa, string? MotivoRecusa)
{
    public static RespostaPix Aprovado(string idTransacao) => new(true, idTransacao, null, null);
    public static RespostaPix Recusado(string codigo, string motivo) => new(false, null, codigo, motivo);
}

/// <summary>Resultado desconhecido: o gateway não respondeu. Transitória para o consumidor.</summary>
public sealed class GatewayPixIndisponivelException(string mensagem, Exception? interna = null)
    : ErroTransitorioException(mensagem, interna);
