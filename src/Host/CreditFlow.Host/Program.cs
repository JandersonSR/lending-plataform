using System.Globalization;
using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using CreditFlow.BuildingBlocks.Auth;
using CreditFlow.BuildingBlocks.Mensageria;
using CreditFlow.BuildingBlocks.Modulos;
using CreditFlow.BuildingBlocks.Observabilidade;
using CreditFlow.Modules.Clientes;
using CreditFlow.Modules.Notificacoes;
using CreditFlow.Modules.Pagamentos;
using CreditFlow.Modules.Propostas;
using CreditFlow.Modules.Simulacao;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Mvc;
using CreditFlow.Host;
using Scalar.AspNetCore;

// Modos especiais, ANTES de montar o host:
//   --healthcheck  a imagem chiseled não tem curl: o HEALTHCHECK do Dockerfile chama o próprio binário.
//   migrar         o Job de migração do Helm: aplica as migrações (IMigracao) dos módulos do perfil e sai.
if (args.Contains("--healthcheck"))
    return await Sondas.VerificarVivoAsync();

var modoMigracao = args.Length > 0 && args[0] == "migrar";
var builder = WebApplication.CreateBuilder(args.Where(a => a is not "migrar" and not "--healthcheck").ToArray());

// 1. Catálogo EXPLÍCITO: nada de varrer assemblies por reflexão. Quem lê o Program.cs sabe tudo o que existe.
IModule[] catalogo =
[
    new ClientesModule(),
    new SimulacaoModule(),
    new PropostasModule(),
    new PagamentosModule(),
    new NotificacoesModule(),
];

// 2. O perfil deste processo: CREDITFLOW__MODULOS e CREDITFLOW__PAPEL (mesma imagem, N deployments).
var perfil = PerfilDeExecucao.Ler(builder.Configuration, catalogo);

// 3. Blocos transversais. Mensageria ANTES dos módulos: o esvaziador do produtor precisa ser
//    registrado antes dos relays/consumidores para parar DEPOIS deles no desligamento.
builder.AddObservabilidade();
builder.Services.AddAutenticacaoCreditFlow(builder.Configuration);
builder.Services.AddMensageria(builder.Configuration);

builder.Services.AddProblemDetails(o => o.CustomizeProblemDetails = ctx =>
{
    // Todo erro sai com o traceId: o suporte cola o id e acha o trace e os logs da requisição.
    ctx.ProblemDetails.Extensions["traceId"] = System.Diagnostics.Activity.Current?.TraceId.ToString() ?? ctx.HttpContext.TraceIdentifier;
});
builder.Services.ConfigureHttpJsonOptions(o => o.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));
builder.Services.AddOpenApi(o => o.AddDocumentTransformer((documento, _, _) =>
{
    documento.Info ??= new();
    documento.Info.Title = "CreditFlow API";
    documento.Info.Description = $"Perfil deste processo: {perfil}";
    return Task.CompletedTask;
}));
builder.Services.AddRateLimiter(o =>
{
    o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    o.OnRejected = async (contexto, ct) =>
    {
        if (contexto.Lease.TryGetMetadata(MetadataName.RetryAfter, out var esperar))
            contexto.HttpContext.Response.Headers.RetryAfter = ((int)Math.Ceiling(esperar.TotalSeconds)).ToString(CultureInfo.InvariantCulture);

        var problemas = contexto.HttpContext.RequestServices.GetRequiredService<IProblemDetailsService>();
        await problemas.WriteAsync(new ProblemDetailsContext
        {
            HttpContext = contexto.HttpContext,
            ProblemDetails = new ProblemDetails
            {
                Status = StatusCodes.Status429TooManyRequests,
                Title = "Muitas requisições",
                Detail = "Limite de requisições atingido. Tente de novo depois do tempo indicado em Retry-After.",
                Type = "urn:creditflow:erro:limite_de_requisicoes",
                Extensions = { ["code"] = "limite_de_requisicoes" },
            },
        });
    };
});
builder.Services.AddExceptionHandler<DependenciaIndisponivelHandler>(); // 503 para banco/Mongo fora; o resto vira 500
builder.Services.AddHealthChecks(); // módulos adicionam checks com a tag "ready" (Postgres, Mongo, Kafka...)

// 4. Os módulos do perfil (e, dos demais, só os contratos síncronos que alguém pode consultar).
builder.Services.AddModulos(builder.Configuration, catalogo, perfil);

var app = builder.Build();

if (modoMigracao)
    return await Migracoes.AplicarAsync(app.Services, app.Logger);

app.Logger.LogInformation("CreditFlow iniciando. Perfil: {Perfil}", perfil.ToString());

// 5. Pipeline. A ordem importa: erro primeiro (pega exceção de tudo que vem depois),
//    auth antes do rate limit (o limite pode depender de quem é), endpoints por último.
app.UseExceptionHandler();
app.UseStatusCodePages();
app.UseAutenticacaoCreditFlow();
app.UseRateLimiter();

// Sondas do Kubernetes. Liveness só olha o PRÓPRIO processo (checks com a tag "live", ex.: o laço do consumidor
// Kafka ainda gira?); banco fora não se resolve reiniciando o pod. Readiness olha as dependências ("ready")
// e tira o pod do balanceamento enquanto elas estiverem fora.
app.MapHealthChecks("/healthz/live", new HealthCheckOptions { Predicate = c => c.Tags.Contains("live") }).AllowAnonymous();
app.MapHealthChecks("/healthz/ready", new HealthCheckOptions { Predicate = c => c.Tags.Contains("ready") }).AllowAnonymous();

if (perfil.EhApi)
{
    app.MapOpenApi().AllowAnonymous();                                   // /openapi/v1.json
    app.MapScalarApiReference(o => o.WithTitle("CreditFlow")).AllowAnonymous(); // /scalar
}

app.MapModulos(catalogo, perfil);

await app.RunAsync();
return 0;

/// <summary>Visível para o WebApplicationFactory&lt;Program&gt; dos testes de integração.</summary>
public partial class Program;
