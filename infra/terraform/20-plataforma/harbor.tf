# Harbor: registry de imagens com projetos, robôs, scan (Trivy) e bloqueio por política.
# Banco: o Postgres Flexible (banco harbor_registry). Camadas das imagens: Azure Blob.
# Segredos: lidos do Key Vault com recurso EFÊMERO e gravados no Secret com data_wo:
# nenhuma senha passa pelo estado do Terraform nem pelos values do Helm.

resource "kubernetes_namespace_v1" "harbor" {
  metadata { name = "harbor" }
}

ephemeral "azurerm_key_vault_secret" "harbor_admin" {
  name         = "harbor-admin-senha"
  key_vault_id = local.azure.key_vault_id
}

ephemeral "azurerm_key_vault_secret" "postgres_admin" {
  name         = "postgres-admin-senha"
  key_vault_id = local.azure.key_vault_id
}

ephemeral "azurerm_key_vault_secret" "harbor_storage" {
  name         = "harbor-storage-chave"
  key_vault_id = local.azure.key_vault_id
}

variable "versao_segredos_harbor" {
  description = "Incremente depois de rotacionar as senhas no Key Vault (versao_senhas da 10-azure)."
  type        = number
  default     = 1
}

resource "kubernetes_secret_v1" "harbor" {
  metadata {
    name      = "harbor-externos"
    namespace = kubernetes_namespace_v1.harbor.metadata[0].name
  }
  data_wo = {
    HARBOR_ADMIN_PASSWORD    = ephemeral.azurerm_key_vault_secret.harbor_admin.value
    password                 = ephemeral.azurerm_key_vault_secret.postgres_admin.value
    AZURE_STORAGE_ACCESS_KEY = ephemeral.azurerm_key_vault_secret.harbor_storage.value
  }
  data_wo_revision = var.versao_segredos_harbor
}

resource "helm_release" "harbor" {
  name       = "harbor"
  repository = "https://helm.goharbor.io"
  chart      = "harbor"
  version    = var.versoes.harbor
  namespace  = kubernetes_namespace_v1.harbor.metadata[0].name
  timeout    = 900
  wait       = true

  values = [yamlencode({
    externalURL = "https://${local.host_harbor}"
    expose = {
      # HTTPRoute no Gateway público (o TLS termina no Gateway, listener https-harbor).
      type = "route"
      tls  = { enabled = false }
      route = {
        hosts      = [local.host_harbor]
        parentRefs = [{ name = "publico", namespace = "gateway", sectionName = "https-harbor", group = "gateway.networking.k8s.io", kind = "Gateway" }]
      }
    }
    existingSecretAdminPassword    = kubernetes_secret_v1.harbor.metadata[0].name
    existingSecretAdminPasswordKey = "HARBOR_ADMIN_PASSWORD"
    database = {
      type = "external"
      external = {
        host           = local.azure.postgres_fqdn
        port           = "5432"
        username       = local.azure.postgres_admin
        coreDatabase   = "harbor_registry"
        existingSecret = kubernetes_secret_v1.harbor.metadata[0].name
        sslmode        = "require"
      }
    }
    persistence = {
      imageChartStorage = {
        type = "azure"
        azure = {
          accountname    = local.azure.harbor_storage_conta
          container      = local.azure.harbor_storage_container
          existingSecret = kubernetes_secret_v1.harbor.metadata[0].name
        }
      }
    }
    trivy = { enabled = true }
  })]

  depends_on = [helm_release.envoy_gateway, helm_release.cert_manager]
}
