#!/usr/bin/env bash
# Copia para dentro dos charts os arquivos que moram no código e que os charts precisam
# (o Helm só lê arquivos de dentro do próprio chart; o Argo CD lê do Git, então as cópias
# são versionadas). O CI roda com --verificar e falha se alguma cópia estiver desatualizada.
#   DDL dos módulos  -> deploy/helm/creditflow/sql/<modulo>.<arquivo>.sql   (Job de migrações)
#   realm do Keycloak -> deploy/k8s/keycloak/realm/creditflow-realm.json
set -euo pipefail
cd "$(dirname "$0")/../.."
verificar=${1:-}
destino_sql=deploy/helm/creditflow/sql
tmp=$(mktemp -d); trap 'rm -rf "$tmp"' EXIT

for f in $(find src/Modules -path '*/Sql/*.sql' | sort); do
  modulo=$(echo "$f" | sed -E 's#src/Modules/([^/]+)/.*#\1#' | tr '[:upper:]' '[:lower:]')
  cp "$f" "$tmp/$modulo.$(basename "$f")"
done
cp deploy/local/keycloak/creditflow-realm.json "$tmp/creditflow-realm.json"

if [ "$verificar" = "--verificar" ]; then
  diff -r <(cd "$tmp" && ls *.sql) <(cd "$destino_sql" && ls *.sql) >/dev/null || { echo "DDL: lista de arquivos diferente; rode deploy/scripts/copiar-artefatos.sh"; exit 1; }
  for f in "$tmp"/*.sql; do cmp -s "$f" "$destino_sql/$(basename "$f")" || { echo "DDL desatualizado: $(basename "$f")"; exit 1; }; done
  cmp -s "$tmp/creditflow-realm.json" deploy/k8s/keycloak/realm/creditflow-realm.json || { echo "realm desatualizado"; exit 1; }
  echo "cópias em dia"
else
  rm -f "$destino_sql"/*.sql
  cp "$tmp"/*.sql "$destino_sql/"
  cp "$tmp/creditflow-realm.json" deploy/k8s/keycloak/realm/creditflow-realm.json
  ls "$destino_sql"
fi
