{{/*
Funções reaproveitadas pelos templates. Lição 7.2 explica cada uma.
*/}}

{{- define "creditflow.nome" -}}
{{- .Release.Name | trunc 40 | trimSuffix "-" -}}
{{- end -}}

{{/* Nome de um recurso de perfil: <release>-<perfil> (ex.: creditflow-api-simulacao). */}}
{{- define "creditflow.nomePerfil" -}}
{{- printf "%s-%s" (include "creditflow.nome" .raiz) .perfil | trunc 63 | trimSuffix "-" -}}
{{- end -}}

{{- define "creditflow.rotulosComuns" -}}
app.kubernetes.io/name: creditflow
app.kubernetes.io/instance: {{ .Release.Name }}
app.kubernetes.io/version: {{ .Chart.AppVersion | quote }}
app.kubernetes.io/managed-by: {{ .Release.Service }}
app.kubernetes.io/part-of: creditflow
helm.sh/chart: {{ printf "%s-%s" .Chart.Name .Chart.Version | replace "+" "_" }}
{{- end -}}

{{/* Seletor de um perfil. NUNCA mude depois do primeiro deploy: o seletor do Deployment é imutável. */}}
{{- define "creditflow.seletorPerfil" -}}
app.kubernetes.io/name: creditflow
app.kubernetes.io/instance: {{ .raiz.Release.Name }}
app.kubernetes.io/component: {{ .perfil }}
{{- end -}}

{{- define "creditflow.rotulosPerfil" -}}
{{ include "creditflow.rotulosComuns" .raiz }}
app.kubernetes.io/component: {{ .perfil }}
creditflow.io/papel: {{ .cfg.papel }}
{{- end -}}

{{/* Referência da imagem: digest ganha da tag; sem os dois, usa o appVersion. */}}
{{- define "creditflow.imagem" -}}
{{- $img := .Values.imagem -}}
{{- if $img.digest -}}
{{- printf "%s@%s" $img.repositorio $img.digest -}}
{{- else -}}
{{- printf "%s:%s" $img.repositorio (default .Chart.AppVersion $img.tag) -}}
{{- end -}}
{{- end -}}

{{/* Nome do Secret materializado para um item de segredos. */}}
{{- define "creditflow.nomeSegredo" -}}
{{- printf "%s-%s" (include "creditflow.nome" .raiz) .item | trunc 63 | trimSuffix "-" -}}
{{- end -}}

{{/* Lista de módulos como texto separado por vírgula (formato de CREDITFLOW__MODULOS). */}}
{{- define "creditflow.modulos" -}}
{{- join "," . -}}
{{- end -}}

{{/* Sonda HTTP a partir de { caminho, periodSeconds, failureThreshold, timeoutSeconds }. */}}
{{- define "creditflow.sonda" -}}
httpGet:
  path: {{ .caminho }}
  port: http
periodSeconds: {{ .periodSeconds | default 10 }}
failureThreshold: {{ .failureThreshold | default 3 }}
timeoutSeconds: {{ .timeoutSeconds | default 2 }}
{{- end -}}
