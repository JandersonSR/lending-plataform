namespace CreditFlow.Modules.Pagamentos.Infrastructure.Gateway;

/// <summary>
/// Seção <c>Pagamentos:GatewayPix</c>. Os botões do laboratório: latência e falhas configuráveis,
/// por variável de ambiente, sem recompilar (ex.: Pagamentos__GatewayPix__TaxaDeIndisponibilidade=0.5).
/// </summary>
public sealed class GatewayPixSimuladoOpcoes
{
    public const string Secao = "Pagamentos:GatewayPix";

    public int LatenciaMinimaMs { get; set; } = 80;
    public int LatenciaMaximaMs { get; set; } = 400;

    /// <summary>Probabilidade (0 a 1) de responder "fora do ar" ANTES de processar.</summary>
    public double TaxaDeIndisponibilidade { get; set; } = 0.05;

    /// <summary>A pior falha: processa (o dinheiro sai) e a resposta se perde (timeout). Só a idempotência salva.</summary>
    public double TaxaDeTimeoutAposProcessar { get; set; } = 0.02;

    /// <summary>Probabilidade de recusa de negócio (conta bloqueada, chave inexistente).</summary>
    public double TaxaDeRecusa { get; set; } = 0.01;

    /// <summary>Chaves PIX sempre recusadas: falha determinística para teste e laboratório.</summary>
    public List<string> ChavesSempreRecusadas { get; set; } = ["recusar@creditflow.dev"];

    /// <summary>Chaves para as quais o gateway está sempre fora: exercita o esgotamento de tentativas.</summary>
    public List<string> ChavesSempreIndisponiveis { get; set; } = ["indisponivel@creditflow.dev"];

    /// <summary>Semente do sorteio (testes reprodutíveis). Nula = aleatório.</summary>
    public int? Semente { get; set; }
}
