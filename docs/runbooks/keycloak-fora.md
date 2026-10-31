# Runbook: Keycloak fora ou tokens recusados

**Alertas:** `KeycloakSemInstanciaPronta`, `ApiRespondendo401Demais`
**Impacto:** com o Keycloak fora, **logins novos e renovação de token falham**. Tokens já emitidos continuam válidos até expirar (5 min no realm creditflow): a API valida a assinatura com as chaves que já baixou (JWKS em cache). Por isso o impacto cresce aos poucos, não de uma vez.

## 1. O Keycloak está de pé?
```bash
kubectl -n keycloak get keycloak keycloak -o jsonpath='{.status.conditions}' ; echo
kubectl -n keycloak get pods
kubectl -n keycloak logs statefulset/keycloak --since=10m | tail -50
```
- `Ready=False` e log com `Connection refused`/`timeout` para o Postgres → passo 2.
- Pods `Pending`: recurso (o Keycloak pede 1 GiB) ou nó; `kubectl describe pod`.
- Pods ok mas 401 em massa → passo 3.

## 2. Banco do Keycloak
O Keycloak usa o banco `keycloak` no Postgres Flexible (o mesmo servidor do CreditFlow).
```bash
kubectl -n keycloak run psql --rm -it --image=postgres:17-alpine --restart=Never -- \
  psql "host=<postgres-fqdn> dbname=keycloak user=keycloak sslmode=require" -c 'select 1'
```
Servidor fora: portal do Azure (status do Flexible Server, failover de HA em andamento leva 1 a 2 min). Senha errada: o Secret `keycloak-banco` vem do Key Vault (`postgres-keycloak-senha`); force a sincronização com `kubectl -n keycloak annotate externalsecret keycloak-banco force-sync=$(date +%s) --overwrite`.

## 3. Keycloak de pé, mas a API responde 401
Compare o `iss` do token com `Auth:Authority` da API:
```bash
TOKEN=...; echo $TOKEN | cut -d. -f2 | base64 -d 2>/dev/null | jq '{iss,aud,exp}'
kubectl -n creditflow get cm creditflow-config -o yaml | grep Auth__
```
Causas clássicas: hostname do Keycloak mudou (o `iss` mudou junto), relógio adiantado/atrasado (folga de 30 s na API), `aud` sem `creditflow-api`, chave de assinatura rotacionada e a API ainda com o JWKS antigo (o JwtBearer busca de novo ao ver um `kid` desconhecido; se não conseguir alcançar o Keycloak, falha).

## 4. Estancar
Não há "modo sem autenticação". O que dá para fazer: escalar o Keycloak (`instancias` no Git), aumentar temporariamente a vida do token **não** é recomendado (ele não pode ser revogado). Comunique: "login indisponível; quem já está logado segue por alguns minutos".
