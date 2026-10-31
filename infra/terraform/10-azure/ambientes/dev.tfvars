ambiente          = "dev"
zonas             = []
aks_sku           = "Free"
saida_por_nat     = false
versao_kubernetes = "1.36"
pool_sistema      = { vm_size = "Standard_D2ads_v5", minimo = 1, maximo = 2 }
pool_apps         = { vm_size = "Standard_D4ads_v5", minimo = 1, maximo = 3, spot = true }
postgres = {
  sku              = "B_Standard_B1ms"
  versao           = "17"
  armazenamento_mb = 32768
  ha               = false
  backup_dias      = 7
  backup_geo       = false
}
documentdb = { tier = "M10", ha = false, armazenamento_gb = 32 }
# administradores_aks = ["<object id do seu usuário ou grupo>"]
# principal_ci        = "<saída github_principal_id da 00-base>"
