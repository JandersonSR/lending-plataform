variable "local" {
  description = "Região Azure."
  type        = string
  default     = "brazilsouth"
}

variable "prefixo" {
  description = "Prefixo curto dos nomes (minúsculas e números)."
  type        = string
  default     = "creditflow"

  validation {
    condition     = can(regex("^[a-z0-9]{3,12}$", var.prefixo))
    error_message = "Use de 3 a 12 letras minúsculas ou números."
  }
}

variable "repositorio_github" {
  description = "dono/repositório do GitHub autorizado a usar a identidade de CI."
  type        = string
  default     = "JandersonSR/lending-plataform"
}

variable "ambientes" {
  description = "Ambientes do GitHub (Settings > Environments) que fazem terraform apply."
  type        = list(string)
  default     = ["dev", "prod"]
}
