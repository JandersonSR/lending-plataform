# CreditFlow — especificação compartilhada (contrato entre os autores)

Plataforma de crédito pessoal, produtiva de verdade, em **ASP.NET 10 (C# 14)** como **monolito modular com DDD**,
que roda na **Azure (AKS)**. Projeto de estudo e portfólio, com curso no learnFlow (projetos/creditflow/docs).
Código em /home/claude/creditflow (vai para github.com/JandersonSR/lending-plataform). Não há SDK .NET no ambiente:
escreva C# com cuidado (versões reais de pacotes, APIs reais do .NET 10), e o aluno compila. Não invente APIs.

## Stack fixa
- .NET 10 / ASP.NET Core Minimal APIs, EF Core 10 + Npgsql, MongoDB.Driver 3.x, HybridCache + StackExchange.Redis
  contra **Dragonfly**, **Apache Kafka** (Confluent.Kafka; local em KRaft; no AKS via **Strimzi**, não Event Hubs),
  **Keycloak** (OIDC/JWT; realm como código), **PostgreSQL**, **MongoDB**, OpenAPI nativo (Microsoft.AspNetCore.OpenApi) + Scalar,
  OpenTelemetry, xUnit + Testcontainers + NetArchTest.
- Infra: Docker, Kubernetes, **Helm**, **Terraform** (azurerm), **Harbor** (registry, não ACR), **Argo CD** (GitOps),
  KEDA, Azure: AKS, Azure Database for PostgreSQL Flexible Server, Key Vault + Workload Identity,
  MongoDB (decidir e justificar: Cosmos DB for MongoDB vCore × MongoDB Atlas × operador no cluster).

## Estrutura de pastas (obrigatória)
```
creditflow/
  CreditFlow.slnx
  Directory.Build.props  Directory.Packages.props   (Central Package Management)
  src/
    Host/CreditFlow.Host/                 Program.cs (carrega módulos por perfil), appsettings*.json
    BuildingBlocks/CreditFlow.BuildingBlocks/   Result, Erro, ValueObjects (Dinheiro, Cpf, Percentual), IModule,
                                                eventos de integração (IEventoIntegracao), Outbox, Auth helpers
    Modules/<Modulo>/CreditFlow.Modules.<Modulo>/            Domain/ Application/ Infrastructure/ Api/ <Modulo>Module.cs
    Modules/<Modulo>/CreditFlow.Modules.<Modulo>.Contracts/  só o que outros módulos podem ver (interfaces, DTOs, eventos)
  tests/ CreditFlow.ArchitectureTests/  CreditFlow.Modules.<Modulo>.Tests/  CreditFlow.IntegrationTests/
  deploy/
    local/docker-compose.yml  (postgres, mongo, dragonfly, kafka KRaft, kafka-ui, keycloak + realm import)
    local/keycloak/creditflow-realm.json
    docker/Dockerfile
    helm/creditflow/          um chart, N deployments (um por perfil)
    k8s/                      manifests de plataforma (Strimzi Kafka, Dragonfly, KEDA ScaledObject, etc.)
  infra/terraform/  00-base  10-azure (rede, AKS, Postgres, Key Vault, identidade)  20-plataforma (Harbor, Argo CD, Strimzi, Keycloak, Dragonfly operator)  30-app
  gitops/            apps do Argo CD (app-of-apps), values por ambiente (dev, prod)
  load/              testes de carga k6
  docs/adr/          ADRs
  .github/workflows/ CI (build, teste, imagem, scan, push no Harbor, bump de tag no gitops)
```

## Módulos de negócio (namespace CreditFlow.Modules.X, schema Postgres = nome minúsculo)
| Módulo | Responsabilidade | Dados |
|---|---|---|
| **Clientes** | cadastro (CPF, renda, score fictício), consentimento LGPD | Postgres `clientes` |
| **Simulacao** | simular empréstimo: Price e SAC, juros mensais, IOF (diário + adicional), CET, tabela de parcelas; CPU-bound; sem login obrigatório (público, com rate limit) | MongoDB coleção `simulacoes` (histórico, documento com a tabela inteira); taxas em cache Dragonfly |
| **Propostas** | proposta a partir de uma simulação: análise (regras), aprovação/recusa, contratação | Postgres `propostas` + outbox |
| **Pagamentos** | desembolso (PIX fictício via gateway simulado) e baixa de parcelas; idempotente; consumidor Kafka | Postgres `pagamentos` + outbox/inbox |
| **Notificacoes** | consome eventos e registra/enviaria e-mail/SMS | MongoDB `notificacoes` |

Comunicação: síncrona só via `*.Contracts` (interfaces in-process); assíncrona por eventos de integração no Kafka
(outbox no Postgres). Tópicos: `creditflow.propostas.v1`, `creditflow.pagamentos.v1`, `creditflow.simulacoes.v1`;
chave = id do agregado. Eventos: `SimulacaoRealizada`, `PropostaCriada`, `PropostaAprovada`, `PropostaRecusada`,
`ContratoAssinado`, `DesembolsoSolicitado`, `DesembolsoConcluido`, `DesembolsoFalhou`, `ParcelaPaga`.
Saga: ContratoAssinado → Pagamentos desembolsa → DesembolsoConcluido/Falhou → Propostas atualiza.

## Perfis de execução (como escalar módulos sem virar microsserviços)
A MESMA imagem sobe com `CREDITFLOW__MODULOS` (lista) e `CREDITFLOW__PAPEL` (`api` | `worker`):
- `api-geral`: Clientes, Propostas, Notificacoes (HTTP)
- `api-simulacao`: só Simulacao (HTTP) → escala por CPU (HPA)
- `worker-pagamentos`: só Pagamentos consumindo Kafka → escala por lag (KEDA)
- `api-pagamentos`: endpoints de consulta de Pagamentos
Ingress por path: /v1/simulacoes → api-simulacao; /v1/pagamentos → api-pagamentos; resto → api-geral.
Host: `IModule { string Nome; void Registrar(IServiceCollection, IConfiguration); void Mapear(IEndpointRouteBuilder); }`,
módulos descobertos por lista explícita em Program.cs e filtrados pela config.

## Auth (Keycloak)
Realm `creditflow`. Clients: `creditflow-web` (público, Authorization Code + PKCE), `creditflow-api` (bearer-only/audience),
`creditflow-backoffice` (confidencial, client credentials M2M). Roles de realm: `cliente`, `analista`, `operador-financeiro`, `admin`.
API valida JWT (JwtBearer, Authority = realm, Audience = creditflow-api), mapeia `realm_access.roles` para claims de role,
policies por caso de uso. Ports locais: API 8080, Keycloak 8081, Kafka 9092, Kafka UI 8085, Postgres 5432, Mongo 27017, Dragonfly 6379.

## Configuração (chaves)
ConnectionStrings: Postgres, Mongo, Cache (Dragonfly), Kafka (BootstrapServers em `Kafka:BootstrapServers`).
`Auth:Authority`, `Auth:Audience`. `CreditFlow:Modulos`, `CreditFlow:Papel`.

## Regras de domínio de crédito (Simulação)
Valor 1.000 a 50.000 BRL; prazo 6 a 48 meses; taxa mensal por faixa de score (tabela em config/cache);
IOF: 0,0082% ao dia (limitado a 365 dias) + 0,38% adicional sobre o principal; CET mensal e anual calculados por IRR
(Newton-Raphson) sobre o fluxo líquido. `decimal`, arredondamento bancário só na apresentação, parcelas em centavos
com ajuste na última. Diga no texto: valores e fórmulas didáticos, confira a regulação atual (Bacen/Receita).

## Curso (learnFlow) — formato
Lições em /home/claude/learnflow/projetos/creditflow/docs/modulo-NN/*.md, cada módulo com README.md (objetivo,
lições, o que você constrói, pré-requisitos), lições numeradas (NN.1 …), perguntas-de-entrevista.md e um desafio.
Use EXATAMENTE os blocos de ensino do curso Plataforma .NET (leia /home/claude/learnflow/projetos/plataforma-dotnet/docs
para o estilo e a sintaxe `::: tipo título` … `:::`, com `???` separando o que fica escondido): conceito, analogia,
porque, video (YouTube real, confira a URL), previsao, armadilha, quebra, checkpoint, desafio, solucao, dica,
pergunta, entrevista. Diagramas em ```mermaid (C4/flowchart/sequence/state). Tom de professor: o que é, por que este
e não aquele, como funciona por dentro, o que quebra, como diagnosticar. pt-BR, direto, sem enrolação.
Quando um conceito já foi ensinado a fundo no curso Plataforma .NET, explique o essencial e aponte a lição de lá
com link relativo `../../plataforma-dotnet/docs/modulo-0X/arquivo.md`.
Links para o código: `https://github.com/JandersonSR/lending-plataform/blob/main/<caminho>`.
Tags do git por módulo: `modulo-01` … (o coordenador cria).

## Mapa do curso (o coordenador escreve docs/README.md com a tabela)
0 Visão geral e como estudar · 1 Do zero: solução, monolito modular e ambiente local · 2 Domínio de crédito: módulo Simulação
· 3 Identidade com Keycloak · 4 Persistência poliglota: PostgreSQL e MongoDB · 5 Cache com Dragonfly
· 6 Kafka: outbox, saga de desembolso e consumidor idempotente · 7 Contêiner, Kubernetes e Helm
· 8 Escalando Simulação e Pagamentos (perfis, HPA, KEDA, k6) · 9 Azure com Terraform · 10 Harbor e cadeia de suprimentos
· 11 GitOps com Argo CD · 12 Operação: OpenTelemetry, SLOs, game day, ADRs

## Program.cs (dono: autor dos módulos 0–2) chama exatamente estes ganchos, implementados por outros autores
- `builder.AddObservabilidade();` → BuildingBlocks/CreditFlow.BuildingBlocks/Observabilidade/ (autor dos módulos 7–12)
- `builder.Services.AddAutenticacaoCreditFlow(builder.Configuration);` e `app.UseAutenticacaoCreditFlow();` → BuildingBlocks/.../Auth/ (autor 3–5)
- `builder.Services.AddMensageria(builder.Configuration);` → BuildingBlocks/.../Mensageria/ (autor 6 e 8)
Cada autor só cria/edita arquivos da sua área (lista no seu pedido). Se precisar de mudança na área de outro, escreva
em /home/claude/creditflow/PEDIDOS.md (quem, arquivo, o quê) e siga; o coordenador resolve.
