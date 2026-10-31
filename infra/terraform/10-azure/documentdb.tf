# MongoDB do CreditFlow (Simulação e Notificações): Azure DocumentDB, o antigo
# "Azure Cosmos DB for MongoDB (vCore)". Decisão e custos: docs/adr/0010-mongodb-na-azure.md.
# Recurso ARM Microsoft.DocumentDB/mongoClusters (azurerm_mongo_cluster).
#
# Limitação conhecida: a senha do administrador NÃO tem variante write-only neste recurso,
# e a connection string exportada contém a senha. Ela fica no ESTADO: por isso o estado
# mora num Storage com RBAC e sem chave de conta (camada 00-base).
resource "random_password" "documentdb" {
  length  = 32
  special = false
}

resource "azurerm_mongo_cluster" "mongo" {
  name                   = "docdb-${local.nome}-${random_string.sufixo.result}"
  resource_group_name    = azurerm_resource_group.rg.name
  location               = azurerm_resource_group.rg.location
  administrator_username = "cfadmin"
  administrator_password = random_password.documentdb.result
  compute_tier           = var.documentdb.tier
  high_availability_mode = var.documentdb.ha ? "ZoneRedundantPreferred" : "Disabled"
  shard_count            = 1
  storage_size_in_gb     = var.documentdb.armazenamento_gb
  version                = "8.0"
  public_network_access  = "Disabled"
  tags                   = local.tags
}

resource "azurerm_private_endpoint" "mongo" {
  name                = "pe-${local.nome}-docdb"
  resource_group_name = azurerm_resource_group.rg.name
  location            = azurerm_resource_group.rg.location
  subnet_id           = azurerm_subnet.endpoints.id
  tags                = local.tags

  private_service_connection {
    name                           = "docdb"
    private_connection_resource_id = azurerm_mongo_cluster.mongo.id
    subresource_names              = ["MongoCluster"]
    is_manual_connection           = false
  }

  private_dns_zone_group {
    name                 = "docdb"
    private_dns_zone_ids = [azurerm_private_dns_zone.documentdb.id]
  }
}

resource "azurerm_key_vault_secret" "mongo" {
  name         = "mongo-connection-string"
  key_vault_id = azurerm_key_vault.kv.id
  value        = azurerm_mongo_cluster.mongo.connection_strings[0].value
  content_type = "connection string do Azure DocumentDB"
  depends_on   = [azurerm_role_assignment.kv_operador]
}
