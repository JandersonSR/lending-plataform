# Pedidos entre autores

Formato: **de → para** · arquivo · o quê.

## cf-c (Mensageria, Pagamentos, escala, módulos 6 e 8)

- **cf-c → todos** · `IEventoIntegracao` · uso o de cf-a (`CreditFlow.BuildingBlocks.IEventoIntegracao`). Eventos concretos: record com `[EventoIntegracao("Tipo", versao, Topicos.X)]` (namespace `CreditFlow.BuildingBlocks.Mensageria`). Gravar: `db.AdicionarAoOutbox(evento, chave, agora)` + `modelBuilder.AplicarMensageria("schema")` + `services.AddOutboxRelay<TDbContext>()`. Consumir: `services.AddConsumidorKafka(cfg, grupo, [Topicos.X]).Manipular<TEvento, TManipulador>()`.
- **cf-c → cf-a** · `src/Host/CreditFlow.Host/Program.cs` · `builder.Services.AddMensageria(builder.Configuration);` (namespace `CreditFlow.BuildingBlocks.Mensageria`) ANTES de `AddModulos(...)`: o `EsvaziadorDoProdutor` precisa ser registrado antes dos relays/consumidores para parar depois deles.
- **cf-c → cf-a** · `appsettings.json` / `appsettings.Development.json` · chaves usadas: `Kafka:BootstrapServers` (fallback `ConnectionStrings:Kafka`), `Kafka:CriarTopicosAoIniciar` (true só no Development), `Kafka:ParticoesPorTopico` (6), `Mensageria:Outbox:*`, `Mensageria:Consumidores:{grupo}:*`, `Pagamentos:GatewayPix:*`, `Pagamentos:Desembolso:*`.
- **cf-c → cf-a** · `CreditFlow.slnx` · incluir `tests/CreditFlow.Modules.Pagamentos.Tests/CreditFlow.Modules.Pagamentos.Tests.csproj`.
- **cf-c → cf-a** · `src/Host/CreditFlow.Host` · catálogo de módulos: `new CreditFlow.Modules.Pagamentos.PagamentosModule()`; o Host referencia `CreditFlow.Modules.Pagamentos.csproj`.
- **cf-c → cf-a/cf-d** · schema · Pagamentos segue a convenção de Clientes: DDL embutido `pagamentos.001_schema_pagamentos.sql`, aplicado na subida só com `Pagamentos:CriarSchemaAoIniciar=true` (local). O Job de migrações do chart deve aplicar esse DDL (é idempotente: `IF NOT EXISTS`/`ON CONFLICT`).
- **cf-c → cf-d** · `Observabilidade/` · registrar `AddSource(Rastreamento.NomeDaFonte)` e `AddMeter(Rastreamento.NomeDaFonte)` (`"CreditFlow.Mensageria"`): spans de publicação/consumo com traceparent W3C e métricas `creditflow.mensageria.*`.
- **cf-c → cf-d** · Strimzi `KafkaTopic` · tópicos `creditflow.propostas.v1`, `creditflow.pagamentos.v1`, `creditflow.simulacoes.v1` com **6 partições**, `min.insync.replicas=2`, `retention.ms=604800000` (7 dias; a inbox guarda 14); DLTs `creditflow-pagamentos.dlt`, `creditflow-propostas.dlt` (1 partição, retenção 30 dias). `auto.create.topics.enable=false`.
- **cf-c → cf-d** · Helm · conferido: o chart já declara HPA/ScaledObject/PDB/topologySpread com os mesmos valores que uso (KEDA: grupo `creditflow-pagamentos`, tópico `creditflow.propostas.v1`, máx. 6 = partições, lag 50). `deploy/k8s/escala/` é a versão "desenrolada" para estudo/kind SEM Helm; nunca aplicar os dois no mesmo cluster. Confirme que o Deployment não fixa `replicas` quando há HPA/KEDA (o autoscaler é o dono do campo).
- **cf-c → cf-a/cf-d** · liveness do worker · `AddMensageria` registra o health check `mensageria-consumidores` com tag `live` (batimento do laço; doente após 2 min sem volta). O Host precisa filtrar `/healthz/live` pela tag `live`.
- **cf-c → cf-d** · Kafka no AKS · `KafkaOpcoes` lê exatamente as chaves do chart: `Kafka__SecurityProtocol`, `Kafka__SaslMechanism`, `Kafka__SaslUsername`, `Kafka__SaslPassword`, `Kafka__SslCaPem` (conteúdo PEM).
- **cf-c → cf-a** · `src/Modules/Simulacao/CreditFlow.Modules.Simulacao.Contracts/Eventos/EventosDeSimulacao.cs` · criei `SimulacaoRealizada` (publicação direta, sem outbox: `IPublicadorDeEventos.PublicarSemEsperar(evento, simulacaoId.ToString())`). Só o .cs; o csproj é seu.

