# Preencha com a saída "backend_hcl" da camada 00-base.
resource_group_name  = "rg-creditflow-base"
storage_account_name = "stcreditflowtfXXXXX"
container_name       = "tfstate"
use_azuread_auth     = true
key                  = "20-plataforma/dev.tfstate"
