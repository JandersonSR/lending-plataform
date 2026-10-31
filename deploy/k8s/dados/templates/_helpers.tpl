{{/* Uma regra de ACL do Strimzi: { tipo, nome, padrao?, ops } */}}
{{- define "dados.acl" -}}
- resource: { type: {{ .tipo }}, name: {{ .nome | quote }}, patternType: {{ .padrao | default "literal" }} }
  operations: {{ toJson .ops }}
  host: "*"
{{- end }}
