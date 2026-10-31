# ADR 0009: GitOps com Argo CD (pull), e não deploy por pipeline (push)

- **Status:** aceita
- **Data:** 2026-10-30
- **Contexto do curso:** módulo 11

## Contexto

O CreditFlow tem dois ambientes (dev e prod), cada um num AKS, e um conjunto grande de componentes:
operadores (Strimzi, KEDA, Dragonfly, Keycloak, ESO), recursos desses operadores (Kafka, tópicos,
usuários, Dragonfly, Keycloak, realm), observabilidade e a aplicação (4 perfis). Precisamos de:
ordem de aplicação (CRD antes de CR, CR antes da aplicação), promoção dev → prod revisada, volta de
versão rápida, e detecção de mudança manual no cluster.

Opções:

1. **Pipeline empurrando** (`helm upgrade` no GitHub Actions): simples, mas a credencial de
   cluster-admin vive no CI, o cluster diverge do Git sem ninguém ver, e a ordem entre componentes
   vira script.
2. **Flux**: GitOps sólido, CRDs (`Kustomization`, `HelmRelease`), sem interface própria.
3. **Argo CD**: GitOps com interface, `Application`/`ApplicationSet`, ondas de sincronização,
   hooks, diff visual, rollback por histórico, multi-cluster.

## Decisão

Opção 3, **Argo CD 3.5 (chart argo-cd 10.11)**, instalado pelo Terraform 20-plataforma; a ponte com
o Terraform é a camada 30-app, que cria **só** o Secret de cluster (com client ids, FQDN do Postgres e
URL do cofre em anotações) e a Application raiz `creditflow-raiz`.

- **App-of-apps** (`gitops/raiz`, um chart Helm) gera uma Application por componente, com ondas:
  `-3` operadores do Argo (Keycloak) e observabilidade, `-2` plataforma (Gateway, emissor ACME,
  lojas de segredo, política de assinatura), `-1` dados (Kafka, Dragonfly) e OTel Collector,
  `0` Keycloak e regras de SLO, `1` aplicação.
- **Saúde de `Application` restaurada** no `argocd-cm` (removida no Argo CD 1.8): sem ela a raiz não
  espera os filhos ficarem saudáveis e as ondas não ordenam nada.
- **ApplicationSet** `creditflow` com gerador `clusters` (rótulo `creditflow.io/ambiente`): cada
  cluster gera a Application do seu ambiente, com os valores de `gitops/ambientes/<ambiente>`.
- **Promoção por PR**: o CI grava tag + digest em `gitops/ambientes/dev/creditflow.yaml` por PR;
  o workflow `promover` copia o mesmo digest para prod em outro PR, que exige aprovação.
- **Migrações** como hook do Helm (`pre-install,pre-upgrade`), que o Argo CD executa como `PreSync`.
- Interface sem exposição pública (port-forward) e sem SSO no laboratório.

## Consequências

**Positivas**
- O CI não tem credencial do cluster: só abre PR. Quem aplica é o Argo CD, de dentro.
- O Git é o registro do que roda e de quem mudou; voltar versão é `git revert` do commit do bot.
- `selfHeal` desfaz `kubectl edit` em segundos: correção de emergência também passa pelo Git.

**Negativas (e o que fazemos)**
- `helm list`/`helm history` deixam de mostrar a aplicação (o Argo CD renderiza e aplica o YAML).
  Histórico = Git + Argo CD.
- Mais uma peça crítica: se o Argo CD cair, nada quebra, mas nada é implantado. Runbook
  `deploy-e-rollback.md`.
- Campos mudados por outros controladores (réplicas, `caBundle`) precisam de `ignoreDifferences`.
- Ondas entre Applications dependem da verificação de saúde customizada; esquecer dela é o erro
  mais comum (lição 11.3).
