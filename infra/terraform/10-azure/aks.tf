# Identidade do PLANO DE CONTROLE do AKS. Atribuída pelo usuário (e não pelo sistema) para
# receber permissão na VNet e no IP público ANTES de o cluster existir.
resource "azurerm_user_assigned_identity" "aks" {
  name                = "id-${local.nome}-aks"
  resource_group_name = azurerm_resource_group.rg.name
  location            = azurerm_resource_group.rg.location
  tags                = local.tags
}

# Network Contributor no grupo: o AKS precisa ligar os nós à sub-rede e pendurar o IP
# público do Gateway (pip-*-gateway) no Load Balancer que ele cria.
resource "azurerm_role_assignment" "aks_rede" {
  scope                = azurerm_resource_group.rg.id
  role_definition_name = "Network Contributor"
  principal_id         = azurerm_user_assigned_identity.aks.principal_id
}

resource "azurerm_kubernetes_cluster" "aks" {
  name                = "aks-${local.nome}"
  resource_group_name = azurerm_resource_group.rg.name
  location            = azurerm_resource_group.rg.location
  dns_prefix          = local.nome
  node_resource_group = "rg-${local.nome}-nos"
  kubernetes_version  = var.versao_kubernetes
  sku_tier            = var.aks_sku
  tags                = local.tags

  # Atualizações: patch do Kubernetes automático; imagem do nó (SO) semanal.
  automatic_upgrade_channel = "patch"
  node_os_upgrade_channel   = "NodeImage"

  # Workload Identity: o cluster publica um emissor OIDC (oidc_issuer_url) e o webhook
  # injeta nos pods o token da ServiceAccount para trocar por um token do Entra ID.
  oidc_issuer_enabled       = true
  workload_identity_enabled = true

  # Autenticação no cluster só por Entra ID; autorização por Azure RBAC. Sem kubeconfig "admin".
  local_account_disabled = true
  azure_active_directory_role_based_access_control {
    azure_rbac_enabled = true
    tenant_id          = data.azurerm_client_config.atual.tenant_id
  }

  image_cleaner_enabled        = true
  image_cleaner_interval_hours = 48

  identity {
    type         = "UserAssigned"
    identity_ids = [azurerm_user_assigned_identity.aks.id]
  }

  # azurerm 5.x: bloco obrigatório. Manual = pools declarados aqui (sem Node Auto Provisioning).
  node_provisioning_profile {
    mode = "Manual"
  }

  default_node_pool {
    name                         = "sistema"
    temporary_name_for_rotation  = "sistematmp"
    vm_size                      = var.pool_sistema.vm_size
    auto_scaling_enabled         = true
    min_count                    = var.pool_sistema.minimo
    max_count                    = var.pool_sistema.maximo
    vnet_subnet_id               = azurerm_subnet.aks.id
    zones                        = var.zonas
    os_sku                       = "AzureLinux"
    only_critical_addons_enabled = local.prod # em prod, só CoreDNS, Konnectivity etc. no pool de sistema

    upgrade_settings {
      max_surge                     = "33%"
      drain_timeout_in_minutes      = 30
      node_soak_duration_in_minutes = 0
    }
  }

  network_profile {
    network_plugin      = "azure"
    network_plugin_mode = "overlay"
    network_data_plane  = "cilium"
    network_policy      = "cilium"
    pod_cidr            = local.pod_cidr
    service_cidr        = local.service_cidr
    dns_service_ip      = local.dns_service
    load_balancer_sku   = "standard"
    outbound_type       = var.saida_por_nat ? "userAssignedNATGateway" : "loadBalancer"
  }

  lifecycle {
    # O autoescalador de nós muda node_count; não é diferença a corrigir.
    ignore_changes = [default_node_pool[0].node_count]
  }

  depends_on = [
    azurerm_role_assignment.aks_rede,
    azurerm_subnet_nat_gateway_association.aks,
  ]
}

resource "azurerm_kubernetes_cluster_node_pool" "apps" {
  name                        = "apps"
  temporary_name_for_rotation = "appstmp"
  kubernetes_cluster_id       = azurerm_kubernetes_cluster.aks.id
  mode                        = "User"
  vm_size                     = var.pool_apps.vm_size
  auto_scaling_enabled        = true
  min_count                   = var.pool_apps.minimo
  max_count                   = var.pool_apps.maximo
  vnet_subnet_id              = azurerm_subnet.aks.id
  zones                       = var.zonas
  os_sku                      = "AzureLinux"
  priority                    = var.pool_apps.spot ? "Spot" : "Regular"
  eviction_policy             = var.pool_apps.spot ? "Delete" : null
  spot_max_price              = var.pool_apps.spot ? -1 : null
  node_labels                 = { "creditflow.io/pool" = "apps" }
  tags                        = local.tags

  upgrade_settings {
    max_surge                = "33%"
    drain_timeout_in_minutes = 30
  }

  lifecycle {
    ignore_changes = [node_count]
  }
}

# Quem administra o cluster (kubectl/helm) via Azure RBAC.
resource "azurerm_role_assignment" "aks_admins" {
  for_each             = toset(var.administradores_aks)
  scope                = azurerm_kubernetes_cluster.aks.id
  role_definition_name = "Azure Kubernetes Service RBAC Cluster Admin"
  principal_id         = each.value
}

resource "azurerm_role_assignment" "aks_ci" {
  count                = var.principal_ci == "" ? 0 : 1
  scope                = azurerm_kubernetes_cluster.aks.id
  role_definition_name = "Azure Kubernetes Service RBAC Cluster Admin"
  principal_id         = var.principal_ci
}
