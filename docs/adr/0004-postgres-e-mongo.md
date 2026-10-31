# ADR 0004: PostgreSQL para o que é transação, MongoDB para o que é documento

- **Status:** aceita
- **Data:** 2026-10-30
- **Contexto do curso:** módulo 4 (lições 4.1 a 4.5)

## Contexto

Os dados do CreditFlow têm formas e exigências diferentes:

| Dado | Forma | Exigência |
|---|---|---|
| Clientes, Propostas, Pagamentos | Linhas com relações e regras entre linhas ("uma proposta ativa por cliente"), estados que mudam | Transação ACID, restrições no banco, concorrência controlada, outbox na mesma transação |
| Simulações | Documento imutável com uma tabela de 6 a 48 parcelas; volume alto (endpoint público); vida curta | Gravar rápido, ler inteiro por id, expirar sozinho |
| Notificações | Registro de um aviso por evento e canal; só acrescenta | Idempotência por evento, expirar sozinho, consultar por cliente |

## Decisão

- **PostgreSQL** (local: compose; Azure: Database for PostgreSQL Flexible Server) para Clientes, Propostas e
  Pagamentos, **um schema por módulo** e um `DbContext` por módulo, sem chave estrangeira entre schemas.
- **MongoDB** para `simulacoes` e `notificacoes`.

Detalhes que fazem parte da decisão:

- Propostas usa **concorrência otimista com `xmin`** (sem coluna de versão) e expõe a versão como `ETag`;
  decisões do analista mandam `If-Match`.
- "Uma proposta ativa por cliente" é um **índice único parcial**, não um `SELECT` antes do `INSERT`.
- O cronograma da proposta é `jsonb` na própria linha (lido e gravado sempre junto).
- A proposta **copia** as condições da simulação: apagar a simulação (TTL) não afeta a proposta.
- Simulação: um documento com a tabela inteira, dinheiro em `Decimal128`, UUID em binário padrão (subtipo 4),
  TTL por `expira_em` (90 dias), índice parcial `(cliente_id, criada_em)`.
- Notificação: índice **único** `(evento_id, canal)` como inbox; TTL de 180 dias em `criada_em`.
- **Nenhuma transação distribuída entre Postgres e Mongo.** Consistência entre os dois é eventual, por
  evento (outbox no Postgres → Kafka → consumidor idempotente que grava no Mongo).
- Schemas Postgres: SQL idempotente versionado (`Sql/001_*.sql`), aplicado na subida só em desenvolvimento
  e, nos ambientes, pelo Job de migrações do chart com o usuário de DDL.

## MongoDB na Azure (a decidir no módulo 9)

| Opção | A favor | Contra |
|---|---|---|
| Azure Cosmos DB for MongoDB (vCore) | Gerenciado na Azure, rede privada, backup | Compatibilidade não é 100% (conferir TTL, índices parciais, `explain`) |
| MongoDB Atlas (na Azure) | É o MongoDB de verdade, recursos completos | Outro fornecedor, contrato e rede (Private Link) à parte |
| Operador no cluster (Community/Percona) | Custo de licença zero, igual ao local | Nós operamos: backup, upgrade, discos, réplica |

Recomendação atual: **Atlas** se a compatibilidade total importar; **vCore** se a política for "tudo na Azure",
depois de rodar os testes de integração contra ele.

## Consequências

**Positivas**: cada dado no banco que combina com a forma dele; o Postgres garante as regras que importam
para dinheiro; o Mongo absorve o volume da simulação sem pesar nas tabelas transacionais e apaga sozinho.

**Negativas**: dois bancos para operar, monitorar e fazer backup; duas formas de "migração" (DDL no Postgres,
índices e formato de documento no Mongo); nada de `JOIN` entre simulação e proposta (por isso a cópia).
