variable "ambiente" { type = string }

variable "estado" {
  description = "Onde está o estado remoto (saída backend_hcl da 00-base)."
  type = object({
    grupo_recursos = string
    conta          = string
  })
}

variable "dominio" {
  description = "Domínio base (api.<dominio>, auth.<dominio>, harbor.<dominio>). Vazio = <ip>.nip.io (laboratório)."
  type        = string
  default     = ""
}

# Versões de TUDO o que esta camada instala, num lugar só. O deploy/k8s/local/subir.sh copia daqui.
variable "versoes" {
  type = map(string)
  default = {
    envoy_gateway      = "v1.9.3"
    cert_manager       = "v1.21.3"
    external_secrets   = "2.14.0"
    strimzi            = "1.3.0"
    keda               = "2.21.0"
    dragonfly_operator = "v1.7.1"
    harbor             = "1.19.4" # Harbor 2.15.4
    argo_cd            = "10.11.6" # Argo CD v3.5.6
    policy_controller  = "0.10.8"
  }
}
