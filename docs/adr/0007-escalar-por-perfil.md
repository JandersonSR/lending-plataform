# ADR 0007: Escalar por perfil de carga (HPA por CPU, KEDA por lag), sem extrair serviços

- **Status:** aceita
- **Data:** 2026-10-30
- **Contexto do curso:** módulo 8
- **Relacionada:** ADR 0002 (perfis de execução)

## Contexto

Dois módulos têm cargas que não combinam com o resto:

- **Simulação**: pública, sem login, picos de 10 a 20 vezes em segundos (campanhas). **CPU-bound**
  (Price/SAC, IOF, CET por Newton-Raphson). Uma rajada não pode degradar Propostas e Clientes.
- **Pagamentos (worker)**: consome `creditflow.propostas.v1` e espera o gateway PIX e o Postgres.
  **I/O-bound**: com a fila crescendo, a CPU continua baixa.

## Decisão

A mesma imagem, quatro Deployments (ADR 0002), e cada um escala pelo **sinal que mede a sua fila**:

| Perfil | Sinal | Objeto | Faixa |
|---|---|---|---|
| api-simulacao | CPU média / request | HPA `autoscaling/v2`, alvo 60% | 2 a 12 |
| worker-pagamentos | lag do grupo `creditflow-pagamentos` | KEDA `ScaledObject` (kafka), `lagThreshold` 50 | 1 a 6 (= partições) |
| api-geral | CPU | HPA, alvo 70% | 2 a 6 |
| api-pagamentos | — | fixo | 2 |

Mais: PodDisruptionBudget por perfil (`minAvailable: 1` nas APIs, `maxUnavailable: 1` no worker) e
`topologySpreadConstraints` por zona e por nó com `ScheduleAnyway`. Ingress (Gateway API) por path:
`/v1/simulacoes` → api-simulacao; `/v1/pagamentos` → api-pagamentos; resto → api-geral.

A fonte de verdade é o chart (`deploy/helm/creditflow`); `deploy/k8s/escala/` é a mesma configuração
desenrolada para estudo e para o lab no kind.

## Alternativas descartadas

- **Extrair Simulação e Pagamentos para microsserviços agora**: o problema é de escala e isolamento
  de carga, e perfis resolvem isso sem rede entre módulos, sem contratos HTTP versionados entre
  times, sem pipelines e bancos novos. Critérios para extrair (time próprio, ciclo de deploy
  próprio, stack diferente, dados que precisam sair do Postgres compartilhado, limites de
  segurança/compliance) estão na lição 8.6 e não são atendidos hoje.
- **HPA por CPU no worker**: não reage (CPU baixa com lag alto).
- **KEDA com `minReplicaCount: 0` no worker**: economiza um pod, mas o primeiro desembolso depois de
  um período ocioso esperaria polling + subida do pod (dezenas de segundos). Dinheiro do cliente
  vale mais que um pod de 100m.
- **Mais réplicas do worker que partições** (`allowIdleConsumers`): réplicas excedentes ficam paradas.
  Para passar de 6, aumenta-se partições (decisão de capacidade, com cuidado: muda o mapeamento
  chave → partição para mensagens novas).

## Consequências

- Uma rajada de simulações escala só a api-simulacao; Propostas e Pagamentos não sentem.
- O teto do worker é o número de partições: 6. Aumentar exige planejar (ordem por chave na transição).
- Cada pod novo abre um pool de conexões: 12 × pool da Simulação contra o Mongo/Dragonfly, 6 × pool do
  worker contra o Postgres. Conferir limites do Flexible Server antes de subir máximos.
- Escala automática leva de 30 s (pods) a 5 min (nós): o mínimo do HPA é a folga do pico típico.
- Teste de carga (`load/simulacao-rajada.js`, `load/fluxo-completo.js`) com limites que reprovam.
