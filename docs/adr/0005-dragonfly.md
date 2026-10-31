# ADR 0005: Dragonfly como cache distribuído, atrás do HybridCache

- **Status:** aceita
- **Data:** 2026-10-30
- **Contexto do curso:** módulo 5 (lições 5.1 a 5.4)

## Contexto

A simulação é pública e CPU-bound, e lê a tabela de taxas por faixa de score em **toda** chamada. A tabela
muda raramente (decisão de comitê) e é igual para todos. Com várias réplicas da `api-simulacao` (HPA), cada
uma indo à origem a cada simulação desperdiça a origem; um cache só em memória não deixa as réplicas
compartilharem nem invalidarem juntas.

## Decisão

- **HybridCache** (`Microsoft.Extensions.Caching.Hybrid`) com L1 em memória (1 min) e L2 no **Dragonfly**
  (10 min), pelo `StackExchange.Redis` (o Dragonfly fala o protocolo do Redis).
- O cache entra como **decorador** de `ITabelaDeTaxas` (`TabelaDeTaxasEmCache`); quem simula não sabe dele.
- **Invalidação por evento:** `IInvalidadorDeTaxas` apaga a tag/chave (L1 local + L2) e publica num canal
  pub/sub; as outras réplicas apagam o próprio L1. Mudança de configuração também invalida.
- **Cache é opcional:** `abortConnect=false`, `connectTimeout=1000`, `syncTimeout/asyncTimeout=500`; o
  HybridCache trata falha de leitura do L2 como "não achei" e só registra falha de escrita. Dragonfly fora =
  mais lento, não fora do ar. O Dragonfly **não** entra na readiness.
- **Nunca no cache:** status de proposta, decisão de crédito, saldo/parcelas, idempotência de pagamento,
  dados pessoais (CPF, renda), tokens. Fonte da verdade continua sendo o banco.

## Por que Dragonfly e não Redis

| | Redis (OSS) | Dragonfly | Valkey |
|---|---|---|---|
| Modelo | Uma thread para comandos (I/O em threads desde o 6) | Multithread, *shared-nothing* (um pedaço das chaves por thread) | Fork do Redis 7.2 pela Linux Foundation, multithread de I/O |
| Licença | Mudou para RSALv2/SSPLv1 em 2024 e voltou a ter AGPLv3 como opção no Redis 8 | BSL 1.1 (vira Apache 2.0 depois de um prazo) | BSD |
| Compatibilidade | É a referência | Protocolo do Redis, a maioria dos comandos; não 100% (conferir módulos e Lua) | Alta (mesma base) |

Escolhemos Dragonfly para o curso porque escala verticalmente num nó só (sem cluster para operar) e é
compatível com o cliente que já usamos. **Trocar por Redis ou Valkey é trocar a imagem**, porque o código
fala o protocolo. Verifique a licença BSL antes de oferecer o Dragonfly como serviço a terceiros.

## Consequências

**Positivas**: uma ida à origem por chave e por processo (debandada contida); réplicas compartilham o L2;
invalidação em segundos; nenhuma dependência dura.

**Negativas**: mais uma peça para operar (memória, `maxmemory`, versão); a proteção contra debandada é por
processo (N réplicas = até N idas à origem numa chave fria); o pub/sub é "no máximo uma vez" (réplica
desconectada perde o aviso, e o L1 de 1 min é o limite da divergência).
