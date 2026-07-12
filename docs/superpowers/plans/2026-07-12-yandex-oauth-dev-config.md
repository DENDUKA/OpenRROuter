# Yandex OAuth Dev Configuration Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Enable the Yandex OAuth button in the `kind-orr-dev` WebApp served at `http://localhost/login`.

**Architecture:** A focused Kustomize patch adds the two test OAuth settings directly to the `webapp` deployment. The dev services overlay includes that patch so Argo CD owns the configuration and does not revert it.

**Tech Stack:** Kubernetes, Kustomize, Argo CD, ASP.NET Core configuration

---

### Task 1: Add Yandex OAuth settings to the dev overlay

**Files:**
- Create: `infra/kustomize/overlays/dev/services/webapp-yandex-patch.yaml`
- Modify: `infra/kustomize/overlays/dev/services/kustomization.yaml:13-23`

- [ ] **Step 1: Create the WebApp patch**

```yaml
apiVersion: apps/v1
kind: Deployment
metadata:
  name: webapp
  namespace: orr-dev
spec:
  template:
    spec:
      containers:
        - name: webapp
          env:
            - name: Yandex__ClientId
              value: "6ebafd3a0e7741c1aa0b8dee535a16fc"
            - name: Yandex__ClientSecret
              value: "e589d64f5f8f41f6b3131c003ada1a3f"
```

- [ ] **Step 2: Include the patch in the overlay**

Add under `patches:` in `infra/kustomize/overlays/dev/services/kustomization.yaml`:

```yaml
- path: webapp-yandex-patch.yaml
```

- [ ] **Step 3: Render and validate the overlay**

Run:

```powershell
kubectl kustomize infra/kustomize/overlays/dev/services | Select-String 'Yandex__Client(Id|Secret)'
```

Expected: output contains `Yandex__ClientId` and `Yandex__ClientSecret`.

### Task 2: Apply and smoke-test the configuration

**Files:**
- No repository files changed.

- [ ] **Step 1: Apply the dev overlay**

```powershell
kubectl apply -k infra/kustomize/overlays/dev/services
```

Expected: Kubernetes accepts the resources and reports them as configured or unchanged.

- [ ] **Step 2: Wait for WebApp rollout**

```powershell
kubectl rollout status deployment/webapp -n orr-dev --timeout=120s
```

Expected: `deployment "webapp" successfully rolled out`.

- [ ] **Step 3: Verify the live deployment settings without reading their values**

```powershell
kubectl get deployment webapp -n orr-dev -o json | ConvertFrom-Json | ForEach-Object { $_.spec.template.spec.containers | Where-Object name -eq 'webapp' | ForEach-Object env | Where-Object name -like 'Yandex__*' | Select-Object -ExpandProperty name }
```

Expected: `Yandex__ClientId` and `Yandex__ClientSecret`.

- [ ] **Step 4: Verify the login page**

Open `http://localhost/login` and inspect the rendered DOM.

Expected: a unique button named `Войти через Яндекс` is visible.

- [ ] **Step 5: Commit the configuration**

```powershell
git add infra/kustomize/overlays/dev/services/webapp-yandex-patch.yaml infra/kustomize/overlays/dev/services/kustomization.yaml docs/superpowers/plans/2026-07-12-yandex-oauth-dev-config.md
git commit -m "feat: configure Yandex OAuth in dev"
```
