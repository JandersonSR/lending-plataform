# ADR 0008: Harbor como registry de imagens, e não Azure Container Registry

- **Status:** aceita
- **Data:** 2026-10-30
- **Contexto do curso:** módulo 10 (lições 10.1 a 10.4)

## Contexto

O CreditFlow publica uma imagem por commit na `main`, e o cluster só pode rodar imagem que passou
por scan e foi assinada pelo CI. O registry precisa de: projetos com permissão separada, contas de
robô com escopo mínimo (push no CI, pull no cluster), scan de vulnerabilidade com **bloqueio de pull**
por severidade, retenção de tags, cache de imagens públicas (Docker Hub limita pulls anônimos) e
suporte a assinaturas e SBOM como artefatos OCI.

Opções consideradas:

1. **Azure Container Registry (ACR) Premium**: gerenciado, integração nativa com o AKS (`AcrPull` na
   identidade do kubelet, sem imagePullSecret), geo-replicação, tarefas de build. Scan pelo Microsoft
   Defender for Containers (cobrado por imagem escaneada), sem bloqueio de pull nativo por severidade.
2. **Harbor no próprio cluster**: open source (CNCF graduado), Trivy embutido com política "impedir
   pull de imagem com vulnerabilidade >= X", robôs por projeto, retenção, cache de proxy, replicação,
   e as mesmas regras em qualquer nuvem ou on-premises.
3. **GitHub Container Registry**: zero operação, mas sem scan com bloqueio e com o registry fora da
   rede do cluster.

## Decisão

Opção 2, **Harbor 2.15 (chart 1.19)**, instalado pelo Terraform (`infra/terraform/20-plataforma/harbor.tf`):

- banco no Azure Database for PostgreSQL Flexible (`harbor_registry`), camadas no Azure Blob;
- exposto pelo Gateway público (`expose.type: route`, `harbor.<domínio>`), TLS no Gateway;
- projeto `creditflow` privado, com "impedir imagens vulneráveis" em `High` e "somente assinadas";
- robô `creditflow+ci` (push/pull no projeto) para o GitHub Actions e `creditflow+pull` (só pull)
  para o cluster (`harbor-pull`, via External Secrets);
- projeto proxy-cache `dockerhub` para imagens públicas (Postgres, Mongo, Dragonfly...);
- segredos (admin, banco, chave do Storage) lidos do Key Vault com recurso efêmero e gravados com
  `data_wo`: não passam pelo estado do Terraform.

## Consequências

**Positivas**
- A política de segurança mora no registry e vale para qualquer cliente (CI, cluster, laptop).
- Mesma ferramenta em dev local, AKS e um futuro on-premises; é o que muitas empresas brasileiras
  reguladas (bancos, fintechs) usam.
- O curso ensina conceitos que valem para qualquer registry: projeto, robô, digest, cache, política.

**Negativas (e o que fazemos)**
- **Operamos o Harbor**: atualização, backup do banco, monitoramento. Mitigação: banco e blobs em
  serviços gerenciados da Azure; o Harbor em si fica sem estado no cluster.
- **Ovo e galinha**: o cluster que roda o Harbor puxa imagens do Harbor. Se o Harbor cair, pods novos
  da aplicação não sobem (os que estão rodando continuam). Mitigação: imagens do próprio Harbor vêm
  do Docker Hub/GHCR, não dele mesmo; runbook `deploy-e-rollback.md` cobre "ImagePullBackOff".
- Sem integração nativa AKS↔registry: precisa de `imagePullSecret`. Mitigação: um único Secret por
  namespace, gerado pelo ESO a partir do Key Vault.
- Custo: ~2 vCPU e 4 GiB de nós + Storage + banco. Em dev cabe no pool existente.

## Quando revisar

Se a empresa padronizar em ACR com Defender e Azure Policy de admissão, migrar é trocar o host da
imagem e os robôs por `AcrPull`: o chart do CreditFlow já recebe `imagem.repositorio` por parâmetro.
