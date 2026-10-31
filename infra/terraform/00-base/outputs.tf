output "backend_hcl" {
  description = "Conteúdo dos arquivos *.backend.hcl das outras camadas (troque a key por camada/ambiente)."
  value       = <<-EOT
    resource_group_name  = "${azurerm_resource_group.base.name}"
    storage_account_name = "${azurerm_storage_account.estado.name}"
    container_name       = "${azurerm_storage_container.estado.name}"
    use_azuread_auth     = true
    key                  = "10-azure/dev.tfstate"
  EOT
}

output "github_client_id" {
  description = "Vai em vars.AZURE_CLIENT_ID no GitHub (não é segredo)."
  value       = azurerm_user_assigned_identity.github.client_id
}

output "github_principal_id" {
  value = azurerm_user_assigned_identity.github.principal_id
}

output "tenant_id" {
  value = data.azurerm_client_config.atual.tenant_id
}

output "subscription_id" {
  value = data.azurerm_client_config.atual.subscription_id
}
