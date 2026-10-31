# ADR 0006: Outbox com faixas, inbox e saga coreografada para o desembolso

- **Status:** aceita
- **Data:** 2026-10-30
- **Contexto do curso:** módulo 6 (lições 6.2 a 6.6)

## Contexto

Contratar um empréstimo envolve dois módulos com bancos (schemas) próprios: Propostas registra a
assinatura; Pagamentos manda o PIX por um gateway externo e cria as parcelas. Não existe transação
que cubra Postgres de Propostas, Kafka, Postgres de Pagamentos e o gateway. Requisitos:

- nenhum contrato assinado pode ficar sem desembolso, e nenhum desembolso pode acontecer duas vezes;
- o gateway falha de três jeitos: fora do ar, recusa, e o pior: **processa e a resposta se perde**;
- eventos de uma mesma proposta precisam chegar em ordem (Solicitado → Concluído → ParcelaPaga…);
- `api-pagamentos` e `worker-pagamentos` rodam com várias réplicas, todas publicando da mesma outbox.

## Decisão

1. **Outbox transacional** por módulo (`<schema>.outbox`): o evento é gravado no mesmo `SaveChanges`
   da mudança do agregado. Um **relay** (`OutboxRelay<TDbContext>`, BackgroundService) em cada pod
   publica com `acks=all` + produtor idempotente e marca `publicada_em`.
2. **Faixas (lanes) com `FOR UPDATE SKIP LOCKED`**: a chave (id da proposta) é mapeada por FNV-1a em
   16 faixas; cada relay trava algumas faixas livres (`outbox_faixas ... FOR UPDATE SKIP LOCKED`),
   lê só as pendentes delas em ordem de `id`, publica em sequência por chave e em paralelo entre chaves.
   Falhou uma mensagem: as seguintes da mesma chave esperam o próximo ciclo.
3. **Inbox** (`<schema>.inbox`, PK `(consumidor, evento_id)`) gravada **na mesma transação do efeito**,
   com `INSERT ... ON CONFLICT DO NOTHING`. Quando o efeito se reconhece pelo estado (transição com
   guarda, como em Propostas), a inbox é dispensada.
4. **Consumidor** com commit manual depois do efeito, retry no lugar com recuo exponencial, falhas
   classificadas (transitória: repete sem fim; permanente: DLT na hora; desconhecida: DLT após 10),
   `Seek` para voltar ao `Consume()` a cada 30 s de retry (nunca estoura `max.poll.interval.ms`).
   DLT por grupo: `<grupo>.dlt`, com tópico/partição/offset de origem e o motivo nos cabeçalhos.
5. **Saga coreografada** (sem orquestrador): `ContratoAssinado` → Pagamentos → `DesembolsoConcluido`
   (Propostas: Liberada) | `DesembolsoFalhou` (Propostas compensa: DesembolsoFalhou).
6. **Gateway com idempotência + consulta antes de desistir**: chave `desembolso-{id}` fixa; esgotadas
   as tentativas, `ObterOuCancelarAsync` descobre se o PIX saiu e, se não saiu, cancela a chave.
   Só então `DesembolsoFalhou`. Se nem a consulta responde, não falha: tenta mais tarde.

## Alternativas descartadas

- **Publicar direto depois do commit** (dual write): perde eventos quando o processo cai entre o
  commit e o publish; ou inventa eventos se publicar antes do commit que depois falha.
- **SKIP LOCKED nas linhas da outbox, sem faixas**: correto para "não perder", mas com vários relays e
  fila acumulada reordena eventos da mesma chave. Medido no protótipo (Postgres 16, 4 relays,
  2.000 eventos, 40 chaves, backlog): **913 eventos fora de ordem** sem faixas, **0** com faixas.
- **Um relay só (líder eleito / advisory lock)**: preserva ordem, mas vira gargalo e ponto único;
  as faixas dão paralelismo com ordem por chave.
- **Debezium/CDC lendo o WAL**: excelente em escala, mas mais uma peça (Kafka Connect) para operar;
  fica como evolução (ver módulo 12).
- **Orquestrador (MassTransit state machine, Temporal)**: vale a partir de 4 ou 5 passos com
  compensações cruzadas e prazos; com 2 participantes e 1 compensação, coreografia é menos código
  e menos uma peça para cair.
- **Transação distribuída (2PC/XA)**: Kafka e o gateway não participam; e trava recursos esperando rede.

## Consequências

**Positivas:** nada se perde (outbox), nada se repete no efeito (inbox/estado + idempotência no
gateway), ordem por proposta preservada com N relays, falha do gateway vira compensação explícita.

**Negativas / custos:**
- "Pelo menos uma vez" de ponta a ponta: duplicatas no tópico são normais (no protótipo, de 2% a 5% das
  entregas com 3% de ciclos morrendo antes do COMMIT). Todo consumidor novo precisa ser idempotente.
- Latência outbox → Kafka de até `IntervaloOcioso` (500 ms) quando o sistema está ocioso.
- Mudar o número de faixas com pendentes pode reordenar uma chave na transição: drenar antes.
- A inbox cresce; limpeza com retenção (14 dias) **maior** que a retenção do tópico (7 dias).
- A saga coreografada é implícita: para entender o fluxo, leia os diagramas do módulo 6 e os
  logs/traces (o traceparent atravessa outbox e Kafka).

## Como verificar

- Protótipos e números: lição 6.4 (relay) e 6.5 (consumidor), scripts em
  `learnflow/projetos/creditflow/docs/modulo-06/labs/`.
- Testes: `tests/CreditFlow.Modules.Pagamentos.Tests` (idempotência no Postgres com Testcontainers).
