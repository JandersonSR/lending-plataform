using System.Globalization;
using CreditFlow.BuildingBlocks.Mensageria;
using CreditFlow.Modules.Pagamentos.Contracts.Eventos;
using CreditFlow.Modules.Propostas.Contracts.Eventos;

namespace CreditFlow.Modules.Notificacoes.Application;

// Manipuladores dos eventos de Pagamentos e de Propostas (módulo 6). Idempotência: índice único
// (evento_id, canal) no Mongo, dentro de Notificar.

internal sealed class AoDesembolsoConcluido(Notificar notificar) : IManipuladorDeEvento<DesembolsoConcluido>
{
    public Task ManipularAsync(DesembolsoConcluido e, ContextoDaMensagem contexto, CancellationToken ct) =>
        notificar.ExecutarAsync(e.EventoId, e.ClienteId, e.PropostaId, "desembolso-concluido",
            "Seu crédito foi liberado",
            $"Enviamos {Reais(e.ValorCentavos)} por PIX. Sua primeira parcela vence em cerca de um mês.", ct);

    internal static string Reais(long centavos) =>
        (centavos / 100m).ToString("C2", CultureInfo.GetCultureInfo("pt-BR"));
}

internal sealed class AoDesembolsoFalhou(Notificar notificar) : IManipuladorDeEvento<DesembolsoFalhou>
{
    public Task ManipularAsync(DesembolsoFalhou e, ContextoDaMensagem contexto, CancellationToken ct) =>
        notificar.ExecutarAsync(e.EventoId, e.ClienteId, e.PropostaId, "desembolso-falhou",
            "Não conseguimos liberar seu crédito",
            "O PIX não foi concluído e a contratação foi desfeita. Nenhum valor será cobrado.", ct);
}

internal sealed class AoParcelaPaga(Notificar notificar) : IManipuladorDeEvento<ParcelaPaga>
{
    public Task ManipularAsync(ParcelaPaga e, ContextoDaMensagem contexto, CancellationToken ct) =>
        notificar.ExecutarAsync(e.EventoId, e.ClienteId, e.PropostaId, e.UltimaParcela ? "contrato-quitado" : "parcela-paga",
            e.UltimaParcela ? "Contrato quitado" : $"Parcela {e.Numero} paga",
            $"Recebemos {AoDesembolsoConcluido.Reais(e.ValorPagoCentavos)}.", ct);
}

internal sealed class AoPropostaAprovada(Notificar notificar) : IManipuladorDeEvento<PropostaAprovada>
{
    public Task ManipularAsync(PropostaAprovada e, ContextoDaMensagem contexto, CancellationToken ct) =>
        notificar.ExecutarAsync(e.EventoId, e.ClienteId, e.PropostaId, "proposta-aprovada",
            "Sua proposta foi aprovada",
            $"Você pode contratar até {e.ValidaAte.ToOffset(TimeSpan.FromHours(-3)).ToString("dd/MM/yyyy", CultureInfo.GetCultureInfo("pt-BR"))}. Depois disso, será preciso simular de novo.", ct);
}

internal sealed class AoPropostaRecusada(Notificar notificar) : IManipuladorDeEvento<PropostaRecusada>
{
    // O código do motivo não vai para o cliente: comunicar recusa de crédito tem regras próprias
    // (e o texto é responsabilidade do jurídico/produto), então a mensagem é genérica.
    public Task ManipularAsync(PropostaRecusada e, ContextoDaMensagem contexto, CancellationToken ct) =>
        notificar.ExecutarAsync(e.EventoId, e.ClienteId, e.PropostaId, "proposta-recusada",
            "Sua proposta não foi aprovada",
            "Desta vez não foi possível aprovar o crédito. Você pode fazer uma nova simulação no futuro.", ct);
}
