namespace CreditFlow.Modules.Pagamentos.Contracts;

/// <summary>
/// Consulta síncrona, in-process, que outros módulos podem usar (ex.: Propostas mostrando a situação do
/// desembolso). Só leitura: nenhum módulo muda o estado de Pagamentos por aqui; para isso existem eventos.
/// </summary>
public interface IConsultaDePagamentos
{
    Task<SituacaoDoDesembolso?> ObterDesembolsoDaPropostaAsync(Guid propostaId, CancellationToken ct = default);
}

public sealed record SituacaoDoDesembolso(
    Guid DesembolsoId,
    Guid PropostaId,
    string Status,
    long ValorCentavos,
    int Tentativas,
    string? IdTransacaoPix,
    string? MotivoFalha,
    DateTimeOffset SolicitadoEm,
    DateTimeOffset? ConcluidoEm);
