# Runbook: SLO da simulação queimando

**Alertas:** `SimulacaoErrosQueima{Rapida,Lenta}`, `SimulacaoLentaQueima{Rapida,Lenta}`
**SLOs:** 99,5% das requisições a `/v1/simulacoes` sem 5xx; 99% abaixo de 250 ms (30 dias).

1. **Começou com um deploy?** Argo CD → histórico da `creditflow-<amb>`. Se sim: `deploy-e-rollback.md`, passo 1.
2. **5xx ou lentidão?** Painel "Simulação": status por código e p99.
3. **Lentidão com CPU no talo e réplicas no máximo do HPA:** é carga. Suba `maximo` do HPA no Git; confira se o pool de nós tem espaço (pods `Pending` → autoescalador de nós). Rate limit da rota pública está segurando abuso? (picos de um IP só).
4. **Lentidão com CPU baixa:** espera por dependência. Tempo: spans lentos de `MongoDB.Driver` (gravação do histórico) ou do cache. Dragonfly fora → `dragonfly-fora.md`.
5. **5xx:** logs com `trace_id` no Loki (`{service_name="creditflow-api-simulacao"} |= "Exception"`), abra o trace no Tempo.
6. **Throttling de CPU:** o perfil não tem limite de CPU de propósito. Se alguém colocou, `container_cpu_cfs_throttled_periods_total` sobe e a latência de cauda explode.
