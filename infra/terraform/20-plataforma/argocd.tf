# Argo CD: o último da camada. Daqui em diante, quem aplica coisas no cluster é ele (pull),
# a partir do Git (gitops/). A Application raiz é criada na camada 30-app.
resource "helm_release" "argo_cd" {
  name             = "argocd"
  repository       = "https://argoproj.github.io/argo-helm"
  chart            = "argo-cd"
  version          = var.versoes.argo_cd
  namespace        = "argocd"
  create_namespace = true
  wait             = true
  values           = [file("${path.module}/valores/argocd.yaml")]

  depends_on = [
    helm_release.external_secrets,
    helm_release.strimzi,
    helm_release.keda,
    helm_release.dragonfly_operator,
    helm_release.cert_manager,
    helm_release.policy_controller,
  ]
}

output "dominio" { value = local.dominio }
output "harbor_url" { value = "https://${local.host_harbor}" }
