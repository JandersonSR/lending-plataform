ambiente          = "prod"
zonas             = ["1", "2", "3"]
aks_sku           = "Standard"
saida_por_nat     = true
versao_kubernetes = "1.36"
pool_sistema      = { vm_size = "Standard_D2ads_v5", minimo = 3, maximo = 3 }
pool_apps         = { vm_size = "Standard_D4ads_v5", minimo = 3, maximo = 9, spot = false }
postgres = {
  sku              = "GP_Standard_D2ds_v5"
  versao           = "17"
  armazenamento_mb = 65536
  ha               = true
  backup_dias      = 35
  backup_geo       = true
}
documentdb = { tier = "M30", ha = true, armazenamento_gb = 64 }
