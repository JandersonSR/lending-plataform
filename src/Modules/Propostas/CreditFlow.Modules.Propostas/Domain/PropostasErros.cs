using CreditFlow.BuildingBlocks;

namespace CreditFlow.Modules.Propostas.Domain;

/// <summary>Códigos estáveis: o front e os testes dependem deles, nunca da mensagem.</summary>
public static class PropostasErros
{
    public static readonly Erro NaoEncontrada = Erro.NaoEncontrado("propostas.nao-encontrada", "Proposta não encontrada.");
    public static readonly Erro SimulacaoNaoEncontrada = Erro.NaoEncontrado("propostas.simulacao-nao-encontrada", "Simulação não encontrada.");
    public static readonly Erro ClienteNaoEncontrado = Erro.NaoEncontrado("propostas.cliente-nao-encontrado", "Cliente não encontrado.");
    public static readonly Erro SimulacaoVencida = Erro.Regra("propostas.simulacao-vencida", "A simulação é antiga demais; as taxas podem ter mudado. Simule de novo.");
    public static readonly Erro SimulacaoDeOutroCliente = Erro.Regra("propostas.simulacao-de-outro-cliente", "A simulação foi feita para outro cliente.");
    public static readonly Erro SemConsentimento = Erro.Regra("propostas.sem-consentimento", "O cliente não autorizou o uso dos dados para análise de crédito.");
    public static readonly Erro JaExistePropostaAtiva = Erro.Conflito("propostas.ja-existe-ativa", "O cliente já tem uma proposta em andamento.");
    public static readonly Erro AlteradaPorOutro = Erro.Conflito("propostas.alterada-por-outro", "A proposta foi alterada por outra pessoa. Recarregue e tente de novo.");
    public static readonly Erro AprovacaoVencida = Erro.Regra("propostas.aprovacao-vencida", "A aprovação venceu. Faça uma nova simulação.");
    public static readonly Erro ChavePixObrigatoria = Erro.Validacao("propostas.chave-pix", "Informe a chave PIX para receber o crédito.", "chavePix");
    public static readonly Erro MotivoObrigatorio = Erro.Validacao("propostas.motivo", "Informe o motivo (mínimo de 10 caracteres).", "motivo");
    public static readonly Erro VersaoInvalida = Erro.Validacao("propostas.if-match", "Cabeçalho If-Match inválido.", "If-Match");

    public static Erro TransicaoInvalida(StatusProposta de, string acao) =>
        Erro.Conflito("propostas.transicao-invalida", $"Não é possível {acao} uma proposta com status {de}.");
}