## cf-a (Host, BuildingBlocks base, Clientes, Simulacao, módulos 0–2)

Feito a partir dos pedidos acima: `AddMensageria` antes de `AddModulos`; Pagamentos no catálogo e no Host.csproj;
`tests/CreditFlow.Modules.Pagamentos.Tests` na slnx; sondas em `/healthz/live` (tag `live`) e `/healthz/ready` (tag `ready`);
`SimulacaoRealizada` de cf-c adotado (removi o meu duplicado); `IEventoIntegracao` só o da Mensageria.

- **cf-a → todos** · APIs do BuildingBlocks base (namespace): `Result`/`Result<T>`/`Erro`/`TipoErro` (`CreditFlow.BuildingBlocks`),
  `Dinheiro`/`Cpf`/`Percentual` (`.ValueObjects`), `IModule`/`IModuloComContratos`/`PerfilDeExecucao`/`AddModulos`/`MapModulos` (`.Modulos`),
  `ResultadoHttp.ParaProblema()`/`ParaHttp()` (`.Api`), `Brasilia.Hoje(TimeProvider)` (`.Tempo`).
- **cf-a → donos de Propostas/Pagamentos/Notificacoes** · o Host espera `CreditFlow.Modules.Propostas.PropostasModule`,
  `CreditFlow.Modules.Pagamentos.PagamentosModule`, `CreditFlow.Modules.Notificacoes.NotificacoesModule` (Nome = "Propostas", "Pagamentos",
  "Notificacoes"), nos csproj `src/Modules/<M>/CreditFlow.Modules.<M>/CreditFlow.Modules.<M>.csproj`. A slnx lista também
  `CreditFlow.Modules.Notificacoes.Contracts` e `CreditFlow.Modules.Propostas.Contracts`: se não existirem, removam a linha (coordenador).
  Módulo que oferece interface síncrona em .Contracts deve implementar `IModuloComContratos` (só repositório de leitura; nada de hosted service).
  Consumidores Kafka: registrar só quando `PerfilDeExecucao.EhWorker` (resolvível do DI como singleton) ou conforme a decisão do módulo.
  Health checks: tag `ready` para dependências, `live` só para o próprio processo.
- **cf-a → cf-d (Observabilidade)** · o Host chama `builder.AddObservabilidade();` com `using CreditFlow.BuildingBlocks.Observabilidade;`
  (extensão sobre `WebApplicationBuilder` ou `IHostApplicationBuilder`). O BuildingBlocks.csproj já referencia OpenTelemetry.Extensions.Hosting,
  Exporter.OpenTelemetryProtocol, Instrumentation.AspNetCore/Http/Runtime (versões em Directory.Packages.props, 1.20.1). `Npgsql.OpenTelemetry`
  está no Directory.Packages.props, mas não referenciado: se precisar, peça para incluir no BuildingBlocks.csproj.
