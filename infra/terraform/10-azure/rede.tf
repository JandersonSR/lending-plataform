resource "azurerm_virtual_network" "vnet" {
  name                = "vnet-${local.nome}"
  resource_group_name = azurerm_resource_group.rg.name
  location            = azurerm_resource_group.rg.location
  address_space       = [var.espaco_vnet]
  tags                = local.tags
}

resource "azurerm_subnet" "aks" {
  name                 = "snet-aks"
  resource_group_name  = azurerm_resource_group.rg.name
  virtual_network_name = azurerm_virtual_network.vnet.name
  address_prefixes     = [local.sub_redes.aks]
}

# Sub-rede DEDICADA e DELEGADA: o Postgres Flexible com acesso privado injeta a placa de
# rede do servidor aqui. Nada mais pode morar nela.
resource "azurerm_subnet" "postgres" {
  name                 = "snet-postgres"
  resource_group_name  = azurerm_resource_group.rg.name
  virtual_network_name = azurerm_virtual_network.vnet.name
  address_prefixes     = [local.sub_redes.postgres]

  delegation {
    name = "postgres-flexible"
    service_delegation {
      name    = "Microsoft.DBforPostgreSQL/flexibleServers"
      actions = ["Microsoft.Network/virtualNetworks/subnets/join/action"]
    }
  }
}

resource "azurerm_subnet" "endpoints" {
  name                 = "snet-endpoints"
  resource_group_name  = azurerm_resource_group.rg.name
  virtual_network_name = azurerm_virtual_network.vnet.name
  address_prefixes     = [local.sub_redes.endpoints]
}

# NSG na sub-rede dos nós: entrada da internet só em 80/443 (o Gateway). O resto do tráfego
# de entrada que o AKS precisa (LB health probe, VNet) já é permitido pelas regras padrão.
resource "azurerm_network_security_group" "aks" {
  name                = "nsg-${local.nome}-aks"
  resource_group_name = azurerm_resource_group.rg.name
  location            = azurerm_resource_group.rg.location
  tags                = local.tags

  security_rule {
    name                       = "permite-http-https"
    priority                   = 100
    direction                  = "Inbound"
    access                     = "Allow"
    protocol                   = "Tcp"
    source_address_prefix      = "Internet"
    source_port_range          = "*"
    destination_address_prefix = "*"
    destination_port_ranges    = ["80", "443"]
  }
}

resource "azurerm_subnet_network_security_group_association" "aks" {
  subnet_id                 = azurerm_subnet.aks.id
  network_security_group_id = azurerm_network_security_group.aks.id
}

# --------------------------- saída por NAT (opcional) ---------------------------
resource "azurerm_public_ip" "nat" {
  count               = var.saida_por_nat ? 1 : 0
  name                = "pip-${local.nome}-nat"
  resource_group_name = azurerm_resource_group.rg.name
  location            = azurerm_resource_group.rg.location
  allocation_method   = "Static"
  sku                 = "Standard"
  zones               = var.zonas
  tags                = local.tags
}

resource "azurerm_nat_gateway" "nat" {
  count               = var.saida_por_nat ? 1 : 0
  name                = "ng-${local.nome}"
  resource_group_name = azurerm_resource_group.rg.name
  location            = azurerm_resource_group.rg.location
  sku_name            = "Standard"
  tags                = local.tags
}

resource "azurerm_nat_gateway_public_ip_association" "nat" {
  count                = var.saida_por_nat ? 1 : 0
  nat_gateway_id       = azurerm_nat_gateway.nat[0].id
  public_ip_address_id = azurerm_public_ip.nat[0].id
}

resource "azurerm_subnet_nat_gateway_association" "aks" {
  count          = var.saida_por_nat ? 1 : 0
  subnet_id      = azurerm_subnet.aks.id
  nat_gateway_id = azurerm_nat_gateway.nat[0].id
}

# --------------------------- entrada: IP fixo do Gateway ---------------------------
# Criado aqui (e não pelo Service LoadBalancer) para sobreviver à recriação do cluster e
# para o DNS do domínio não mudar. O EnvoyProxy aponta para ele pela anotação azure-pip-name.
resource "azurerm_public_ip" "gateway" {
  name                = "pip-${local.nome}-gateway"
  resource_group_name = azurerm_resource_group.rg.name
  location            = azurerm_resource_group.rg.location
  allocation_method   = "Static"
  sku                 = "Standard"
  zones               = var.zonas
  domain_name_label   = "${local.nome}-${random_string.sufixo.result}"
  tags                = local.tags
}

# --------------------------- DNS privado ---------------------------
resource "azurerm_private_dns_zone" "postgres" {
  # Precisa terminar em .postgres.database.azure.com.
  name                = "${local.nome}.private.postgres.database.azure.com"
  resource_group_name = azurerm_resource_group.rg.name
  tags                = local.tags
}

resource "azurerm_private_dns_zone_virtual_network_link" "postgres" {
  name                = "vnet-${local.nome}"
  private_dns_zone_id = azurerm_private_dns_zone.postgres.id
  virtual_network_id  = azurerm_virtual_network.vnet.id
  tags                = local.tags
}

resource "azurerm_private_dns_zone" "documentdb" {
  name                = "privatelink.mongocluster.cosmos.azure.com"
  resource_group_name = azurerm_resource_group.rg.name
  tags                = local.tags
}

resource "azurerm_private_dns_zone_virtual_network_link" "documentdb" {
  name                = "vnet-${local.nome}"
  private_dns_zone_id = azurerm_private_dns_zone.documentdb.id
  virtual_network_id  = azurerm_virtual_network.vnet.id
  tags                = local.tags
}
