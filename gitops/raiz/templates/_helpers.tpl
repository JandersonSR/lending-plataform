{{/* Política de sincronização comum: automática, poda o que sai do Git, desfaz mudança manual. */}}
{{- define "raiz.sync" -}}
syncPolicy:
  automated:
    prune: true
    selfHeal: true
  syncOptions:
    - CreateNamespace={{ .criarNs | default false }}
    - ServerSideApply=true
  retry:
    limit: 10
    backoff:
      duration: 15s
      factor: 2
      maxDuration: 5m
{{- end -}}

{{- define "raiz.meta" -}}
namespace: argocd
annotations:
  argocd.argoproj.io/sync-wave: {{ .onda | quote }}
finalizers:
  - resources-finalizer.argocd.argoproj.io
{{- end -}}
