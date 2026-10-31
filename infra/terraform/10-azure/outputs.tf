# Saídas lidas pela 20-plataforma e pela 30-app (terraform_remote_state). Nada secreto aqui.
output "ambiente" { value = var.ambiente }
output "tenant_id" { value = data.azurerm_client_config.atual.tenant_id }
output "grupo_recursos" { value = azurerm_resource_group.rg.name }
output "aks_nome" { value = azurerm_kubernetes_cluster.aks.name }
output "aks_id" { value = azurerm_kubernetes_cluster.aks.id }
output "aks_host" { value = azurerm_kubernetes_cluster.aks.kube_config[0].host }
output "aks_ca" {
  value = azurerm_kubernetes_cluster.aks.kube_config[0].cluster_ca_certificate
}
output "oidc_issuer_url" { value = azurerm_kubernetes_cluster.aks.oidc_issuer_url }
output "key_vault_id" { value = azurerm_key_vault.kv.id }
output "key_vault_url" { value = azurerm_key_vault.kv.vault_uri }
output "eso_client_id" { value = azurerm_user_assigned_identity.eso.client_id }
output "app_client_id" { value = azurerm_user_assigned_identity.app.client_id }
output "postgres_fqdn" { value = azurerm_postgresql_flexible_server.pg.fqdn }
output "postgres_admin" { value = azurerm_postgresql_flexible_server.pg.administrator_login }
output "gateway_ip" { value = azurerm_public_ip.gateway.ip_address }
output "gateway_ip_nome" { value = azurerm_public_ip.gateway.name }
output "gateway_fqdn_azure" { value = azurerm_public_ip.gateway.fqdn }
output "harbor_storage_conta" { value = azurerm_storage_account.harbor.name }
output "harbor_storage_container" { value = azurerm_storage_container.harbor.name }
