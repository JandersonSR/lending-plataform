# Camada 30: a "ponte" entre o Terraform e o GitOps. Cria duas coisas e para:
#   1) o Secret de cluster do Argo CD, com os dados da Azure em anotações (o Git não sabe
#      client ids, FQDN do Postgres nem URL do cofre; o Terraform sabe);
#   2) a Application raiz (app-of-apps) apontando para gitops/raiz.
# Daí em diante, TODO o resto (operadores do Argo, CRs, aplicação) vem do Git.
# Separada da 20 porque kubernetes_manifest precisa da CRD Application já instalada no PLAN.
terraform {
  required_version = ">= 1.12.0"
  required_providers {
    azurerm    = { source = "hashicorp/azurerm", version = "~> 5.12" }
    kubernetes = { source = "hashicorp/kubernetes", version = "~> 3.4" }
  }
  backend "azurerm" {}
}

provider "azurerm" {
  features {}
  resource_provider_registrations = "none"
  storage_use_azuread             = true
}

data "terraform_remote_state" "plataforma" {
  backend = "azurerm"
  config = {
    resource_group_name  = var.estado.grupo_recursos
    storage_account_name = var.estado.conta
    container_name       = "tfstate"
    key                  = "20-plataforma/${var.ambiente}.tfstate"
    use_azuread_auth     = true
  }
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

