# Argo CD + kind dev setup

This runbook recreates the local dev Kubernetes cluster, exposes Argo CD and Seq on localhost, configures Argo CD applications, and enables private GitHub/GHCR access.

## Endpoints

- Argo CD: `https://localhost:9090/`
- Seq: `http://localhost:18081/`

## Prerequisites

- Docker running.
- `kind`, `kubectl`, and `curl` available in PATH.
- GitHub PAT with access to `DENDUKA/OpenRussiaRouter`.
- For private GHCR images, the same PAT must be able to read packages.

Recommended PAT permissions:

- Repository `DENDUKA/OpenRussiaRouter`: `Contents: Read and write` (обязательно — Image Updater коммитит `.argocd-source-orr-services.yaml` с digest'ами образов).
- Packages/GHCR: read access to required package images.

## 1. Recreate kind cluster

This deletes all Kubernetes state in the local cluster, including PVC data.

```powershell
kind delete cluster --name orr-dev
kind create cluster --config infra/kind/orr-dev.yaml
```

`infra/kind/orr-dev.yaml` maps host localhost ports to Kubernetes NodePorts:

- `localhost:9090` -> node `30090` -> Argo CD HTTPS
- `localhost:18081` -> node `30081` -> Seq UI

## 2. Install ingress-nginx

```powershell
kubectl apply -f https://raw.githubusercontent.com/kubernetes/ingress-nginx/controller-v1.14.1/deploy/static/provider/kind/deploy.yaml
kubectl wait --namespace ingress-nginx --for=condition=ready pod --selector=app.kubernetes.io/component=controller --timeout=300s
```

## 3. Install Argo CD

Client-side apply can fail on large CRD annotations. Use server-side apply if needed.

```powershell
kubectl create namespace argocd
kubectl apply -n argocd -f https://raw.githubusercontent.com/argoproj/argo-cd/stable/manifests/install.yaml
```

If CRD annotation size fails:

```powershell
kubectl apply --server-side -n argocd -f https://raw.githubusercontent.com/argoproj/argo-cd/stable/manifests/install.yaml
```

Wait for Argo CD:

```powershell
kubectl wait --namespace argocd --for=condition=available deployment/argocd-server --timeout=300s
```

## 4. Expose Argo CD on localhost

```powershell
kubectl apply -f infra/argocd/argocd-server-nodeport.yaml
```

Verify:

```powershell
curl.exe -k https://localhost:9090/
```

Expected HTTP status:

```text
200
```

## 5. Set fixed Argo CD admin password

Get the auto-generated initial password, then replace it with the fixed dev password:

```powershell
$encoded = kubectl -n argocd get secret argocd-initial-admin-secret -o jsonpath='{.data.password}'
$initialPassword = [System.Text.Encoding]::UTF8.GetString([System.Convert]::FromBase64String($encoded))

argocd login localhost:9090 --insecure --username admin --password $initialPassword
argocd account update-password --current-password $initialPassword --new-password password
```

Login:

- User: `admin`
- Password: `password`

## 6. Configure Argo CD Git repository auth

Set the token in a PowerShell variable without printing it:

```powershell
$token = Read-Host -AsSecureString "GitHub PAT"
$plainToken = [Runtime.InteropServices.Marshal]::PtrToStringAuto([Runtime.InteropServices.Marshal]::SecureStringToBSTR($token))
```

Create Argo CD repository secret:

```powershell
@"
apiVersion: v1
kind: Secret
metadata:
  name: repo-openrussiarouter
  namespace: argocd
  labels:
    argocd.argoproj.io/secret-type: repository
stringData:
  type: git
  url: https://github.com/DENDUKA/OpenRussiaRouter.git
  username: x-access-token
  password: $plainToken
"@ | kubectl apply -f -
```

## 7. Configure GHCR pull secrets

Create GHCR pull secrets for workload image pulls and image updater registry access:

```powershell
kubectl -n orr-dev create secret docker-registry ghcr-pull-secret `
  --docker-server=ghcr.io `
  --docker-username=DENDUKA `
  --docker-password=$plainToken `
  --dry-run=client -o yaml | kubectl apply -f -

kubectl -n argocd create secret docker-registry ghcr-pull-secret `
  --docker-server=ghcr.io `
  --docker-username=DENDUKA `
  --docker-password=$plainToken `
  --dry-run=client -o yaml | kubectl apply -f -
```

Attach the pull secret to the default ServiceAccount in `orr-dev`:

```powershell
@"
apiVersion: v1
kind: ServiceAccount
metadata:
  name: default
  namespace: orr-dev
imagePullSecrets:
  - name: ghcr-pull-secret
"@ | kubectl apply -f -
```

## 8. Apply ORR infra and services

```powershell
kubectl apply -k infra/kustomize/overlays/dev/infra
kubectl apply -k infra/kustomize/overlays/dev/services
```

If pods were created before `imagePullSecrets` were attached, restart them:

```powershell
kubectl delete pod -n orr-dev -l app=api-gateway
kubectl delete pod -n orr-dev -l app=auth-service
kubectl delete pod -n orr-dev -l app=billing-service
kubectl delete pod -n orr-dev -l app=control-plane
kubectl delete pod -n orr-dev -l app=webapp
```

## 9. Expose Seq on localhost

Seq NodePort is managed by the dev infra kustomize overlay:

```powershell
kubectl apply -k infra/kustomize/overlays/dev/infra
```

Verify:

```powershell
curl.exe http://localhost:18081/
```

Expected HTTP status:

```text
200
```

## 10. Bootstrap Argo CD applications

```powershell
kubectl apply -f infra/argocd/orr-dev-app.yaml
kubectl apply -f infra/argocd/orr-image-updater-app.yaml
```

Force refresh after repo credentials are added:

```powershell
kubectl -n argocd annotate application orr-infra argocd.argoproj.io/refresh=hard --overwrite
kubectl -n argocd annotate application orr-services argocd.argoproj.io/refresh=hard --overwrite
kubectl -n argocd annotate application orr-argocd-image-updater argocd.argoproj.io/refresh=hard --overwrite
```

## 11. Install CRDs for Argo CD controllers

The dev deployment pins Argo CD Image Updater to `v1.2.0`. v1.x requires the `ImageUpdater` CRD; the repo then creates `infra/argocd-image-updater/image-updater.yaml`, which selects `orr-services` and reads its existing image updater annotations.

If Argo CD was applied incompletely, the ApplicationSet controller can also crash because `applicationsets.argoproj.io` is missing. Re-apply both CRDs before syncing the image updater app:

```powershell
kubectl apply --server-side -f https://raw.githubusercontent.com/argoproj/argo-cd/stable/manifests/crds/applicationset-crd.yaml
kubectl apply --server-side -f https://raw.githubusercontent.com/argoproj-labs/argocd-image-updater/master/config/crd/bases/argocd-image-updater.argoproj.io_imageupdaters.yaml
```

If the image updater was already crashing before the CRD existed, restart it after the GitOps app syncs:

```powershell
kubectl delete pod -n argocd -l control-plane=argocd-image-updater-controller
kubectl wait --namespace argocd --for=condition=available deployment/argocd-image-updater-controller --timeout=180s
```

## 12. Verify final state

Applications:

```powershell
kubectl get applications -n argocd
```

Expected:

```text
orr-argocd-image-updater   Synced   Healthy
orr-infra                  Synced   Healthy
orr-services               Synced   Healthy
```

Pods:

```powershell
kubectl get pods -A
```

Expected:

- All `argocd` pods: `Running` and ready.
- `ingress-nginx-controller`: `Running` and ready.
- All `orr-dev` pods: `Running` and ready.
- Core `kube-system` pods: `Running`.

Localhost endpoints:

```powershell
curl.exe -k -s -o NUL -w "%{http_code}" https://localhost:9090/
curl.exe -s -o NUL -w "%{http_code}" http://localhost:18081/
```

Expected:

```text
200
200
```

## Troubleshooting

### Argo apps are empty

Cause: bootstrap Application manifests were not applied after cluster recreation.

Fix:

```powershell
kubectl apply -f infra/argocd/orr-dev-app.yaml
kubectl apply -f infra/argocd/orr-image-updater-app.yaml
```

### Argo app status is Unknown with repository auth error

Error:

```text
authentication required: Repository not found
```

Cause: Argo CD repo credentials are missing or invalid.

Fix: recreate `repo-openrussiarouter` secret in `argocd` namespace.

### ORR pods are ImagePullBackOff

Cause: `orr-dev` namespace cannot pull private GHCR images.

Fix:

```powershell
kubectl get secret -n orr-dev ghcr-pull-secret
kubectl get serviceaccount -n orr-dev default -o yaml
```

If missing, recreate GHCR pull secret and attach it to default ServiceAccount, then delete failed pods.

### Argo CD Image Updater CrashLoopBackOff

Check the CRD and deployed image:

```powershell
kubectl get crd imageupdaters.argocd-image-updater.argoproj.io
kubectl get deployment -n argocd argocd-image-updater-controller -o jsonpath='{.spec.template.spec.containers[0].image}'
```

Expected:

```text
quay.io/argoprojlabs/argocd-image-updater:v1.2.0
```

If the CRD is missing, apply it with the command from step 11 and restart the image updater pod.

### localhost ports do not work after `kind create cluster`

Check Docker port mappings:

```powershell
docker port orr-dev-control-plane
```

Expected:

```text
30081/tcp -> 0.0.0.0:18081
30090/tcp -> 0.0.0.0:9090
```

If missing, cluster was not created with `infra/kind/orr-dev.yaml`. Recreate it with that config.

## Обновление контейнеров

### Автоматический поток

```text
git push -> main
  -> GitHub Actions: restore, build, test
  -> GitHub Actions: build changed images
  -> GHCR:
     ghcr.io/denduka/<service>:sha-<commit>
     ghcr.io/denduka/<service>:dev-latest
  -> Argo CD Image Updater polls GHCR every 2 minutes
  -> Image Updater resolves dev-latest to a digest
  -> Image Updater commits the digest override to Git
  -> Argo CD syncs orr-services
  -> Kubernetes rolls the changed deployments
```

Образы собираются только при push в `main`, не в PR. Сервисные изменения пересобирают только затронутые сервисы; общие изменения в `BuildingBlocks`, `Contracts` или solution-файлах пересобирают все пять сервисов.

GitHub Actions не обновляет Kubernetes manifests и не вызывает Argo CD. Image Updater является единственным автоматическим writer'ом версий образов для деплоя.

`write-back-method: git` означает, что Image Updater коммитит Argo CD source overrides в репозиторий после выбора нового digest. Git остаётся audit log'ом точного image digest, выбранного cluster-side updater'ом.

Для работы write-back PAT в секрете `argocd/repo-openrussiarouter` должен иметь `Contents: Read and write` на репозиторий.

### Проверить, что обновление применилось

```powershell
# Текущий image digest подов
kubectl get pods -n orr-dev -o jsonpath='{range .items[*]}{.metadata.name}{"  "}{.spec.containers[0].image}{"\n"}{end}'

# История rollout конкретного сервиса
kubectl rollout history deployment/api-gateway -n orr-dev
```

### Принудительный запуск Image Updater

Если не хочется ждать 2 минуты:

```powershell
kubectl delete pod -n argocd -l control-plane=argocd-image-updater-controller
kubectl wait --namespace argocd --for=condition=ready pod -l control-plane=argocd-image-updater-controller --timeout=60s
```

Логи Image Updater:

```powershell
kubectl logs -n argocd -l control-plane=argocd-image-updater-controller --tail=50
```

### Принудительный sync Argo CD

Если Image Updater обновил параметры, но Argo CD ещё не засинхронизировался:

```powershell
kubectl -n argocd annotate application orr-services argocd.argoproj.io/refresh=hard --overwrite
```

Или через CLI:

```powershell
argocd app sync orr-services --force
```

### Ручное обновление образа

Используется для экстренного деплоя или тестирования конкретного SHA. Это временный обход GitOps-потока: следующий цикл Image Updater снова применит digest из registry и Git write-back.

```powershell
# Например, откатиться к предыдущему образу api-gateway
kubectl set image deployment/api-gateway api-gateway=ghcr.io/denduka/api-gateway:sha-<commit> -n orr-dev

# Вернуть управление Image Updater
kubectl delete pod -n argocd -l control-plane=argocd-image-updater-controller
```

### Откат деплоя

```powershell
# Откат на предыдущую ревизию Kubernetes
kubectl rollout undo deployment/api-gateway -n orr-dev

# Проверить статус после отката
kubectl rollout status deployment/api-gateway -n orr-dev
```

После ручного отката Image Updater при следующем цикле опроса снова подтянет последний digest из registry и перезапишет ручное изменение. Чтобы заморозить версию, временно удалите аннотацию `argocd-image-updater.argoproj.io/image-list` из Argo CD Application:

```powershell
kubectl -n argocd patch application orr-services --type=json \
  -p='[{"op":"remove","path":"/metadata/annotations/argocd-image-updater.argoproj.io~1image-list"}]'
```

Восстановить аннотацию:

```powershell
kubectl apply -f infra/argocd/orr-dev-app.yaml
```

### Обновление f9router

`f9router` (`ghcr.io/inc64/f9router:dev-latest`) управляется тем же Image Updater. Новый digest на стороне `inc64` автоматически подхватывается при следующем цикле опроса.

Если нужно обновить вручную:

```powershell
kubectl set image deployment/f9router f9router=ghcr.io/inc64/f9router:dev-latest -n orr-dev
kubectl rollout status deployment/f9router -n orr-dev
```

### Диагностика зависшего обновления

```powershell
# Статус деплоя
kubectl rollout status deployment/<service> -n orr-dev --timeout=120s

# Описание пода с причиной ошибки
kubectl describe pod -n orr-dev -l app=<service>

# Логи нового пода
kubectl logs -n orr-dev -l app=<service> --tail=100

# Проверить ImagePullBackOff
kubectl get events -n orr-dev --sort-by='.lastTimestamp' | tail -20
```

Типичные причины:
- `ImagePullBackOff` - истёк PAT или секрет `ghcr-pull-secret` не привязан к ServiceAccount, см. шаг 7.
- `CrashLoopBackOff` - новый контейнер падает; см. логи пода.
- Argo CD в `OutOfSync` после обновления Image Updater - вручную нажать Sync или запустить `argocd app sync orr-services`.

## Security notes

- Do not commit PATs or plaintext token files.
- Delete any temporary local PAT file after use.
- Prefer fine-grained PATs with minimum repository/package permissions.
