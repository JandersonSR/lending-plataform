# ADR 0011: Terraform em quatro camadas, com estado remoto por camada e ambiente

- **Status:** aceita
- **Data:** 2026-10-30
- **Contexto do curso:** módulo 9; curso Plataforma .NET, módulo 9 (estado e camadas)

## Contexto

A infraestrutura do CreditFlow tem ritmos de mudança muito diferentes: a conta do estado e a
identidade do CI mudam quase nunca; rede, AKS e bancos mudam algumas vezes por trimestre; os
componentes de plataforma (Harbor, Argo CD, operadores) mudam todo mês; a aplicação muda várias vezes
por dia (mas essa é do Argo CD, não do Terraform). Um único Terraform teria um `plan` lento, um raio
de explosão enorme (um erro no Helm do Harbor bloqueando uma mudança de rede) e um problema técnico:
`kubernetes_manifest` precisa da CRD instalada **durante o plan**, o que não existe num apply único.

## Decisão

| Camada | O que cria | Estado | Quem aplica |
|---|---|---|---|
| `00-base` | Storage do estado (sem chave de conta, versionamento, lock de exclusão), identidade OIDC do GitHub | local na 1ª vez, depois migrado | pessoa, raramente |
| `10-azure` | RG, VNet e sub-redes, NSG, IP público do Gateway, AKS (Workload Identity, OIDC, Entra ID + Azure RBAC, CNI Overlay + Cilium), Postgres Flexible privado, DocumentDB + Private Endpoint, Key Vault + segredos, identidades e credenciais federadas, Storage do Harbor | `10-azure/<amb>.tfstate` | CI com aprovação |
| `20-plataforma` | Helm: Envoy Gateway, cert-manager, ESO, Strimzi, KEDA, operador Dragonfly, policy-controller, Harbor, Argo CD | `20-plataforma/<amb>.tfstate` | CI com aprovação |
| `30-app` | Secret de cluster do Argo CD (ponte de dados) e a Application raiz | `30-app/<amb>.tfstate` | CI com aprovação |

Regras:
- camadas de cima leem as de baixo só por `terraform_remote_state` (saídas explícitas, nada secreto);
- senhas geradas com `ephemeral "random_password"` e gravadas com argumentos write-only
  (`administrator_password_wo`, `value_wo`, `data_wo`): não aparecem no plano nem no estado;
- lock pelo lease do blob (`-lock-timeout=5m` no CI); `azurerm` 5.x com registro de provedores explícito;
- versões de todos os charts num mapa só (`20-plataforma/variables.tf`), copiado pelo laboratório local.

## Consequências

**Positivas**: plans rápidos e focados; um erro numa camada não trava as outras; permissões do CI
por camada; a ordem CRD → CR é natural (20 antes de 30).

**Negativas**: quatro `init/plan/apply` por ambiente; mudanças que cruzam camadas (ex.: um novo segredo
no cofre usado por um chart) exigem dois PRs em ordem; saída renomeada numa camada quebra a de cima
(o `plan` da de cima acusa). Documentado na lição 9.5, com a ordem de apply e de destroy.
