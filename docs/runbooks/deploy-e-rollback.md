# Runbook: deploy com problema e rollback

## Sintomas
Pods novos em `CrashLoopBackOff`, `ImagePullBackOff`, `CreateContainerConfigError`, readiness falhando; Argo CD `Degraded`; alerta `PodsReiniciando`; SLO queimando logo depois de um merge no gitops.

## 1. Estancar: voltar a versão (2 min)
O rolling update com `maxUnavailable: 0` mantém os pods antigos enquanto os novos não ficam prontos: muitas vezes nada caiu. Mesmo assim, volte:
```bash
git revert <commit "deploy(<amb>): creditflow X">   # o commit do bot no gitops
git push   # (ou PR, se a branch é protegida)
```
O Argo CD sincroniza a versão anterior. **Não** use `kubectl rollout undo` nem "Rollback" na UI com `selfHeal` ligado: o Argo CD reaplica o que está no Git segundos depois.

## 2. Diagnosticar
```bash
kubectl -n creditflow get pods -l app.kubernetes.io/instance=creditflow
kubectl -n creditflow describe pod <pod> | sed -n '/Events/,$p'
kubectl -n creditflow logs <pod> --previous
```
| Estado | Causa provável |
|---|---|
| `ImagePullBackOff` | digest inexistente; robô de pull sem permissão (`harbor-pull`); Harbor fora; **imagem barrada** pelo Harbor (vulnerabilidade) |
| recusado na admissão (`admission webhook "policy.sigstore.dev" denied`) | imagem sem assinatura do CI da main (lição 10.4) |
| `CreateContainerConfigError` | Secret que o pod referencia não existe: ExternalSecret com erro (`kubectl get externalsecret -n creditflow`) |
| `CrashLoopBackOff` | exceção na subida: módulo desconhecido em `CREDITFLOW__MODULOS`, configuração faltando |
| Pronto nunca | readiness falhando: dependência (Postgres, Mongo) inalcançável |
| `OOMKilled` | limite de memória baixo para a carga; heap do .NET + buffers do librdkafka |

## 3. Argo CD não sincroniza
```bash
kubectl -n argocd get applications
kubectl -n argocd get application creditflow-<amb> -o jsonpath='{.status.operationState.message}'; echo
```
`ComparisonError` = o chart não renderiza com aqueles valores (rode `helm template` local com o mesmo values). `OutOfSync` eterno num campo = falta `ignoreDifferences`.
