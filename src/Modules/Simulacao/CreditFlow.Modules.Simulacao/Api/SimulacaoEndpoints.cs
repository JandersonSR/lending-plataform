using CreditFlow.BuildingBlocks.Api;
using CreditFlow.Modules.Simulacao.Application;
using CreditFlow.Modules.Simulacao.Domain;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace CreditFlow.Modules.Simulacao.Api;

/// <summary>Corpo do POST /v1/simulacoes. "sistema": "price" ou "sac". "clienteId" é opcional.</summary>
public sealed record SimularRequest(decimal Valor, int PrazoMeses, string? Sistema, Guid? ClienteId);

public sealed record ParcelaResponse(int Numero, DateOnly Vencimento, decimal Valor, decimal Juros, decimal Amortizacao, decimal SaldoDevedor);

/// <summary>
/// Resposta pública. Taxas em pontos percentuais já arredondados para exibir (o arredondamento
/// acontece AQUI, na apresentação, e não no cálculo). Sem clienteId: a simulação é consultável por
/// quem tiver o id, e ela não precisa dizer de quem é.
/// </summary>
public sealed record SimulacaoResponse(
    Guid Id,
    decimal Valor,
    int PrazoMeses,
    string Sistema,
    string FaixaDeScore,
    decimal TaxaMensalPercentual,
    decimal IofDiario,
    decimal IofAdicional,
    decimal IofTotal,
    decimal ValorLiberado,
    decimal TotalJuros,
    decimal TotalPago,
    decimal CetMensalPercentual,
    decimal CetAnualPercentual,
    DateOnly DataContratacao,
    DateTimeOffset ValidaAte,
    IReadOnlyList<ParcelaResponse> Parcelas)
{
    internal static SimulacaoResponse De(SimulacaoDeEmprestimo s)
    {
        var r = s.Resultado;
        return new SimulacaoResponse(
            s.Id.Valor, s.Valor.Valor.Valor, s.Prazo.Meses, MapeamentoDeContratos.NomeDoSistema(s.Sistema), s.Faixa.Nome,
            s.Faixa.TaxaMensal.PontosArredondados(2),
            r.Iof.Diario.Valor, r.Iof.Adicional.Valor, r.Iof.Total.Valor,
            r.ValorLiberado.Valor, r.TotalJuros.Valor, r.TotalPago.Valor,
            r.CetMensal.PontosArredondados(4), r.CetAnual.PontosArredondados(2),
            s.DataContratacao, s.ValidaAte,
            [.. r.Parcelas.Select(p => new ParcelaResponse(
                p.Numero, p.Vencimento, p.Valor.Valor, p.Juros.Valor, p.Amortizacao.Valor, p.SaldoDevedor.Valor))]);
    }
}

public sealed record FaixaResponse(string Nome, int ScoreMinimo, decimal TaxaMensalPercentual);

internal static class SimulacaoEndpoints
{
    public const string PoliticaDeLimite = "simulacao-publica";

    public static void Mapear(IEndpointRouteBuilder endpoints)
    {
        var grupo = endpoints.MapGroup("/v1/simulacoes")
            .WithTags("Simulações")
            .AllowAnonymous()                       // público de propósito: simular não exige login
            .RequireRateLimiting(PoliticaDeLimite);  // ...por isso tem limite por IP

        grupo.MapGet("/taxas", async (ITabelaDeTaxas tabela, CancellationToken ct) =>
            {
                var faixas = await tabela.ObterFaixasAsync(ct);
                return TypedResults.Ok(faixas
                    .OrderByDescending(f => f.ScoreMinimo)
                    .Select(f => new FaixaResponse(f.Nome, f.ScoreMinimo, f.TaxaMensal.PontosArredondados(2)))
                    .ToList());
            })
            .WithName("ListarTaxas")
            .WithSummary("Faixas de score e taxa de juros mensal de cada uma");

        grupo.MapPost("/", async (SimularRequest req, SimularEmprestimo casoDeUso, CancellationToken ct) =>
            {
                var resultado = await casoDeUso.ExecutarAsync(
                    new SimularEmprestimoComando(req.Valor, req.PrazoMeses, req.Sistema, req.ClienteId), ct);
                return resultado.ParaHttp(s => TypedResults.Created($"/v1/simulacoes/{s.Id}", SimulacaoResponse.De(s)));
            })
            .WithName("Simular")
            .WithSummary("Simula um empréstimo (Price ou SAC) com IOF, CET e a tabela de parcelas")
            .Produces<SimulacaoResponse>(StatusCodes.Status201Created)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status429TooManyRequests);

        grupo.MapGet("/{id:guid}", async (Guid id, ObterSimulacao casoDeUso, CancellationToken ct) =>
                (await casoDeUso.ExecutarAsync(id, ct)).ParaHttp(s => TypedResults.Ok(SimulacaoResponse.De(s))))
            .WithName("ObterSimulacao")
            .WithSummary("Obtém uma simulação já feita")
            .Produces<SimulacaoResponse>()
            .ProducesProblem(StatusCodes.Status404NotFound);
    }
}
