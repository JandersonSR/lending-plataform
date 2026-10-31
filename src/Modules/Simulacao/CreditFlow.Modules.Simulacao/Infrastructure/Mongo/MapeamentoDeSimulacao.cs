using System.Globalization;
using CreditFlow.BuildingBlocks.ValueObjects;
using CreditFlow.Modules.Simulacao.Domain;
using CreditFlow.Modules.Simulacao.Domain.Calculo;

namespace CreditFlow.Modules.Simulacao.Infrastructure.Mongo;

/// <summary>Agregado ↔ documento. Escrito à mão: é chato, mas é o único lugar que conhece os dois.</summary>
internal static class MapeamentoDeSimulacao
{
    private const string FormatoData = "yyyy-MM-dd";

    public static SimulacaoDocumento ParaDocumento(SimulacaoDeEmprestimo s, TimeSpan retencao)
    {
        ArgumentNullException.ThrowIfNull(s);
        var r = s.Resultado;
        var criadaEm = s.CriadaEm.UtcDateTime;
        return new SimulacaoDocumento
        {
            Id = s.Id.Valor,
            ClienteId = s.ClienteId,
            Valor = s.Valor.Valor.Valor,
            PrazoMeses = s.Prazo.Meses,
            Sistema = s.Sistema.ToString().ToLowerInvariant(),
            Faixa = new FaixaDocumento { Nome = s.Faixa.Nome, ScoreMinimo = s.Faixa.ScoreMinimo, TaxaMensal = s.Faixa.TaxaMensal.Fracao },
            DataContratacao = s.DataContratacao.ToString(FormatoData, CultureInfo.InvariantCulture),
            CriadaEm = criadaEm,
            ExpiraEm = criadaEm + retencao,
            IofDiario = r.Iof.Diario.Valor,
            IofAdicional = r.Iof.Adicional.Valor,
            ValorLiberado = r.ValorLiberado.Valor,
            TotalJuros = r.TotalJuros.Valor,
            TotalPago = r.TotalPago.Valor,
            CetMensal = r.CetMensal.Fracao,
            CetAnual = r.CetAnual.Fracao,
            Parcelas = [.. r.Parcelas.Select(p => new ParcelaDocumento
            {
                Numero = p.Numero,
                Vencimento = p.Vencimento.ToString(FormatoData, CultureInfo.InvariantCulture),
                Valor = p.Valor.Valor,
                Juros = p.Juros.Valor,
                Amortizacao = p.Amortizacao.Valor,
                SaldoDevedor = p.SaldoDevedor.Valor,
            })],
        };
    }

    public static SimulacaoDeEmprestimo ParaAgregado(SimulacaoDocumento d)
    {
        ArgumentNullException.ThrowIfNull(d);
        if (d.VersaoDoDocumento > SimulacaoDocumento.VersaoAtual)
            throw new InvalidOperationException(
                $"Simulação {d.Id} gravada no formato v{d.VersaoDoDocumento}, mais novo que este código (v{SimulacaoDocumento.VersaoAtual}).");

        var sistema = Enum.Parse<SistemaDeAmortizacao>(d.Sistema, ignoreCase: true);
        var resultado = new ResultadoDoCalculo(
            [.. d.Parcelas.OrderBy(p => p.Numero).Select(p => new ParcelaCalculada(
                p.Numero, Data(p.Vencimento), new Dinheiro(p.Valor), new Dinheiro(p.Juros),
                new Dinheiro(p.Amortizacao), new Dinheiro(p.SaldoDevedor)))],
            new IofCalculado(new Dinheiro(d.IofDiario), new Dinheiro(d.IofAdicional)),
            new Dinheiro(d.ValorLiberado),
            new Dinheiro(d.TotalJuros),
            new Dinheiro(d.TotalPago),
            Percentual.DeFracao(d.CetMensal),
            Percentual.DeFracao(d.CetAnual));

        return SimulacaoDeEmprestimo.Reconstituir(
            new SimulacaoId(d.Id), d.ClienteId, d.Valor, d.PrazoMeses, sistema,
            new FaixaDeScore(d.Faixa.Nome, d.Faixa.ScoreMinimo, Percentual.DeFracao(d.Faixa.TaxaMensal)),
            Data(d.DataContratacao),
            new DateTimeOffset(DateTime.SpecifyKind(d.CriadaEm, DateTimeKind.Utc)),
            resultado);
    }

    private static DateOnly Data(string texto) => DateOnly.ParseExact(texto, FormatoData, CultureInfo.InvariantCulture);
}