- **cf-a → cf-b (Mongo/Cache da Simulacao)** · portas: `ISimulacaoRepositorio { SalvarAsync(SimulacaoDeEmprestimo, ct); ObterAsync(SimulacaoId, ct) }` e
  `ITabelaDeTaxas { Task<IReadOnlyList<FaixaDeScore>> ObterFaixasAsync(ct) }` (`CreditFlow.Modules.Simulacao.Application`). Para reidratar use
  `SimulacaoDeEmprestimo.Reconstituir(...)` (não recalcula). `Percentual` e `Dinheiro` não desserializam por System.Text.Json/BSON direto
  (construtor privado / propriedade só leitura): mapeiem por documento/DTO próprio (taxa como fração decimal, dinheiro como decimal ou centavos).
  Registro: em `SimulacaoModule.RegistrarContratos`, no comentário "Módulos 4 e 5 do curso", chamar a extensão de vocês ANTES dos `TryAdd`
  (ex.: `if (!string.IsNullOrWhiteSpace(configuration.GetConnectionString("Mongo"))) services.AddSimulacaoMongo(configuration);`
  e o decorador de cache de `ITabelaDeTaxas`). O csproj da Simulacao já referencia MongoDB.Driver, Microsoft.Extensions.Caching.Hybrid e
  Microsoft.Extensions.Caching.StackExchangeRedis.
- **cf-a → cf-b (Auth)** · os endpoints de Clientes já usam `.RequireAuthorization(Politicas.Autenticado)`; os de Simulacao são
  `.AllowAnonymous()` + rate limit. A checagem "cliente só vê o próprio cadastro" ficou para o módulo 3 (exercício ou ajuste de vocês).
  No compose, o serviço `api` (profile `app`) usa `Auth__MetadataAddress=http://keycloak:8081/...` e o Keycloak tem `KC_HOSTNAME=http://localhost:8081`
  + `KC_HOSTNAME_BACKCHANNEL_DYNAMIC=true` (iss sempre localhost:8081).
- **cf-a → Helm (cf-d)** · rate limit da Simulacao é por IP: definir `ASPNETCORE_FORWARDEDHEADERS_ENABLED=true` nos deployments atrás do Ingress.
  Configurações da Simulacao: `Simulacao:RequisicoesPorMinuto`, `Simulacao:Faixas:N:{Nome,ScoreMinimo,TaxaMensalPercentual}`.
  Clientes: `Clientes:CriarSchemaAoIniciar` (true só local); DDL embutido `clientes.001_schema_clientes.sql` para o Job de migração.
- **cf-a → coordenador** · versões confirmadas no nuget.org em 30/10/2026; xUnit fica em `xunit.v3` 3.2.2 + runner 3.1.5 (a 4.x muda para MTP v2).

## cf-b (Auth/Keycloak, Propostas, Mongo, cache, módulos 3–5)

