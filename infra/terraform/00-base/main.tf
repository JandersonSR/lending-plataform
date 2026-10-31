data "azurerm_client_config" "atual" {}
data "azurerm_subscription" "atual" {}

resource "random_string" "sufixo" {
  length  = 5
  special = false
  upper   = false
}

resource "azurerm_resource_group" "base" {
  name     = "rg-${var.prefixo}-base"
  location = var.local
  tags     = { projeto = "creditflow", camada = "00-base" }
}

# ---------------------------------------------------------------------------
# Estado remoto: um container, um blob por camada e ambiente (10-azure/dev.tfstate...).
# O lock é o "lease" do blob: dois applies ao mesmo tempo, o segundo espera ou falha.
# ---------------------------------------------------------------------------
resource "azurerm_storage_account" "estado" {
  name                     = "st${var.prefixo}tf${random_string.sufixo.result}"
  resource_group_name      = azurerm_resource_group.base.name
  location                 = azurerm_resource_group.base.location
  account_tier             = "Standard"
  account_replication_type = "ZRS"
  min_tls_version          = "TLS1_2"

  # Sem chave de conta: só Entra ID + RBAC. Ninguém "vaza a connection string do estado".
  shared_access_key_enabled       = false
  default_to_oauth_authentication = true
  allow_nested_items_to_be_public = false

  blob_properties {
    versioning_enabled = true # um apply ruim que corrompe o estado tem volta
    delete_retention_policy {
      days = 30
    }
    container_delete_retention_policy {
      days = 30
    }
  }

  tags = azurerm_resource_group.base.tags
}

resource "azurerm_storage_container" "estado" {
  name                  = "tfstate"
  storage_account_id    = azurerm_storage_account.estado.id
  container_access_type = "private"
}

# Quem roda o primeiro apply (você) precisa ler/escrever blobs: RBAC de DADOS, não de gestão.
resource "azurerm_role_assignment" "estado_operador" {
  scope                = azurerm_storage_account.estado.id
  role_definition_name = "Storage Blob Data Contributor"
  principal_id         = data.azurerm_client_config.atual.object_id
}

# Evita apagar a conta do estado por engano (um "terraform destroy" nesta camada falha).
resource "azurerm_management_lock" "estado" {
  name       = "nao-apagar-estado"
  scope      = azurerm_storage_account.estado.id
  lock_level = "CanNotDelete"
  notes      = "Estado do Terraform do CreditFlow."
}

# ---------------------------------------------------------------------------
# Identidade do GitHub Actions: credencial federada (OIDC). O GitHub emite um token
# assinado para o job; o Entra ID confere emissor + subject e devolve um token da Azure.
# ---------------------------------------------------------------------------
resource "azurerm_user_assigned_identity" "github" {
  name                = "id-${var.prefixo}-github"
  resource_group_name = azurerm_resource_group.base.name
  location            = azurerm_resource_group.base.location
  tags                = azurerm_resource_group.base.tags
}

resource "azurerm_federated_identity_credential" "github_ambiente" {
  for_each = toset(var.ambientes)

  name                      = "github-${each.key}"
  user_assigned_identity_id = azurerm_user_assigned_identity.github.id
  audience                  = ["api://AzureADTokenExchange"]
  issuer                    = "https://token.actions.githubusercontent.com"
  # Só jobs que declaram "environment: <ambiente>" (e passam pelas regras de proteção dele).
  subject = "repo:${var.repositorio_github}:environment:${each.key}"
}

resource "azurerm_federated_identity_credential" "github_pr" {
  name                      = "github-pull-request"
  user_assigned_identity_id = azurerm_user_assigned_identity.github.id
  audience                  = ["api://AzureADTokenExchange"]
  issuer                    = "https://token.actions.githubusercontent.com"
  subject                   = "repo:${var.repositorio_github}:pull_request"
}

# Em PR: só "terraform plan". Leitura da assinatura + escrita no estado (o plan pega o lease).
resource "azurerm_role_assignment" "github_leitor" {
  scope                = data.azurerm_subscription.atual.id
  role_definition_name = "Reader"
  principal_id         = azurerm_user_assigned_identity.github.principal_id
}

resource "azurerm_role_assignment" "github_estado" {
  scope                = azurerm_storage_account.estado.id
  role_definition_name = "Storage Blob Data Contributor"
  principal_id         = azurerm_user_assigned_identity.github.principal_id
}
# O "Contributor" + "Role Based Access Control Administrator" que o apply precisa NÃO estão
# aqui de propósito: conceda no grupo de recursos de cada ambiente (10-azure), com escopo menor.
