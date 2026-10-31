# Runbook: SLO do desembolso queimando

**Alertas:** `DesembolsoLentoQueima{Rapida,Lenta}`
**SLO:** 99% dos desembolsos concluídos em até 5 minutos do `ContratoAssinado` (falhas contam como ruins).

A jornada: Propostas grava `ContratoAssinado` na outbox → relay publica em `creditflow.propostas.v1` → worker-pagamentos consome, chama o gateway PIX, grava `DesembolsoConcluido`/`Falhou` na outbox → Propostas consome de `creditflow.pagamentos.v1`.

Procure o elo lento:
1. **Outbox parada?** `creditflow_mensageria_outbox_publicadas_total` zerado no api-geral; falhas em `..._outbox_falhas_total`. Kafka fora ou ACL do `creditflow-geral`.
2. **Lag no worker?** `kafka-lag.md`.
3. **Gateway PIX lento/falhando?** `creditflow_desembolsos_total{creditflow_resultado="falhou"}` por `creditflow_motivo`. Falha técnica (timeout) x de negócio (chave inválida): só a técnica é incidente.
4. **Trace de ponta a ponta:** o `traceparent` viaja no cabeçalho da mensagem; no Tempo, busque por `creditflow.proposta.id` e veja onde o tempo foi gasto.
