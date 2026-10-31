# Runbook: migração travada ou falhando (deploy parado)

**Alertas:** `MigracaoDemorando`, `MigracaoFalhou`; no Argo CD: `creditflow-<amb>` em `Syncing` (PreSync) ou `SyncFailed`.
**Impacto:** a versão nova não sobe; **a versão antiga continua rodando** (o Job roda antes de mexer nos Deployments). Ninguém está fora por causa disso, a menos que alguém "force" o deploy.

## 1. Onde está
```bash
kubectl -n creditflow get jobs -l app.kubernetes.io/component=migracoes
kubectl -n creditflow logs job/creditflow-migracoes   # mostra ">> arquivo.sql" antes de cada arquivo
```

## 2. Esperando lock?
O Job roda o psql com `lock_timeout=30s` e `statement_timeout=300s`. Para ver quem segura:
```sql
SELECT a.pid, a.usename, a.state, now()-a.xact_start AS em_transacao, a.query
  FROM pg_stat_activity a
 WHERE a.datname = 'creditflow' AND a.pid IN (SELECT unnest(pg_blocking_pids(b.pid)) FROM pg_stat_activity b WHERE b.wait_event_type = 'Lock');
```
Culpado típico: uma transação longa da aplicação (`idle in transaction`), um relatório, ou um `ALTER TABLE` que precisa de `ACCESS EXCLUSIVE` numa tabela quente. **Não** mate pids da aplicação sem saber o que fazem; uma transação `idle in transaction` há minutos pode ser encerrada (`SELECT pg_terminate_backend(pid)`) e o servidor já faz isso sozinho após 60 s (`idle_in_transaction_session_timeout`).

## 3. Falhou
- `activeDeadlineSeconds` (10 min) estourado → era lock ou migração pesada demais. Migração que reescreve tabela grande não roda no deploy: vira tarefa separada, em lotes.
- Erro de SQL (o log mostra o arquivo e a linha) → bug no DDL, quase sempre falta de idempotência (`ADD COLUMN` sem `IF NOT EXISTS`): **reverta o commit do deploy** no gitops (a versão anterior não precisa da migração) e corrija.
- Sucesso parcial: cada arquivo roda numa transação (`psql -1`). Os arquivos anteriores ficaram aplicados; o que falhou, não. Como todo DDL é idempotente, rodar de novo (novo sync) reaplica tudo sem efeito nos que já passaram.

## 4. Regra que evita este runbook
Toda migração é **compatível com a versão anterior do código** (expandir, migrar dados, contrair em outro deploy). É o que permite voltar a aplicação sem voltar o banco.