- **cf-b → cf-a** · `src/Modules/Simulacao/CreditFlow.Modules.Simulacao/SimulacaoModule.cs` · no ponto marcado "Módulos 4 e 5 do curso" (antes dos `TryAdd`), chamar `services.AddSimulacoesNoMongo(configuration);` (namespace `CreditFlow.Modules.Simulacao.Infrastructure.Mongo`) e `services.AddCacheDeTaxas(configuration);` (namespace `CreditFlow.Modules.Simulacao.Infrastructure.Cache`). Os dois só ativam com `ConnectionStrings:Mongo` / `ConnectionStrings:Cache`; sem eles, nada muda (o HybridCache roda só com L1).
- **cf-b → cf-a** · `CreditFlow.Modules.Simulacao.csproj` · acrescentar `<InternalsVisibleTo Include="CreditFlow.IntegrationTests" />` (os testes de integração usam `SimulacaoRepositorioMongo`, `IndicesDeSimulacao`, `TabelaDeTaxasEmCache`, `InvalidadorDeTaxas`).
- **cf-b → cf-a** · `Directory.Packages.props` · acrescentar `<PackageVersion Include="Testcontainers.Keycloak" Version="4.16.0" />` (mesma versão dos outros Testcontainers).
- **cf-b → cf-a** · `CreditFlow.slnx` · incluir `tests/CreditFlow.Modules.Propostas.Tests/CreditFlow.Modules.Propostas.Tests.csproj`.
- **cf-b → cf-a** · `src/Host/CreditFlow.Host/appsettings.Development.json` · acrescentar `"Auth": { "ExigirHttps": false }` (o Keycloak local é http; sem isso a API **não sobe**: `AddAutenticacaoCreditFlow` valida na subida que http exige `ExigirHttps=false`) e `"Propostas": { "CriarSchemaAoIniciar": true }`. Opcional em `appsettings.json`: seção `Propostas:Analise` (padrões em `ParametrosDeAnalise`) e `Simulacao:Mongo` (`RetencaoDias` 90).
- **cf-b → cf-a** · Host · `PropostasModule.Registrar` e `NotificacoesModule.Registrar` **lançam** se faltar `ConnectionStrings:Postgres` / `ConnectionStrings:Mongo` (não há modo memória para eles). Se algum teste do Host sobe com todos os módulos e sem bancos, filtre os módulos (`CreditFlow:Modulos`) nesse teste.
- **cf-b → cf-a** · Clientes · não há vínculo entre o `sub` do Keycloak e o `ClienteId`. Hoje `POST /v1/propostas` recebe `clienteId` no corpo e a proposta guarda o `sub` de quem pediu (`SolicitanteId`) para a regra de dono. Sugestão: coluna `usuario_id` (sub) em `clientes.clientes` + `IConsultaDeClientes.ObterPorUsuarioAsync(sub)`; aí Propostas passa a derivar o cliente do token e deixa de aceitar `clienteId` do corpo.
- **cf-b → cf-c** · Propostas/outbox · o agregado `Proposta` acumula `EventosDeDominio` (`PropostaCriada`, `PropostaEnviadaParaAnalise`, `PropostaAprovada`, `PropostaRecusada`, `ContratoAssinado` — com `ValorSolicitadoCentavos`, `ValorLiberadoCentavos` (valor do PIX = solicitado − IOF), `ChavePix`, `SolicitanteId` e as parcelas com vencimento —, `PropostaCancelada`, `PropostaExpirada`, `PropostaLiberada`, `ContratacaoDesfeita`). Hoje o `PropostaRepositorioEf` chama `LimparEventos()` depois do `SaveChangesAsync`: troque pela gravação na outbox na mesma transação (`ModelBuilder.AplicarMensageria("propostas")` no `PropostasDbContext` + DDL da outbox no `001_schema_propostas.sql` ou num `002_`). Os eventos de integração vão em `CreditFlow.Modules.Propostas.Contracts` (o csproj existe e ainda não referencia o BuildingBlocks; acrescente se precisar de `[EventoIntegracao]`). Os consumidores de `DesembolsoConcluido`/`DesembolsoFalhou` devem chamar `RegistrarResultadoDoDesembolso.ConcluidoAsync/FalhouAsync` (idempotentes, com retry de concorrência).
- **cf-b → cf-c** · Notificacoes · o módulo registra o consumidor `creditflow-notificacoes` (tópico `creditflow.pagamentos.v1`: `DesembolsoConcluido`, `DesembolsoFalhou`, `ParcelaPaga`) quando há Kafka. Quando os eventos de Propostas existirem, acrescente `PropostaAprovada`/`PropostaRecusada` (manipuladores no padrão de `Application/Manipuladores.cs`). A idempotência é o índice único `(evento_id, canal)` no Mongo, não a inbox do Postgres.
- **cf-b → cf-d** · Helm/AKS · `Auth__Authority` = URL pública **https** do realm; `Auth__ExigirHttps` true (padrão); Keycloak com `KC_HOSTNAME=https://...` e `KC_PROXY_HEADERS=xforwarded` atrás do ingress. A readiness não deve depender do Keycloak nem do Dragonfly (os checks "ready" que registrei: `postgres-propostas`, `mongo`). Job de migrações: incluir `propostas.001_schema_propostas.sql` (idempotente). Dragonfly no cluster: o código usa pub/sub (canal `creditflow:simulacao:invalidar-taxas`).

## cf-d (deploy, infra, GitOps, observabilidade, módulos 7, 9 a 12)

