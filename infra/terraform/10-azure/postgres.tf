# Azure Database for PostgreSQL Flexible Server com ACESSO PRIVADO (injeção na VNet):
# sem IP público, resolvido pelo DNS privado, alcançável só de dentro da VNet (os nós do AKS).
# Um servidor, três bancos: creditflow (módulos), keycloak e harbor_registry.

# Senha efêmera: gerada a cada plan, mas só ENVIADA quando versao_senhas muda (write-only).
# Nunca aparece no plano nem no estado.
ephemeral "random_password" "postgres_admin" {
  length           = 32
  special          = true
  override_special = "-_.~"
}

ephemeral "random_password" "postgres_app" {
  length  = 32
  special = false
}

ephemeral "random_password" "postgres_keycloak" {
  length  = 32
  special = false
}

resource "azurerm_postgresql_flexible_server" "pg" {
  name                = "psql-${local.nome}-${random_string.sufixo.result}"
  resource_group_name = azurerm_resource_group.rg.name
  location            = azurerm_resource_group.rg.location
  version             = var.postgres.versao
  sku_name            = var.postgres.sku
  storage_mb          = var.postgres.armazenamento_mb
  auto_grow_enabled   = true
  zone                = length(var.zonas) > 0 ? var.zonas[0] : null

  delegated_subnet_id           = azurerm_subnet.postgres.id
  private_dns_zone_id           = azurerm_private_dns_zone.postgres.id
  public_network_access_enabled = false

  administrator_login               = "cfadmin"
  administrator_password_wo         = ephemeral.random_password.postgres_admin.result
  administrator_password_wo_version = var.versao_senhas

  authentication {
    password_auth_enabled         = true
    active_directory_auth_enabled = false
  }

  backup_retention_days        = var.postgres.backup_dias
  geo_redundant_backup_enabled = var.postgres.backup_geo

  dynamic "high_availability" {
    for_each = var.postgres.ha ? [1] : []
    content {
      mode                      = "ZoneRedundant"
      standby_availability_zone = length(var.zonas) > 1 ? var.zonas[1] : null
    }
  }

  maintenance_window {
    day_of_week  = 0 # domingo
    start_hour   = 4 # 04:00 UTC = 01:00 em Brasília
    start_minute = 0
  }

  tags       = local.tags
  depends_on = [azurerm_private_dns_zone_virtual_network_link.postgres]

  lifecycle {
    # Failover de HA troca a zona primária; não é drift.
    ignore_changes = [zone, high_availability[0].standby_availability_zone]
  }
}

resource "azurerm_postgresql_flexible_server_database" "bancos" {
  for_each  = toset(["creditflow", "keycloak", "harbor_registry"])
  name      = each.key
  server_id = azurerm_postgresql_flexible_server.pg.id
  charset   = "UTF8"
  collation = "en_US.utf8"
}

# Conexões: o default do B1ms é baixo; 4 perfis x réplicas x pool somam rápido (lição 9.3).
resource "azurerm_postgresql_flexible_server_configuration" "log_lock_waits" {
  name      = "log_lock_waits"
  server_id = azurerm_postgresql_flexible_server.pg.id
  value     = "on"
}

resource "azurerm_postgresql_flexible_server_configuration" "idle_in_tx" {
  name      = "idle_in_transaction_session_timeout"
  server_id = azurerm_postgresql_flexible_server.pg.id
  value     = "60000"
}
