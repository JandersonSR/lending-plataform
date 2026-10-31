variable "ambiente" {
  description = "dev ou prod."
  type        = string
  validation {
    condition     = contains(["dev", "prod"], var.ambiente)
    error_message = "ambiente deve ser dev ou prod."
  }
}

variable "local" {
  type    = string
  default = "brazilsouth"
}

variable "prefixo" {
  type    = string
  default = "creditflow"
}

variable "zonas" {
  description = "Zonas de disponibilidade usadas pelos nós e pelo HA do Postgres. [] = sem zona (mais barato, sem tolerância a zona)."
  type        = list(string)
  default     = ["1", "2", "3"]
}

# ----------------------------- rede -----------------------------
variable "espaco_vnet" {
  type    = string
  default = "10.40.0.0/16"
}

variable "saida_por_nat" {
  description = "Saída para a internet por NAT Gateway (IP de saída fixo, sem esgotar portas SNAT do LB)."
  type        = bool
  default     = false
}

# ----------------------------- AKS ------------------------------
variable "versao_kubernetes" {
  description = "Versão menor; o AKS escolhe o patch GA mais novo. Confira com: az aks get-versions -l brazilsouth -o table"
  type        = string
  default     = "1.36"
}

variable "aks_sku" {
  description = "Free (sem SLA do control plane) ou Standard (SLA 99,95% com zonas)."
  type        = string
  default     = "Free"
}

variable "pool_sistema" {
  type = object({
    vm_size = string
    minimo  = number
    maximo  = number
  })
  default = { vm_size = "Standard_D2ads_v5", minimo = 1, maximo = 3 }
}

variable "pool_apps" {
  type = object({
    vm_size = string
    minimo  = number
    maximo  = number
    spot    = bool
  })
  default = { vm_size = "Standard_D4ads_v5", minimo = 1, maximo = 5, spot = false }
}

variable "administradores_aks" {
  description = "Object IDs (usuários ou grupos do Entra ID) com cluster-admin via Azure RBAC."
  type        = list(string)
  default     = []
}

variable "principal_ci" {
  description = "principal_id da identidade do GitHub Actions (saída github_principal_id da 00-base)."
  type        = string
  default     = ""
}

# --------------------------- Postgres ---------------------------
variable "postgres" {
  type = object({
    sku              = string
    versao           = string
    armazenamento_mb = number
    ha               = bool
    backup_dias      = number
    backup_geo       = bool
  })
  default = {
    sku              = "B_Standard_B1ms"
    versao           = "17"
    armazenamento_mb = 32768
    ha               = false
    backup_dias      = 7
    backup_geo       = false
  }
}

# ---------------------- MongoDB (DocumentDB) --------------------
variable "documentdb" {
  description = "Azure DocumentDB (antigo Cosmos DB for MongoDB vCore). Ver docs/adr/0010-mongodb-na-azure.md."
  type = object({
    tier             = string
    ha               = bool
    armazenamento_gb = number
  })
  default = { tier = "M10", ha = false, armazenamento_gb = 32 }
}

# ----------------------------- senhas ---------------------------
variable "versao_senhas" {
  description = "Incremente para gerar e gravar senhas novas (Postgres + Key Vault) no próximo apply."
  type        = number
  default     = 1
}

variable "tags" {
  type    = map(string)
  default = {}
}
