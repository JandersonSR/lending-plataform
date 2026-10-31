# Camada 20: o que roda DENTRO do cluster e não é a aplicação. Operadores e serviços de
# plataforma que precisam existir antes do Argo CD sincronizar qualquer coisa (CRDs,
# webhooks, o próprio Argo CD), e os que dependem de dados da Azure (Harbor, Key Vault).
terraform {
  required_version = ">= 1.12.0"
  required_providers {
    azurerm    = { source = "hashicorp/azurerm", version = "~> 5.12" }
    helm       = { source = "hashicorp/helm", version = "~> 3.3" }
    kubernetes = { source = "hashicorp/kubernetes", version = "~> 3.4" }
  }
  backend "azurerm" {}
}

provider "azurerm" {
  features {}
  resource_provider_registrations = "none"
  storage_use_azuread             = true
}

data "terraform_remote_state" "azure" {
  backend = "azurerm"
  config = {
    resource_group_name  = var.estado.grupo_recursos
    storage_account_name = var.estado.conta
    container_name       = "tfstate"
    key                  = "10-azure/${var.ambiente}.tfstate"
    use_azuread_auth     = true
  }
}

locals {
  azure = data.terraform_remote_state.azure.outputs
  # O cluster não tem conta local: o token vem do Entra ID pelo kubelogin (az login antes,
  # ou azure/login no GitHub Actions). 6dae42f8-... é o "server id" fixo do AKS no Entra ID.
  exec_kubelogin = {
    api_version = "client.authentication.k8s.io/v1beta1"
    command     = "kubelogin"
    args        = ["get-token", "--login", "azurecli", "--server-id", "6dae42f8-4368-4678-94ff-3960e28e3630"]
  }
}

provider "kubernetes" {
  host                   = local.azure.aks_host
  cluster_ca_certificate = base64decode(local.azure.aks_ca)
  exec {
    api_version = local.exec_kubelogin.api_version
    command     = local.exec_kubelogin.command
    args        = local.exec_kubelogin.args
  }
}

provider "helm" {
  kubernetes = {
    host                   = local.azure.aks_host
    cluster_ca_certificate = base64decode(local.azure.aks_ca)
    exec                   = local.exec_kubelogin
  }
}
