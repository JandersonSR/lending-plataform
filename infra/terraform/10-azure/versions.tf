# Camada 10: a infraestrutura Azure de UM ambiente (dev ou prod).
#   terraform init -backend-config=ambientes/dev.backend.hcl
#   terraform plan -var-file=ambientes/dev.tfvars -out=plano
terraform {
  # 1.11+ para argumentos write-only (senhas que nunca vão para o estado).
  required_version = ">= 1.12.0"

  required_providers {
    azurerm = {
      source  = "hashicorp/azurerm"
      version = "~> 5.12"
    }
    random = {
      source  = "hashicorp/random"
      version = "~> 3.9"
    }
  }

  # resource_group_name, storage_account_name, container_name, key e use_azuread_auth
  # vêm de ambientes/<ambiente>.backend.hcl (saída "backend_hcl" da camada 00-base).
  backend "azurerm" {}
}

provider "azurerm" {
  features {
    key_vault {
      # Em dev, um destroy seguido de apply recria o cofre com o mesmo nome: precisa purgar.
      purge_soft_delete_on_destroy = true
    }
    resource_group {
      prevent_deletion_if_contains_resources = true
    }
  }
  resource_providers_to_register = [
    "Microsoft.ContainerService",
    "Microsoft.Network",
    "Microsoft.Storage",
    "Microsoft.KeyVault",
    "Microsoft.ManagedIdentity",
    "Microsoft.DBforPostgreSQL",
    "Microsoft.DocumentDB",
    "Microsoft.OperationalInsights",
  ]
  storage_use_azuread = true
}
