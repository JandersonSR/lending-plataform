locals {
  nome = "${var.prefixo}-${var.ambiente}"
  prod = var.ambiente == "prod"

  tags = merge({
    projeto  = "creditflow"
    ambiente = var.ambiente
    camada   = "10-azure"
  }, var.tags)

  # Sub-redes (/16 dividida). Com Azure CNI Overlay, os PODS não consomem IP da VNet
  # (vêm de pod_cidr); só os nós. Por isso /20 sobra para centenas de nós.
  sub_redes = {
    aks       = cidrsubnet(var.espaco_vnet, 4, 0)  # 10.40.0.0/20
    postgres  = cidrsubnet(var.espaco_vnet, 8, 16) # 10.40.16.0/24 (delegada ao Postgres Flexible)
    endpoints = cidrsubnet(var.espaco_vnet, 8, 17) # 10.40.17.0/24 (private endpoints)
  }
  pod_cidr     = "192.168.0.0/16" # fora da VNet: rede overlay dos pods
  service_cidr = "10.41.0.0/16"   # IPs virtuais dos Services; não pode colidir com a VNet
  dns_service  = "10.41.0.10"

  namespace_app = "creditflow"
  perfis        = ["api-geral", "api-simulacao", "api-pagamentos", "worker-pagamentos"]
}

data "azurerm_client_config" "atual" {}

resource "random_string" "sufixo" {
  length  = 4
  special = false
  upper   = false
}

resource "azurerm_resource_group" "rg" {
  name     = "rg-${local.nome}"
  location = var.local
  tags     = local.tags
}

# O CI faz apply só dentro deste grupo de recursos.
resource "azurerm_role_assignment" "ci_contributor" {
  count                = var.principal_ci == "" ? 0 : 1
  scope                = azurerm_resource_group.rg.id
  role_definition_name = "Contributor"
  principal_id         = var.principal_ci
}

resource "azurerm_role_assignment" "ci_rbac_admin" {
  count                = var.principal_ci == "" ? 0 : 1
  scope                = azurerm_resource_group.rg.id
  role_definition_name = "Role Based Access Control Administrator"
  principal_id         = var.principal_ci
}
