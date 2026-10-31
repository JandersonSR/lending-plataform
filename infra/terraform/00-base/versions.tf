# Camada 00: a base que as outras camadas pressupõem.
#   - onde o estado remoto mora (Storage Account + container, acesso só por Entra ID)
#   - a identidade que o GitHub Actions usa (OIDC, sem segredo)
# Esta camada guarda o PRÓPRIO estado localmente na primeira vez (ovo e galinha: o backend
# remoto ainda não existe). Depois do primeiro apply, migre: lição 9.5.
terraform {
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
}

provider "azurerm" {
  features {}
  # azurerm 5.x não registra provedores de recurso sozinho: registre só o que esta camada usa.
  resource_providers_to_register = [
    "Microsoft.Storage",
    "Microsoft.ManagedIdentity",
  ]
  # Autentica no Storage do estado por Entra ID, não por chave de conta.
  storage_use_azuread = true
}
