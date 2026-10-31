variable "ambiente" { type = string }

variable "estado" {
  type = object({
    grupo_recursos = string
    conta          = string
  })
}

variable "repositorio" {
  type    = string
  default = "https://github.com/JandersonSR/lending-plataform.git"
}

variable "revisao" {
  description = "Branch que o Argo CD acompanha."
  type        = string
  default     = "main"
}

variable "email_acme" {
  description = "E-mail da conta no Let's Encrypt (avisos de expiração)."
  type        = string
}

variable "acme_producao" {
  description = "false = servidor de staging do Let's Encrypt (certificado não confiável, sem limite apertado)."
  type        = bool
  default     = false
}
