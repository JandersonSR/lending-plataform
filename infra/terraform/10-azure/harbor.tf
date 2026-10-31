# Armazenamento das camadas de imagem do Harbor: Azure Blob (durável, barato, fora do cluster).
# O driver de storage do Harbor (distribution) autentica com a CHAVE da conta, por isso esta
# conta, ao contrário da do estado, mantém shared_access_key_enabled.
resource "azurerm_storage_account" "harbor" {
  name                            = "st${var.prefixo}hb${var.ambiente}${random_string.sufixo.result}"
  resource_group_name             = azurerm_resource_group.rg.name
  location                        = azurerm_resource_group.rg.location
  account_tier                    = "Standard"
  account_replication_type        = local.prod ? "ZRS" : "LRS"
  min_tls_version                 = "TLS1_2"
  allow_nested_items_to_be_public = false
  shared_access_key_enabled       = true
  tags                            = local.tags
}

resource "azurerm_storage_container" "harbor" {
  name                  = "harbor"
  storage_account_id    = azurerm_storage_account.harbor.id
  container_access_type = "private"
}

resource "azurerm_key_vault_secret" "harbor_storage" {
  name         = "harbor-storage-chave"
  key_vault_id = azurerm_key_vault.kv.id
  value_wo     = azurerm_storage_account.harbor.primary_access_key
  # Rotacionou a chave da conta? Incremente versao_senhas.
  value_wo_version = var.versao_senhas
  depends_on       = [azurerm_role_assignment.kv_operador]
}
