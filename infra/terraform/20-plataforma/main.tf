locals {
  dominio     = var.dominio != "" ? var.dominio : "${replace(local.azure.gateway_ip, ".", "-")}.nip.io"
  host_harbor = "harbor.${local.dominio}"
}

# ------------------------------------------------------------------------------------------
# Ordem (depends_on): Envoy Gateway (traz as CRDs da Gateway API) -> cert-manager (lê Gateways)
# -> ESO -> operadores -> Harbor -> Argo CD. As CRDs vêm de cada chart.
# ------------------------------------------------------------------------------------------

# Porta de entrada. O ingress-nginx foi aposentado (manutenção encerrada em mar/2026) e o
# add-on App Routing gerenciado (NGINX) tem suporte só até nov/2026: o projeto usa Gateway API
# com Envoy Gateway, igual no kind e no AKS. Decisão discutida na lição 9.4.
resource "helm_release" "envoy_gateway" {
  name             = "eg"
  repository       = "oci://docker.io/envoyproxy"
  chart            = "gateway-helm"
  version          = var.versoes.envoy_gateway
  namespace        = "envoy-gateway-system"
  create_namespace = true
  wait             = true
}

resource "helm_release" "cert_manager" {
  name             = "cert-manager"
  repository       = "https://charts.jetstack.io"
  chart            = "cert-manager"
  version          = var.versoes.cert_manager
  namespace        = "cert-manager"
  create_namespace = true
  wait             = true
  values = [yamlencode({
    crds = { enabled = true }
    # Liga o suporte a Gateway API: certificados a partir de anotações no Gateway e
    # desafio HTTP-01 por HTTPRoute.
    config = {
      apiVersion       = "controller.config.cert-manager.io/v1alpha1"
      kind             = "ControllerConfiguration"
      enableGatewayAPI = true
    }
  })]
  depends_on = [helm_release.envoy_gateway]
}

resource "helm_release" "external_secrets" {
  name             = "external-secrets"
  repository       = "https://charts.external-secrets.io"
  chart            = "external-secrets"
  version          = var.versoes.external_secrets
  namespace        = "external-secrets"
  create_namespace = true
  wait             = true
}

resource "helm_release" "strimzi" {
  name             = "strimzi"
  repository       = "oci://quay.io/strimzi-helm"
  chart            = "strimzi-kafka-operator"
  version          = var.versoes.strimzi
  namespace        = "strimzi"
  create_namespace = true
  wait             = true
  values = [yamlencode({
    # O operador fica num namespace e só olha o namespace dos dados.
    watchNamespaces = ["creditflow-dados"]
  })]
}

resource "helm_release" "keda" {
  name             = "keda"
  repository       = "https://kedacore.github.io/charts"
  chart            = "keda"
  version          = var.versoes.keda
  namespace        = "keda"
  create_namespace = true
  wait             = true
}

resource "helm_release" "dragonfly_operator" {
  name             = "dragonfly-operator"
  repository       = "oci://ghcr.io/dragonflydb/dragonfly-operator/helm"
  chart            = "dragonfly-operator"
  version          = var.versoes.dragonfly_operator
  namespace        = "dragonfly-operator-system"
  create_namespace = true
  wait             = true
}

# Verificação de assinatura das imagens no momento da admissão (lição 10.4).
resource "helm_release" "policy_controller" {
  name             = "policy-controller"
  repository       = "https://sigstore.github.io/helm-charts"
  chart            = "policy-controller"
  version          = var.versoes.policy_controller
  namespace        = "cosign-system"
  create_namespace = true
  wait             = true
}

# O operador do Keycloak NÃO tem chart Helm oficial (é instalado por kustomize a partir de
# keycloak/keycloak-k8s-resources). Quem instala é o Argo CD, na onda -3 (gitops/raiz).