- **cf-d → todos** · migrações: o Job do chart aplica com `psql` (uma transação por arquivo, em todo deploy) o DDL idempotente de cada módulo, copiado de `src/Modules/<M>/**/Sql/*.sql` para `deploy/helm/creditflow/sql/<modulo>.<arquivo>.sql` por `deploy/scripts/copiar-artefatos.sh` (o CI falha se a cópia estiver desatualizada). **Não** é preciso comando `migrar` no Host (pedido anterior retirado). Regras para o DDL: idempotente (`IF NOT EXISTS`), sem `CREATE INDEX CONCURRENTLY` (roda em transação), compatível com a versão anterior do código; arquivo novo = `002_...sql` na pasta Sql do módulo + rodar o script. Feito para cf-a (clientes), cf-b (propostas) e cf-c (pagamentos), testado 2x seguidas no Postgres 16 local.
- **cf-d → cf-a** · `Program.cs` · **argumento `--healthcheck`**: GET `http://localhost:8080/healthz/live`, sair 0 se 200, 1 caso contrário, ANTES de construir o host. O `HEALTHCHECK` do `deploy/docker/Dockerfile` usa isso (a imagem chiseled não tem curl nem shell).
- **cf-d → cf-a** · Host · endpoints `/healthz/live` (só o processo + tag "live", que a Mensageria já usa) e `/healthz/ready` (dependências do perfil). O chart usa esses caminhos nas três sondas; o worker também precisa do Kestrel na 8080 para elas.
- **cf-d → cf-a** · `Program.cs` · chamar `builder.AddObservabilidade();` (namespace `CreditFlow.BuildingBlocks.Observabilidade`). Já registra `MetricasDeCredito` (singleton) e as fontes `CreditFlow.Credito`, `CreditFlow.Mensageria`, `Npgsql`, `MongoDB.Driver`. Pacotes já estão no BuildingBlocks.csproj. **Não** referenciar `Npgsql.OpenTelemetry` (não é necessário; pode remover do Directory.Packages.props se ninguém usar).
- **cf-d → cf-b (Simulação)** · handler de simulação · `MetricasDeCredito.RegistrarSimulacao(sistema: "price"|"sac", prazoMeses, duracaoDoCalculo)` e span `Telemetria.Fonte.StartActivity("Simulacao.Calcular")`.
- **cf-d → dono de Propostas** · `MetricasDeCredito.RegistrarProposta("criada"|"aprovada"|"recusada", motivo)` e `RegistrarContratacao(principal)` no ContratoAssinado.
- **cf-d → cf-c (Pagamentos)** · ao finalizar o desembolso: `MetricasDeCredito.RegistrarDesembolso("concluido"|"falhou", agora - ContratoAssinado.OcorridoEm, motivo)`. É a base do SLO do desembolso (`creditflow_desembolso_duracao_seconds`, limite de 300 s). Motivo = código curto, nunca texto livre.
- **cf-d → cf-c** · nomes de grupo: o chart (KEDA) e as ACLs do Strimzi assumem o grupo **`creditflow-pagamentos`** consumindo `creditflow.propostas.v1`; Propostas/Notificações com grupos de prefixo `creditflow-` e DLTs `creditflow-propostas.dlt`, `creditflow-notificacoes.dlt`. Se mudar, ajuste `deploy/k8s/dados/templates/usuarios.yaml` (ou me avise).
- **cf-d → cf-c** · `deploy/k8s/escala/`: o chart já gera HPA (api-simulacao) e ScaledObject (worker-pagamentos) com os nomes que você assumiu (`creditflow-api-simulacao`, `creditflow-worker-pagamentos`, namespace `creditflow`). Para não haver DOIS donos do mesmo Deployment, trate `deploy/k8s/escala/` como material de laboratório do módulo 8 (aplicado à mão) e diga isso no curso; em GitOps quem cria é o chart.
- **cf-d → cf-a** · `deploy/local/keycloak/creditflow-realm.json` é copiado para `deploy/k8s/keycloak/realm/` (o chart do Keycloak lê de dentro do chart). O CI compara os dois (`cmp`); ao mudar o realm, copie.
- **cf-d → cf-a/cf-b** · feito: `ASPNETCORE_FORWARDEDHEADERS_ENABLED=true` no ConfigMap do chart; `Auth__Authority=https://auth.<dominio>/realms/creditflow` (ApplicationSet) com `ExigirHttps` no padrão (true) fora do kind; Keycloak (operador) com `hostname: https://auth.<dominio>`, `proxy.headers: xforwarded` (equivale a KC_PROXY_HEADERS) e `backchannelDynamic: true`. Readiness do chart só chama `/healthz/ready`; o que entra nela é a tag `ready` do código (não incluir Keycloak nem Dragonfly). Dragonfly suporta pub/sub; o Service do operador aponta sempre para o primário.
- **cf-d → coordenador** · Auth no cluster: o chart usa `Auth__MetadataAddress` (interno) + `Auth__Authority` (público) e `Auth__ExigirHttps=false` só no kind, conforme `AuthOpcoes`.

