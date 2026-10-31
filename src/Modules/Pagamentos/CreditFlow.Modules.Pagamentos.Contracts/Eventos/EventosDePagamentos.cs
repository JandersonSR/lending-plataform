using CreditFlow.BuildingBlocks;
using CreditFlow.BuildingBlocks.Mensageria;

namespace CreditFlow.Modules.Pagamentos.Contracts.Eventos;

// Eventos de integração de Pagamentos (tópico creditflow.pagamentos.v1, chave = PropostaId).
// A chave é a proposta, e não o desembolso, porque a proposta é o id de correlação da saga:
// todos os fatos de pagamento de um contrato caem na mesma partição e chegam em ordem
// (DesembolsoSolicitado → DesembolsoConcluido → ParcelaPaga 1 → ... → ParcelaPaga N).
// Desembolso é 1:1 com a proposta, então a chave continua sendo a "identidade do agregado" na prática.
// Dinheiro em centavos (long): nada de double em contrato público.

/// <summary>Pagamentos aceitou o contrato e vai tentar o PIX. Ainda não saiu dinheiro.</summary>
[EventoIntegracao("DesembolsoSolicitado", 1, Topicos.Pagamentos)]
public sealed record DesembolsoSolicitado(
    Guid EventoId,
    DateTimeOffset OcorridoEm,
    Guid DesembolsoId,
    Guid PropostaId,
    Guid ClienteId,
    long ValorCentavos) : IEventoIntegracao;

/// <summary>O PIX foi aceito pelo gateway. As parcelas existem a partir daqui.</summary>
[EventoIntegracao("DesembolsoConcluido", 1, Topicos.Pagamentos)]
public sealed record DesembolsoConcluido(
    Guid EventoId,
    DateTimeOffset OcorridoEm,
    Guid DesembolsoId,
    Guid PropostaId,
    Guid ClienteId,
    long ValorCentavos,
    string IdTransacaoPix,
    DateTimeOffset ConcluidoEm,
    int QuantidadeDeParcelas) : IEventoIntegracao;

/// <summary>Não saiu dinheiro, e não vai sair por esta saga. Propostas compensa (cancela a contratação).</summary>
[EventoIntegracao("DesembolsoFalhou", 1, Topicos.Pagamentos)]
public sealed record DesembolsoFalhou(
    Guid EventoId,
    DateTimeOffset OcorridoEm,
    Guid DesembolsoId,
    Guid PropostaId,
    Guid ClienteId,
    MotivoFalhaDesembolso Motivo,
    string Detalhe,
    int Tentativas) : IEventoIntegracao;

public enum MotivoFalhaDesembolso
{
    /// <summary>O gateway recusou (chave PIX inexistente, conta bloqueada...). Tentar de novo não adianta.</summary>
    RecusadoPeloGateway,

    /// <summary>Gateway indisponível até o fim das tentativas, e a consulta confirmou que nada foi enviado.</summary>
    GatewayIndisponivel,

    /// <summary>O contrato chegou com dados que Pagamentos não aceita (valor, cronograma).</summary>
    DadosInvalidos,
}

/// <summary>Uma parcela foi baixada. <see cref="UltimaParcela"/> = o contrato está quitado.</summary>
[EventoIntegracao("ParcelaPaga", 1, Topicos.Pagamentos)]
public sealed record ParcelaPaga(
    Guid EventoId,
    DateTimeOffset OcorridoEm,
    Guid ParcelaId,
    Guid PropostaId,
    Guid ClienteId,
    int Numero,
    long ValorPagoCentavos,
    DateTimeOffset PagaEm,
    bool UltimaParcela) : IEventoIntegracao;
