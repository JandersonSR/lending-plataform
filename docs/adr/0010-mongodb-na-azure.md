# ADR 0010: MongoDB na Azure: Azure DocumentDB (antigo Cosmos DB for MongoDB vCore)

- **Status:** aceita
- **Data:** 2026-10-30
- **Contexto do curso:** módulo 9 (lição 9.3); ADR 0004 (por que MongoDB)

## Contexto

Simulação (histórico de simulações, documento com a tabela inteira) e Notificações usam MongoDB
(MongoDB.Driver 3.13). Volume esperado: centenas de milhares de simulações por mês, documentos de
5 a 30 KB, leitura por id e por cliente, sem transação multi-documento, sem change streams.
Requisitos: acesso privado (sem IP público), backup gerenciado, alta disponibilidade em prod,
custo baixo em dev, e o mínimo de operação para um time sem DBA.

Opções (preços de lista em USD, out/2026; confira na calculadora antes de decidir):

| | Azure DocumentDB (vCore) | MongoDB Atlas na Azure | Operador no AKS |
|---|---|---|---|
| O que é | Serviço Azure com protocolo MongoDB (motor próprio, open source "DocumentDB") | O MongoDB de verdade, gerenciado pela MongoDB Inc., rodando na Azure | MongoDB Community/Enterprise via operador `mongodb-kubernetes` |
| Dev | M10 sem HA (1 vCore burstable, 2 GiB), ou Free | Flex (US$ 8 a 30/mês) ou M10 (~US$ 0,08/h, ~US$ 57/mês) | 1 pod no pool que já existe (custo marginal) |
| Prod | M30 (2 vCores, 8 GiB) com HA por zona: o HA dobra o custo dos nós | M30 (2 vCPU, 8 GB, ~US$ 0,51/h, ~US$ 370/mês, 3 nós já incluídos) | 3 pods em 3 zonas + discos Premium + backup por sua conta |
| Rede privada | Private Endpoint na VNet (zona `privatelink.mongocluster.cosmos.azure.com`) | Private Endpoint (Atlas em M10+), configurado nos dois lados | Nativa (está no cluster) |
| Compatibilidade | Alta para CRUD/agregações comuns; recursos ausentes ou parciais (confira a lista oficial) | 100% (é o MongoDB) | 100% |
| Cobrança/contrato | Fatura Azure, conta no compromisso Azure (MACC) | Fatura separada ou Marketplace | Fatura dos nós |
| Operação | Nenhuma além de tamanho e índices | Nenhuma além de tamanho e índices | Upgrades, backup, restore, monitoração, PVs: tudo nosso |
| Terraform | `azurerm_mongo_cluster` (mesmo provider) | provider `mongodb/mongodbatlas` (outra credencial) | CRs no GitOps |

## Decisão

**Azure DocumentDB**, `infra/terraform/10-azure/documentdb.tf`:

- dev: `M10`, sem HA, 32 GiB; prod: `M30`, `ZoneRedundantPreferred`, 64 GiB;
- `public_network_access = Disabled` + Private Endpoint na `snet-endpoints` + zona DNS privada;
- connection string no Key Vault (`mongo-connection-string`), entregue ao pod pelo ESO;
- laboratório local (kind/compose): `mongo:8.0` comum, porque o código só usa o protocolo.

Por quê, na ordem que pesou:
1. **Operação zero** para um time pequeno e **mesma fatura/contrato Azure** do resto da plataforma.
2. **Mesmo provider do Terraform** e mesma rede privada (Private Endpoint), sem segunda credencial.
3. O uso do CreditFlow (CRUD, índices simples, sem change streams) está no centro do que o DocumentDB
   suporta bem.

## Consequências

**Negativas (e o que fazemos)**
- **Não é o MongoDB**: a compatibilidade é alta mas não total. Mitigação: os testes de integração
  rodam contra `mongo:8.0`, e um teste de fumaça no pipeline de dev roda contra o DocumentDB real;
  qualquer recurso novo do driver é conferido na lista de compatibilidade antes de usar.
- **Senha de administrador no estado do Terraform**: `azurerm_mongo_cluster` não tem
  `administrator_password_wo`, e a connection string exportada contém a senha. Mitigação: o estado
  mora num Storage sem chave de conta, só RBAC (camada 00-base). Próximo passo: autenticação Entra ID.
- **Usuário da aplicação é o administrador**: o recurso `azurerm_mongo_cluster_user` só cria usuários
  Entra ID com papel `root`. Aceito em dev; em prod, criar um usuário nativo com `readWrite` no banco
  `creditflow` (comando `createUser` via mongosh) e trocar o segredo no cofre.
- **HA dobra o custo** em prod. Aceito: é o que garante failover automático por zona.

## Quando revisar

- Se o CreditFlow precisar de change streams, Atlas Search ou transações complexas: Atlas.
- Se o custo do M30 com HA pesar mais que ~meio dia de engenharia por mês: reavaliar o operador
  (mas lembre que backup e restore testados também custam engenharia).
