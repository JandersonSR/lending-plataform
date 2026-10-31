locals {
  dominio = data.terraform_remote_state.plataforma.outputs.dominio
}

# 1) O cluster local registrado no Argo CD COM metadados. O ApplicationSet (gerador "clusters")
#    e a Application raiz leem estes rótulos e anotações e passam para os charts.
resource "kubernetes_secret_v1" "cluster" {
  metadata {
    name      = "in-cluster"
    namespace = "argocd"
    labels = {
      "argocd.argoproj.io/secret-type" = "cluster"
      "creditflow.io/ambiente"         = var.ambiente
    }
    annotations = {
      "creditflow.io/dominio"          = local.dominio
      "creditflow.io/tenant-id"        = local.azure.tenant_id
      "creditflow.io/key-vault-url"    = local.azure.key_vault_url
      "creditflow.io/eso-client-id"    = local.azure.eso_client_id
      "creditflow.io/app-client-id"    = local.azure.app_client_id
      "creditflow.io/postgres-host"    = local.azure.postgres_fqdn
      "creditflow.io/gateway-ip-nome"  = local.azure.gateway_ip_nome
      "creditflow.io/grupo-recursos"   = local.azure.grupo_recursos
      "creditflow.io/harbor-host"      = "harbor.${local.dominio}"
    }
  }
  data = {
    name   = "in-cluster"
    server = "https://kubernetes.default.svc"
    config = jsonencode({ tlsClientConfig = { insecure = false } })
  }
}

# 2) A raiz do app-of-apps. É um chart Helm (gitops/raiz) que gera as Applications de cada
#    componente com a onda de sincronização certa.
resource "kubernetes_manifest" "raiz" {
  manifest = {
    apiVersion = "argoproj.io/v1alpha1"
    kind       = "Application"
    metadata = {
      name       = "creditflow-raiz"
      namespace  = "argocd"
      finalizers = ["resources-finalizer.argocd.argoproj.io"]
    }
    spec = {
      project = "default"
      source = {
        repoURL        = var.repositorio
        targetRevision = var.revisao
        path           = "gitops/raiz"
        helm = {
          valuesObject = {
            ambiente    = var.ambiente
            repositorio = var.repositorio
            revisao     = var.revisao
            dominio     = local.dominio
            acme = {
              email    = var.email_acme
              servidor = var.acme_producao ? "https://acme-v02.api.letsencrypt.org/directory" : "https://acme-staging-v02.api.letsencrypt.org/directory"
            }
            azure = {
              tenantId        = local.azure.tenant_id
              keyVaultUrl     = local.azure.key_vault_url
              esoClientId     = local.azure.eso_client_id
              appClientId     = local.azure.app_client_id
              postgresHost    = local.azure.postgres_fqdn
              ipPublicoNome   = local.azure.gateway_ip_nome
              grupoRecursosIp = local.azure.grupo_recursos
            }
          }
        }
      }
      destination = { server = "https://kubernetes.default.svc", namespace = "argocd" }
      syncPolicy = {
        automated = { prune = true, selfHeal = true }
      }
    }
  }
  depends_on = [kubernetes_secret_v1.cluster]
}
