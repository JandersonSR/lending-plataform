#!/usr/bin/env bash
# Sobe o CreditFlow inteiro num kind local: operadores, Kafka (Strimzi), Dragonfly, Keycloak,
# Postgres, Mongo, observabilidade e os 4 perfis da aplicação. Lição 7.6.
# Requisitos: docker, kind, kubectl, helm 3.18+ ou 4.x. Uns 8 GB de RAM livres.
# Uso (na raiz do repositório): ./deploy/k8s/local/subir.sh
set -euo pipefail

# Versões: as MESMAS do infra/terraform/20-plataforma/versoes.tf (fonte da verdade).
STRIMZI=1.3.0
ENVOY_GATEWAY=v1.9.3
KEDA=2.21.0
DRAGONFLY_OPERATOR=v1.7.1
KEYCLOAK=26.8.1
METRICS_SERVER=3.14.0

aguardar() { kubectl wait --for="$1" --timeout="${3:-300s}" "$2" ${4:+-n "$4"}; }

echo ">> cluster"
kind get clusters | grep -qx creditflow || kind create cluster --config deploy/k8s/local/kind.yaml

echo ">> namespaces"
for ns in creditflow creditflow-dados keycloak observabilidade; do
  kubectl create namespace "$ns" --dry-run=client -o yaml | kubectl apply -f -
done

echo ">> operadores"
# O HPA precisa do metrics-server; o kind não traz. --kubelet-insecure-tls: o kubelet do kind usa certificado autoassinado.
helm repo add metrics-server https://kubernetes-sigs.github.io/metrics-server/ --force-update >/dev/null
helm upgrade --install metrics-server metrics-server/metrics-server --version "$METRICS_SERVER" \
  -n kube-system --set 'args={--kubelet-insecure-tls}' --wait
helm upgrade --install strimzi oci://quay.io/strimzi-helm/strimzi-kafka-operator --version "$STRIMZI" \
  -n strimzi --create-namespace --set watchNamespaces='{creditflow-dados}' --wait
helm upgrade --install eg oci://docker.io/envoyproxy/gateway-helm --version "$ENVOY_GATEWAY" \
  -n envoy-gateway-system --create-namespace --wait
helm repo add kedacore https://kedacore.github.io/charts --force-update >/dev/null
helm upgrade --install keda kedacore/keda --version "$KEDA" -n keda --create-namespace --wait
helm upgrade --install dragonfly-operator oci://ghcr.io/dragonflydb/dragonfly-operator/helm/dragonfly-operator \
  --version "$DRAGONFLY_OPERATOR" -n dragonfly-operator-system --create-namespace --wait
kubectl -n keycloak apply -k "github.com/keycloak/keycloak-k8s-resources/kubernetes?ref=$KEYCLOAK"
aguardar condition=Available deployment/keycloak-operator 300s keycloak

echo ">> dependências do laboratório"
kubectl apply -f deploy/k8s/local/gateway.yaml
kubectl apply -f deploy/k8s/local/postgres.yaml -f deploy/k8s/local/mongo.yaml -f deploy/k8s/local/lgtm.yaml
aguardar condition=Ready pod/postgres-0 300s creditflow-dados

echo ">> dados: Kafka (KRaft) e Dragonfly"
helm upgrade --install dados deploy/k8s/dados -n creditflow-dados -f deploy/k8s/dados/values-local.yaml
aguardar condition=Ready kafka/creditflow 600s creditflow-dados

echo ">> Keycloak"
kubectl -n keycloak create secret generic keycloak-banco \
  --from-literal=host=postgres.creditflow-dados.svc \
  --from-literal=username=keycloak --from-literal=password=keycloak-local-123 \
  --from-literal=adminUsuario=postgres --from-literal=adminSenha=postgres-local-123 \
  --dry-run=client -o yaml | kubectl apply -f -
# Fora do Argo CD, o hook "Sync" do bootstrap é só um Job comum: roda junto com o resto.
helm upgrade --install keycloak deploy/k8s/keycloak -n keycloak -f deploy/k8s/keycloak/values-local.yaml
aguardar condition=Ready keycloak/keycloak 600s keycloak

echo ">> imagem da aplicação"
docker buildx build -f deploy/docker/Dockerfile -t creditflow:dev --load .
kind load docker-image creditflow:dev --name creditflow

echo ">> CreditFlow (4 perfis)"
helm upgrade --install creditflow deploy/helm/creditflow -n creditflow \
  -f deploy/helm/creditflow/values-local.yaml --wait --timeout 10m

cat <<'FIM'

Pronto. Em terminais separados:
  kubectl -n keycloak port-forward svc/keycloak-service 8081:8080
  kubectl -n envoy-gateway-system port-forward \
    "$(kubectl -n envoy-gateway-system get svc -l gateway.envoyproxy.io/owning-gateway-name=local -o name)" 8080:80
  kubectl -n observabilidade port-forward svc/lgtm 3000:3000
Teste:
  curl -s -H 'Host: api.127.0.0.1.nip.io' localhost:8080/v1/simulacoes -X POST -H 'Content-Type: application/json' \
       -d '{"valor":10000,"prazoMeses":24,"sistema":"price"}'
FIM
