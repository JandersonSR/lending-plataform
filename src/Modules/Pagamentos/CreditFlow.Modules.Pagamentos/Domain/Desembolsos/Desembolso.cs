using CreditFlow.Modules.Pagamentos.Contracts.Eventos;

namespace CreditFlow.Modules.Pagamentos.Domain.Desembolsos;

public enum StatusDesembolso
{
    Solicitado,
    EmProcessamento,
    Concluido,
    Falhou,
}

/// <summary>
/// Agregado Desembolso: a ordem de mandar o dinheiro de UM contrato para o cliente.
/// Invariantes: um por proposta (chave única no banco); só sai de Solicitado/EmProcessamento para
/// Concluido ou Falhou, uma vez; a chave de idempotência do gateway é derivada do id e nunca muda,
/// então qualquer nova tentativa, de qualquer pod, é o MESMO pedido para o gateway.
/// </summary>
public sealed class Desembolso
{
    private Desembolso() { } // EF

    public Guid Id { get; private set; }
    public Guid PropostaId { get; private set; }
    public Guid ClienteId { get; private set; }
    /// <summary>Claim sub do titular no Keycloak: usada para o cliente só ver o próprio desembolso.</summary>
    public string? UsuarioId { get; private set; }
    public long ValorCentavos { get; private set; }
    public string ChavePix { get; private set; } = "";
    public StatusDesembolso Status { get; private set; }
    public int Tentativas { get; private set; }
    public string? IdTransacaoGateway { get; private set; }
    public MotivoFalhaDesembolso? Motivo { get; private set; }
    public string? DetalheFalha { get; private set; }
    public DateTimeOffset SolicitadoEm { get; private set; }
    public DateTimeOffset? ConcluidoEm { get; private set; }
    /// <summary>Token de concorrência otimista: dois pods mexendo no mesmo desembolso, um perde e relê.</summary>
    public int Versao { get; private set; }

    public bool Terminal => Status is StatusDesembolso.Concluido or StatusDesembolso.Falhou;

    /// <summary>Chave de idempotência enviada ao gateway. Determinística: mesmo desembolso, mesma chave, sempre.</summary>
    public string ChaveIdempotencia => $"desembolso-{Id:N}";

    public static Desembolso Solicitar(Guid id, Guid propostaId, Guid clienteId, string? usuarioId,
        long valorCentavos, string chavePix, DateTimeOffset agora)
    {
        if (valorCentavos <= 0)
            throw new ArgumentOutOfRangeException(nameof(valorCentavos), "Desembolso precisa de valor positivo.");
        ArgumentException.ThrowIfNullOrWhiteSpace(chavePix);

        return new Desembolso
        {
            Id = id,
            PropostaId = propostaId,
            ClienteId = clienteId,
            UsuarioId = usuarioId,
            ValorCentavos = valorCentavos,
            ChavePix = chavePix.Trim(),
            Status = StatusDesembolso.Solicitado,
            SolicitadoEm = agora,
        };
    }

    /// <summary>Registra (e persiste antes de chamar o gateway) que uma tentativa vai começar.</summary>
    public void IniciarTentativa()
    {
        GarantirNaoTerminal();
        Status = StatusDesembolso.EmProcessamento;
        Tentativas++;
        Versao++;
    }

    public void Concluir(string idTransacao, DateTimeOffset agora)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(idTransacao);
        GarantirNaoTerminal();
        Status = StatusDesembolso.Concluido;
        IdTransacaoGateway = idTransacao;
        ConcluidoEm = agora;
        Versao++;
    }

    public void Falhar(MotivoFalhaDesembolso motivo, string detalhe, DateTimeOffset agora)
    {
        GarantirNaoTerminal();
        Status = StatusDesembolso.Falhou;
        Motivo = motivo;
        DetalheFalha = detalhe.Length > 500 ? detalhe[..500] : detalhe;
        ConcluidoEm = agora;
        Versao++;
    }

    private void GarantirNaoTerminal()
    {
        if (Terminal)
            throw new InvalidOperationException($"Desembolso {Id} já está {Status}; transição recusada.");
    }
}
