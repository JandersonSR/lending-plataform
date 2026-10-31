# Key Vault com RBAC (sem access policies). Os segredos que a plataforma consome via
# External Secrets nascem aqui. Valores sensíveis gerados no Terraform usam value_wo
# (write-only): vão para o cofre e NÃO ficam no estado.
resource "azurerm_key_vault" "kv" {
  name                       = "kv-${var.prefixo}-${var.ambiente}-${random_string.sufixo.result}"
  resource_group_name        = azurerm_resource_group.rg.name
  location                   = azurerm_resource_group.rg.location
  tenant_id                  = data.azurerm_client_config.atual.tenant_id
  sku_name                   = "standard"
  rbac_authorization_enabled = true
  purge_protection_enabled   = local.prod
  soft_delete_retention_days = local.prod ? 90 : 7

  # Público + RBAC: o runner do GitHub precisa gravar os segredos no apply. Em prod endureça:
  # default_action = "Deny" + private endpoint + runner self-hosted dentro da VNet (lição 9.4).
  public_network_access_enabled = true
  network_acls {
    bypass         = "AzureServices"
    default_action = "Allow"
  }

  tags = local.tags
}

# Quem roda o apply grava segredos.
resource "azurerm_role_assignment" "kv_operador" {
  scope                = azurerm_key_vault.kv.id
  role_definition_name = "Key Vault Secrets Officer"
  principal_id         = data.azurerm_client_config.atual.object_id
}

# O ESO só LÊ.
resource "azurerm_role_assignment" "kv_eso" {
  scope                = azurerm_key_vault.kv.id
  role_definition_name = "Key Vault Secrets User"
  principal_id         = azurerm_user_assigned_identity.eso.principal_id
}

ephemeral "random_password" "dragonfly" {
  length  = 32
  special = false
}

ephemeral "random_password" "grafana" {
  length  = 24
  special = false
}

ephemeral "random_password" "harbor_admin" {
  length  = 24
  special = false
}

ephemeral "random_password" "backoffice" {
  length  = 40
  special = false
}

locals {
  # Segredos gerados aqui: nome no cofre => valor (write-only).
  segredos_wo = {
    "postgres-admin-senha"        = ephemeral.random_password.postgres_admin.result
    "postgres-app-senha"          = ephemeral.random_password.postgres_app.result
    "postgres-keycloak-senha"     = ephemeral.random_password.postgres_keycloak.result
    "dragonfly-senha"             = ephemeral.random_password.dragonfly.result
    "grafana-admin-senha"         = ephemeral.random_password.grafana.result
    "harbor-admin-senha"          = ephemeral.random_password.harbor_admin.result
    "keycloak-backoffice-segredo" = ephemeral.random_password.backoffice.result
  }
  nomes_segredos_wo = {
    "postgres-admin-senha" = 1, "postgres-app-senha" = 1, "postgres-keycloak-senha" = 1,
    "dragonfly-senha" = 1, "grafana-admin-senha" = 1, "harbor-admin-senha" = 1,
    "keycloak-backoffice-segredo" = 1,
  }
  # Não secretos (mas consumidos pelo mesmo caminho, para a aplicação ler tudo do cofre).
  segredos_texto = {
    "postgres-host"          = azurerm_postgresql_flexible_server.pg.fqdn
    "postgres-admin-usuario" = azurerm_postgresql_flexible_server.pg.administrator_login
  }
}

resource "azurerm_key_vault_secret" "gerados" {
  # for_each NÃO pode receber valor efêmero: itera sobre os NOMES (conhecidos no plan)
  # e busca o valor efêmero só no argumento write-only.
  for_each         = toset(keys(local.nomes_segredos_wo))
  name             = each.key
  key_vault_id     = azurerm_key_vault.kv.id
  value_wo         = local.segredos_wo[each.key]
  value_wo_version = var.versao_senhas
  content_type     = "senha gerada pelo Terraform (10-azure)"
  depends_on       = [azurerm_role_assignment.kv_operador]
}

resource "azurerm_key_vault_secret" "texto" {
  for_each     = local.segredos_texto
  name         = each.key
  key_vault_id = azurerm_key_vault.kv.id
  value        = each.value
  depends_on   = [azurerm_role_assignment.kv_operador]
}