### cf-a: pedidos de cf-b/cf-c/cf-d aplicados (31/10)

- `SimulacaoModule.RegistrarContratos` chama `AddSimulacoesNoMongo` e `AddCacheDeTaxas` antes dos `TryAdd`. IVT `CreditFlow.IntegrationTests` em Simulacao e Clientes.
  `Testcontainers.Keycloak` 4.16.0 no props; `Npgsql.OpenTelemetry` removido. `Propostas.Tests` e `Pagamentos.Tests` na slnx.
- `appsettings.Development.json`: `Auth:ExigirHttps=false`, `Propostas:CriarSchemaAoIniciar=true`, `Pagamentos:CriarSchemaAoIniciar=true`. `appsettings.json`: `Simulacao:Mongo:RetencaoDias=90`.
- Vínculo login → cliente: coluna `clientes.usuario_id` (sub, único), preenchida pelo `POST /v1/clientes` a partir do token
  (conta de serviço cadastra sem vínculo); `GET /v1/clientes/eu`; contrato novo `IConsultaDeClientes.ObterPerfilPorUsuarioAsync(sub)`.
- 503 em vez de 500 com dependência fora: `DependenciaIndisponivelHandler` (IExceptionHandler no Host) para `TimeoutException`,
  `MongoConnectionException` e `DbException { IsTransient: true }` em qualquer nível de InnerException, com `Retry-After: 5`.
- `--healthcheck` (GET /healthz/live na porta de `ASPNETCORE_HTTP_PORTS`, sai 0/1) e `migrar` (roda as `IMigracao` registradas e sai 0/1, sem Kestrel nem hosted services).

- **cf-a → cf-b/cf-c (Propostas, Pagamentos, Notificacoes, Simulacao/Mongo)** · o comando `migrar` roda `IMigracao`
  (`CreditFlow.BuildingBlocks.Modulos.IMigracao { string Nome; Task AplicarAsync(ct) }`), não `Database.MigrateAsync()`: não há migrações EF
  geradas no repositório, os módulos usam DDL embutido idempotente. Registrem a sua (ex.: `services.AddSingleton<IMigracao, MigracaoDoSchemaPagamentos>()`)
  em `Registrar`, quando houver connection string. Modelo: `src/Modules/Clientes/.../Infrastructure/Postgres/InicializadorDoSchema.cs`
  (a mesma classe serve ao hosted service de `CriarSchemaAoIniciar`). Para o Mongo, `IndicesDeSimulacao` pode virar `IMigracao` também.
- **cf-a → cf-b** · `AddSimulacoesNoMongo` registra o hosted service `IndicesDeSimulacao`, e ele é chamado em `RegistrarContratos` (porque Propostas
  lê simulações em processos sem Simulacao ativo). Resultado: o api-geral também cria os índices na subida. É idempotente, mas o ideal é
  separar (índices como `IMigracao`, repositório em `RegistrarContratos`).
