using CreditFlow.Modules.Simulacao.Contracts;
using CreditFlow.Modules.Simulacao.Contracts.Eventos;
using CreditFlow.Modules.Simulacao.Domain;

namespace CreditFlow.Modules.Simulacao.Application;

internal sealed class ConsultaDeSimulacoes(ISimulacaoRepositorio repositorio) : IConsultaDeSimulacoes
{
    public async Task<SimulacaoDto?> ObterAsync(Guid simulacaoId, CancellationToken ct = default)
    {
        var simulacao = await repositorio.ObterAsync(new SimulacaoId(simulacaoId), ct);
        return simulacao is null ? null : MapeamentoDeContratos.ParaDto(simulacao);
    }
}

internal static class MapeamentoDeContratos
{
    public static string NomeDoSistema(Domain.Calculo.SistemaDeAmortizacao sistema) => sistema.ToString().ToLowerInvariant();

    public static SimulacaoDto ParaDto(SimulacaoDeEmprestimo s)
    {
        var r = s.Resultado;
        return new SimulacaoDto(
            s.Id.Valor,
            s.ClienteId,
            s.Valor.Valor.Centavos,
            s.Prazo.Meses,
            NomeDoSistema(s.Sistema),
            s.Faixa.Nome,
            s.Faixa.TaxaMensal.Fracao,
            r.Iof.Total.Centavos,
            r.ValorLiberado.Centavos,
            r.TotalJuros.Centavos,
            r.TotalPago.Centavos,
            r.CetMensal.Fracao,
            r.CetAnual.Fracao,
            s.DataContratacao,
            s.CriadaEm,
            s.ValidaAte,
            [.. r.Parcelas.Select(p => new ParcelaDto(
                p.Numero, p.Vencimento, p.Valor.Centavos, p.Juros.Centavos, p.Amortizacao.Centavos, p.SaldoDevedor.Centavos))]);
    }

    public static SimulacaoRealizada ParaEvento(SimulacaoDeEmprestimo s) => new(
        Guid.CreateVersion7(),
        s.CriadaEm,
        s.Id.Valor,
        NomeDoSistema(s.Sistema),
        s.Valor.Valor.Centavos,
        s.Prazo.Meses,
        s.Faixa.TaxaMensal.Fracao,
        s.Resultado.CetAnual.Fracao,
        s.Faixa.Nome);
}
