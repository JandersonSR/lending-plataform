# Identidades de CARGA DE TRABALHO (Workload Identity). Nenhuma tem segredo: o Entra ID
# confia no emissor OIDC do cluster para um "subject" exato (namespace + ServiceAccount).

# External Secrets Operator: lê segredos do Key Vault.
resource "azurerm_user_assigned_identity" "eso" {
  name                = "id-${local.nome}-eso"
  resource_group_name = azurerm_resource_group.rg.name
  location            = azurerm_resource_group.rg.location
  tags                = local.tags
}

resource "azurerm_federated_identity_credential" "eso" {
  name                      = "eso-azure-kv"
  user_assigned_identity_id = azurerm_user_assigned_identity.eso.id
  audience                  = ["api://AzureADTokenExchange"]
  issuer                    = azurerm_kubernetes_cluster.aks.oidc_issuer_url
  subject                   = "system:serviceaccount:external-secrets:eso-azure-kv"
}

# Aplicação: uma identidade, uma credencial federada por perfil (ServiceAccount do chart).
# Hoje ela não recebe nenhum papel: está pronta para Blob, Service Bus, Entra ID no Postgres...
resource "azurerm_user_assigned_identity" "app" {
  name                = "id-${local.nome}-app"
  resource_group_name = azurerm_resource_group.rg.name
  location            = azurerm_resource_group.rg.location
  tags                = local.tags
}

resource "azurerm_federated_identity_credential" "app" {
  for_each = toset(local.perfis)

  name                      = "creditflow-${each.key}"
  user_assigned_identity_id = azurerm_user_assigned_identity.app.id
  audience                  = ["api://AzureADTokenExchange"]
  issuer                    = azurerm_kubernetes_cluster.aks.oidc_issuer_url
  subject                   = "system:serviceaccount:${local.namespace_app}:creditflow-${each.key}"
}