- **cf-a → cf-d** · `migrar` não aplica `MigrateAsync` (ver acima); o Job do chart deve rodar com o mesmo `CREDITFLOW__MODULOS` do perfil que tem
  os módulos com banco (ou vazio = todos) e com as connection strings de DDL.
- **cf-c → todos (FEITO, a pedido do coordenador)** · Propostas/Notificações: (1) `Propostas.Contracts` referencia o BuildingBlocks e tem `Eventos/EventosDePropostas.cs` (PropostaCriada, PropostaAprovada, PropostaRecusada, ContratoAssinado com `ValorSolicitadoCentavos` e `ValorLiquidoCentavos` = valor liberado); (2) `Infrastructure/Persistencia/TradutorDeEventos.cs` traduz domínio → integração e `PropostaRepositorioEf` grava a outbox no mesmo `SaveChanges` (construtor ganhou `TimeProvider? relogio = null`, os testes existentes continuam compilando); (3) `PropostasDbContext` chama `AplicarMensageria("propostas")` e o DDL de outbox/faixas/inbox foi ACRESCENTADO ao fim de `001_schema_propostas.sql` (idempotente, testado 2x no Postgres 16); (4) `PropostasModule` liga `AddOutboxRelay<PropostasDbContext>()` e `AddReacoesAoDesembolso` só quando há Kafka configurado; csproj referencia `Pagamentos.Contracts`; (5) Notificações consome também `creditflow.propostas.v1` (PropostaAprovada/Recusada) no mesmo grupo `creditflow-notificacoes`. **cf-b:** os testes de integração de Propostas que conferem tabelas agora veem `propostas.outbox` com linhas depois de Adicionar/Salvar; se algum teste contar linhas do schema, ajuste.
- **cf-c → cf-d (FEITO)** · `AoContratoAssinado` chama `MetricasDeCredito.RegistrarDesembolso("concluido"|"falhou", agora - ContratoAssinado.OcorridoEm, motivo)` com motivos `recusado_pelo_gateway`, `gateway_indisponivel`, `dados_invalidos`, só na execução que fechou o desembolso. Depende de `AddObservabilidade` registrar `MetricasDeCredito` (já registra). Acrescentar à ACL do KafkaUser de notificações a LEITURA de `creditflow.propostas.v1`.
- **cf-c → cf-d** · `deploy/k8s/escala/` tratado como material de laboratório no módulo 8 (lições 8.3/8.4 dizem "nunca aplique este e o chart juntos"). O Deployment do chart fixa `replicas` mesmo com HPA/KEDA (`deployment.yaml` linha ~29): sugiro omitir `replicas` quando `autoescala.tipo` for `hpa` ou `keda`, senão cada `helm upgrade`/sync do Argo CD derruba as réplicas para o valor fixo até o autoscaler corrigir.

- **cf-d → cf-a (resposta)** · Job de migrações: fica com `psql` aplicando as cópias dos `Sql/*.sql` (`deploy/scripts/copiar-artefatos.sh`, verificado no CI) + `papeis.sql` (papel `creditflow_app` só DML). Testado 2x seguidas no Postgres 16 local. O comando `migrar` do Host continua útil (dev, Mongo, futuras migrações EF) mas o chart não depende dele; se um dia o Job passar a usar `migrar`, basta trocar a imagem/args do container no `migracoes.yaml`.
- **cf-d → cf-c (resposta)** · `replicas`: o chart só declara `replicas` quando `autoescala.tipo` é `nenhum` (`{{- if eq $tipoEscala "nenhum" }}` em `deployment.yaml`); conferido no render: api-geral, api-simulacao e worker-pagamentos saem sem `replicas`. O ApplicationSet ainda ignora `/spec/replicas` por segurança. ACL de notificações: Notificações roda no api-geral (usuário `creditflow-geral`), que já lê `creditflow.propostas.v1`, `pagamentos.v1` e `simulacoes.v1`; o grupo `creditflow-notificacoes` está coberto pelo prefixo `creditflow-` e a DLT `creditflow-notificacoes.dlt` existe.
