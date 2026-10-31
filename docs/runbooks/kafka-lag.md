# Runbook: lag do Kafka (desembolsos atrasando)

**Alertas:** `KafkaLagPagamentosAlto`, `KafkaLagPagamentosCrescendo`, `MensagensNaDlt`, `DesembolsoLento*`
**Impacto:** contratos assinados esperam o desembolso. Nada se perde (o evento está no tópico), mas o SLO de 5 minutos queima.

## 1. Confirmar e medir (2 min)

```bash
kubectl -n creditflow-dados exec creditflow-brokers-0 -- /opt/kafka/bin/kafka-consumer-groups.sh \
  --bootstrap-server localhost:9092 --describe --group creditflow-pagamentos
```
(Em dev o pool se chama `unico`: `creditflow-unico-0`.) Olhe `LAG` por partição e `CONSUMER-ID`:
- **partições sem consumidor** (`-`): o grupo está rebalanceando ou os pods caíram → passo 2;
- **lag em UMA partição só**: mensagem envenenada ou chave quente → passo 4;
- **lag em todas, crescendo**: o worker não dá conta ou está travado numa dependência → passo 3.

## 2. Os pods do worker estão vivos?

```bash
kubectl -n creditflow get deploy creditflow-worker-pagamentos
kubectl -n creditflow get scaledobject,hpa
kubectl -n creditflow describe scaledobject creditflow-worker-pagamentos | tail -20
kubectl -n creditflow logs deploy/creditflow-worker-pagamentos --since=10m | grep -iE "erro|exception|rebalance"
```
- ScaledObject com `READY False`: o KEDA não consegue ler o lag (senha do `creditflow-keda`? ACL?). Veja `kubectl -n keda logs deploy/keda-operator`.
- Réplicas = máximo (6) e lag crescendo: o limite é o número de **partições** (6). Mais pods não ajudam; o gargalo está no processamento (passo 3).
- `CrashLoopBackOff`/`OOMKilled`: `kubectl describe pod` e o runbook `deploy-e-rollback.md`.

## 3. O worker está esperando alguém?

No Grafana, painel "CreditFlow: SLOs e jornadas", e no Tempo: spans `consumir creditflow.propostas.v1` lentos? O filho lento diz quem é: Postgres (lock? conexões esgotadas?), gateway PIX simulado (timeout?).
- Postgres: `SELECT pid, wait_event_type, state, now()-query_start, query FROM pg_stat_activity WHERE datname='creditflow' ORDER BY 4 DESC;`
- Dependência externa fora: o consumidor tenta de novo sem avançar o offset (comportamento esperado). Estanque a origem; o lag drena sozinho quando ela volta.

## 4. Uma partição parada

Uma mensagem que sempre falha deveria ir para a DLT (`creditflow-pagamentos.dlt`). Se o lag parou numa partição só e não há DLT:
```bash
kubectl -n creditflow logs deploy/creditflow-worker-pagamentos --since=30m | grep -i "partição\|offset"
```
Não pule offset na mão sem registrar: anote tópico/partição/offset no incidente, corrija o código (classificação de erro permanente) e só então, se precisar, avance o offset com `--reset-offsets --to-offset N --execute` com o grupo **parado** (KEDA em `minReplicaCount: 0` temporário pelo Git).

## 5. Encerrar
Lag de volta a ~0, `DesembolsoLento` resolvido, post-mortem se o budget queimou mais de 10%.
