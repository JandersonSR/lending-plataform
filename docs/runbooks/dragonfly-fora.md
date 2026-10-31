# Runbook: Dragonfly (cache) fora

**Alerta:** `DragonflySemPrimario`; sintoma: latência da simulação subindo, CPU do Mongo/consulta de taxas subindo.
**Impacto esperado:** degradação, não queda. O HybridCache tem a camada L1 em memória em cada pod e cai para a fonte quando o L2 (Dragonfly) falha; a API continua respondendo, mais devagar.

## 1. Confirmar
```bash
kubectl -n creditflow-dados get dragonfly creditflow-cache -o jsonpath='{.status.phase}'; echo
kubectl -n creditflow-dados get pods -l app=creditflow-cache
kubectl -n creditflow-dados logs -l app=creditflow-cache --tail=50
```
- `OOMKilled`: o `--maxmemory` precisa ficar ABAIXO do limite de memória do pod (o processo usa mais que os dados). Ajuste no Git (`gitops/ambientes/<amb>/dados.yaml`).
- Primário caiu e réplica não assumiu: o operador promove em segundos; se não, `kubectl -n dragonfly-operator-system logs deploy/dragonfly-operator`.

## 2. A aplicação está degradando bem?
```bash
kubectl -n creditflow logs deploy/creditflow-api-simulacao --since=5m | grep -i "redis\|cache" | tail
```
Se a API estiver **caindo** (5xx) em vez de ficar lenta, é bug: o acesso ao cache deveria ter timeout curto (`syncTimeout=1000`, `connectTimeout=2000` na connection string) e falha aberta. Abra incidente de código.

## 3. Voltar
O cache volta vazio (sem snapshot, de propósito). Espere o pico de leitura na fonte enquanto ele aquece; o HybridCache evita a debandada (uma busca por chave por pod).
