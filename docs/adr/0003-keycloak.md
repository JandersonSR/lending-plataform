# ADR 0003: Keycloak como servidor de identidade, com o realm como código

- **Status:** aceita
- **Data:** 2026-10-30
- **Contexto do curso:** módulo 3 (lições 3.1 a 3.6)

## Contexto

O CreditFlow tem três tipos de chamador: pessoas no navegador (clientes e analistas), processos de
servidor (backoffice, conciliação) e o público anônimo (simulação). A API precisa saber **quem** chama
e **o que** pode fazer, sem guardar senha nem sessão. O mesmo arranjo tem de rodar no notebook
(`docker compose`), no CI (Testcontainers) e no AKS.

Opções:

1. **Keycloak** (open source, CNCF): roda em contêiner, realm exportável em JSON.
2. **Microsoft Entra ID / External ID**: gerenciado, integrado à Azure; não roda local.
3. **Auth0 / Okta**: gerenciados, cobrados por usuário ativo; não rodam local.
4. **Duende IdentityServer**: biblioteca .NET; licença comercial acima de um faturamento; mais código nosso.
5. Autenticação própria (tabela de usuários + JWT emitido pela API): descartada de saída.

## Decisão

Keycloak, realm `creditflow` versionado em `deploy/local/keycloak/creditflow-realm.json`.

- **Clients:** `creditflow-web` (público, Authorization Code + PKCE S256, sem implicit nem password),
  `creditflow-api` (só audiência, bearer-only), `creditflow-backoffice` (confidencial, client credentials,
  conta de serviço com `operador-financeiro`), `creditflow-testes` (password grant, **só no realm local**).
- **Roles de realm** de negócio: `cliente`, `analista`, `operador-financeiro`, `admin`.
- **Audiência explícita:** mapper `oidc-audience-mapper` com `creditflow-api` em todo client que chama a API.
- **Tempos:** access token 5 min, código 60 s, sessão ociosa 30 min, sessão máxima 10 h, rotação de refresh token.
- **API:** JwtBearer com `Authority` = realm, `Audience` = `creditflow-api`, só RS256, folga de relógio de 30 s,
  `MapInboundClaims = false`, `realm_access.roles` traduzido para claims `role` no `OnTokenValidated`.
  Endpoints pedem **policies por caso de uso** (`Politicas.cs`), nunca roles. Regras de recurso (a proposta é
  sua?) ficam no caso de uso, com 404 para não revelar existência.
- **Atrás de proxy/contêiner:** hostname público fixo no Keycloak (`KC_HOSTNAME`) com backchannel dinâmico;
  a API usa `Auth:Authority` público e `Auth:MetadataAddress` interno.

## Consequências

**Positivas**

- A API só fala OIDC/JWT padrão: trocar de provedor muda configuração (`Auth:*`) e a tradução de roles
  (`ClaimsKeycloak`), não endpoints nem domínio.
- O mesmo realm sobe igual no notebook, no CI e no cluster; mudanças passam por pull request.
- A validação é local (assinatura + JWKS em memória): o Keycloak fora não derruba requisições com token válido.

**Negativas / custos**

- Operar o Keycloak: banco próprio, atualização (lançamentos frequentes), alta disponibilidade, backup.
- O import (`--import-realm`) **só cria**: realm existente é ignorado. Fora do ambiente local, mudanças
  exigem admin API, keycloak-config-cli ou o operador; isso é um processo a manter.
- Sem revogação imediata de access token (5 min de janela). Bloqueio instantâneo exigiria introspecção
  ou lista de bloqueio distribuída.
- O segredo dos clients confidenciais no JSON só é aceitável no realm local. Fora dele: Key Vault e,
  de preferência, `private_key_jwt`.

## Se um dia trocarmos por Entra ID

Muda: `Auth:Authority`, `Auth:Audience` (o Application ID URI), e as roles passam a vir na claim `roles`
(App Roles) em vez de `realm_access.roles`, ou seja, a tradução em `ClaimsKeycloak.PapeisDoRealm`.
Não muda: policies, endpoints, `ICurrentUser`, domínio. O PKCE e o client credentials são os mesmos fluxos.
