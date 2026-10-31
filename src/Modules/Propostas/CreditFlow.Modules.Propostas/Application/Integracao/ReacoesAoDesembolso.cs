using CreditFlow.BuildingBlocks;
using CreditFlow.BuildingBlocks.Mensageria;
using CreditFlow.Modules.Pagamentos.Contracts.Eventos;
using CreditFlow.Modules.Propostas.Domain;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace CreditFlow.Modules.Propostas.Application.Integracao;

// O fim da saga, do lado de Propostas (escrito por cf-c, dono da saga; ver PEDIDOS.md).
//
// Por que aqui NÃO há inbox: os dois passos são idempotentes pelo ESTADO do agregado.
// RegistrarDesembolsoConcluido numa proposta já Liberada devolve Ok sem gerar evento, e o
// RegistrarResultadoDoDesembolso não grava nada quando não há evento de domínio. A segunda
// entrega da mesma mensagem é um no-op natural. Inbox é para efeito que não se reconhece
// sozinho (somar 1, mandar e-mail, chamar gateway); transição de estado com guarda se reconhece.

/// <summary>DesembolsoConcluido → proposta Liberada.</summary>
internal sealed class AoDesembolsoConcluido(RegistrarResultadoDoDesembolso passo) : IManipuladorDeEvento<DesembolsoConcluido>
{
    public async Task ManipularAsync(DesembolsoConcluido evento, ContextoDaMensagem contexto, CancellationToken ct) =>
        ReacoesAoDesembolso.Traduzir(await passo.ConcluidoAsync(evento.PropostaId, ct), evento.PropostaId);
}

/// <summary>DesembolsoFalhou → compensação: a contratação é desfeita (status DesembolsoFalhou).</summary>
internal sealed class AoDesembolsoFalhou(RegistrarResultadoDoDesembolso passo) : IManipuladorDeEvento<DesembolsoFalhou>
{
    public async Task ManipularAsync(DesembolsoFalhou evento, ContextoDaMensagem contexto, CancellationToken ct) =>
        ReacoesAoDesembolso.Traduzir(
            await passo.FalhouAsync(evento.PropostaId, $"{evento.Motivo}: {evento.Detalhe}", ct), evento.PropostaId);
}

public static class ReacoesAoDesembolso
{
    public const string GrupoDeConsumidores = "creditflow-propostas";

    /// <summary>
    /// Chame no Registrar do PropostasModule (perfil api-geral). Grupo próprio: Propostas lê o tópico
    /// de Pagamentos com seus próprios offsets, sem disputar mensagens com mais ninguém.
    /// </summary>
    public static IServiceCollection AddReacoesAoDesembolso(this IServiceCollection services, IConfiguration configuration)
    {
        // RegistrarResultadoDoDesembolso já é registrado pelo PropostasModule.
        services.AddConsumidorKafka(configuration, GrupoDeConsumidores, [Topicos.Pagamentos])
            .Manipular<DesembolsoConcluido, AoDesembolsoConcluido>()
            .Manipular<DesembolsoFalhou, AoDesembolsoFalhou>();
        return services;
    }

    /// <summary>
    /// Result → política do consumidor. Transição inválida ou proposta inexistente nunca vai passar:
    /// DLT (e alguém investiga, porque a saga chegou num estado que o desenho não previa).
    /// Conflito de concorrência: transitória, o consumidor repete.
    /// </summary>
    internal static void Traduzir(Result resultado, Guid propostaId)
    {
        if (resultado.Sucesso)
            return;
        // Pelo CÓDIGO, não pelo tipo: TransicaoInvalida também é Conflito, mas nunca vai passar.
        if (resultado.Erro.Codigo == PropostasErros.AlteradaPorOutro.Codigo)
            throw new ErroTransitorioException($"Proposta {propostaId}: {resultado.Erro.Codigo} ({resultado.Erro.Mensagem}).");
        throw new ErroPermanenteException($"Proposta {propostaId}: {resultado.Erro.Codigo} ({resultado.Erro.Mensagem}).");
    }
}
