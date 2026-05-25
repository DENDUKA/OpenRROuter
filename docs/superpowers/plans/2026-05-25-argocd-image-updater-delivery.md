# Argo CD Image Updater Delivery Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make Argo CD Image Updater the only automatic writer of deployed image versions for the dev Kubernetes environment.

**Architecture:** GitHub Actions keeps ownership of restore, build, test, and GHCR image publishing. Argo CD Image Updater runs in the cluster, polls `dev-latest` tags from GHCR, writes digest-pinned overrides back to Git, and lets Argo CD sync `orr-services`.

**Tech Stack:** GitHub Actions YAML, GHCR, Argo CD, Argo CD Image Updater, Kubernetes, Kustomize, PowerShell validation commands.

---

## File Structure

- Modify `.github/workflows/build-deploy.yml`: keep `test` and `build-image`; remove the `update-manifests` job so GitHub Actions no longer commits deploy image tags.
- Delete `.github/workflows/cd.yml`: remove the informational `Deploy` workflow because it implies GitHub Actions owns deployment.
- Modify `infra/kustomize/overlays/dev/services/kustomization.yaml`: change all OpenRussiaRouter service baseline image tags to `dev-latest`; keep `f9router` on `dev-latest`.
- Verify `infra/argocd/orr-dev-app.yaml`: no expected edit if annotations already match `ghcr.io/denduka/<service>:dev-latest` and `digest` strategy.
- Modify `docs/argo-k8s-dev-runbook.md`: clean up the container update section so the normal flow describes Image Updater as the only deployment state writer.

## Scope Check

This plan is one cohesive change: removing competing CI manifest writes and documenting the existing Argo CD Image Updater delivery model. It does not add production promotion, new environments, new Argo projects, or secret management changes.

### Task 1: Remove GitHub Actions deploy ownership

**Files:**
- Modify: `.github/workflows/build-deploy.yml`
- Delete: `.github/workflows/cd.yml`

- [ ] **Step 1: Confirm the current competing workflow jobs**

Run:

```powershell
rg -n "update-manifests|Install kustomize|Update image tags|Commit and push|name: Deploy|workflow_run" .github/workflows
```

Expected: matches in `.github/workflows/build-deploy.yml` for `update-manifests` and in `.github/workflows/cd.yml` for the `Deploy` workflow.

- [ ] **Step 2: Remove the `update-manifests` job from `.github/workflows/build-deploy.yml`**

Edit `.github/workflows/build-deploy.yml` so the file ends immediately after the `Build and push` step in the `build-image` job:

```yaml
      - name: Build and push
        if: steps.check.outputs.changed == 'true'
        uses: docker/build-push-action@v6
        with:
          context: .
          file: ${{ matrix.dockerfile }}
          push: true
          tags: |
            ${{ env.REGISTRY }}/${{ env.OWNER }}/${{ matrix.service }}:sha-${{ github.sha }}
            ${{ env.REGISTRY }}/${{ env.OWNER }}/${{ matrix.service }}:dev-latest
          cache-from: type=gha,scope=${{ matrix.service }}
          cache-to: type=gha,mode=max,scope=${{ matrix.service }}
```

Remove this entire job and everything below it:

```yaml
  update-manifests:
    needs: [test, build-image]
    if: github.event_name == 'push' && github.ref == 'refs/heads/main'
    runs-on: ubuntu-latest
    timeout-minutes: 5
    permissions:
      contents: write
```

- [ ] **Step 3: Delete `.github/workflows/cd.yml`**

Remove the file `.github/workflows/cd.yml` from the repository. The removed file currently starts with:

```yaml
name: Deploy

on:
  workflow_run:
    workflows: ["Build Images"]
```

- [ ] **Step 4: Verify CI no longer writes manifests**

Run:

```powershell
rg -n "update-manifests|kustomize edit set image|git commit -m \"chore\\(deploy\\)|git push origin main|name: Deploy|workflow_run" .github/workflows
```

Expected: no output and exit code `1`. If any match remains, remove the remaining deploy-write behavior.

- [ ] **Step 5: Commit**

Run:

```powershell
git add .github/workflows/build-deploy.yml .github/workflows/cd.yml
git commit -m "ci: let argo image updater own deploy tags"
```

Expected: commit succeeds with one modified workflow and one deleted workflow.

### Task 2: Normalize Kustomize baseline image tags

**Files:**
- Modify: `infra/kustomize/overlays/dev/services/kustomization.yaml`
- Verify: `infra/argocd/orr-dev-app.yaml`

- [ ] **Step 1: Inspect current image tags and Image Updater annotations**

Run:

```powershell
rg -n "newTag:|image-list|update-strategy|write-back-method" infra/kustomize/overlays/dev/services/kustomization.yaml infra/argocd/orr-dev-app.yaml
```

Expected: `kustomization.yaml` currently contains some `sha-*` tags, while `orr-dev-app.yaml` references `dev-latest` images with `digest` update strategy.

- [ ] **Step 2: Set baseline service tags to `dev-latest`**

Edit the `images:` section in `infra/kustomize/overlays/dev/services/kustomization.yaml` to exactly this block:

```yaml
images:
- name: ghcr.io/denduka/api-gateway
  newTag: dev-latest
- name: ghcr.io/denduka/auth-service
  newTag: dev-latest
- name: ghcr.io/denduka/billing-service
  newTag: dev-latest
- name: ghcr.io/denduka/control-plane
  newTag: dev-latest
- name: ghcr.io/denduka/webapp
  newTag: dev-latest
- name: ghcr.io/inc64/f9router
  newTag: dev-latest
```

- [ ] **Step 3: Verify Image Updater annotations still match pushed image names**

Run:

```powershell
rg -n "api-gateway=ghcr.io/denduka/api-gateway:dev-latest|auth-service=ghcr.io/denduka/auth-service:dev-latest|billing-service=ghcr.io/denduka/billing-service:dev-latest|control-plane=ghcr.io/denduka/control-plane:dev-latest|webapp=ghcr.io/denduka/webapp:dev-latest|f9router=ghcr.io/inc64/f9router:dev-latest" infra/argocd/orr-dev-app.yaml
```

Expected: one match on the `argocd-image-updater.argoproj.io/image-list` annotation.

- [ ] **Step 4: Verify each managed image uses digest strategy**

Run:

```powershell
rg -n "api-gateway.update-strategy: digest|auth-service.update-strategy: digest|billing-service.update-strategy: digest|control-plane.update-strategy: digest|webapp.update-strategy: digest|f9router.update-strategy: digest|write-back-method: git:secret:argocd/repo-openrussiarouter" infra/argocd/orr-dev-app.yaml
```

Expected: seven matches: six digest strategy annotations and one git write-back annotation.

- [ ] **Step 5: Render the services overlay if `kubectl` is available**

Run:

```powershell
kubectl kustomize infra/kustomize/overlays/dev/services > $env:TEMP\orr-services-rendered.yaml
```

Expected: command exits with code `0`. If `kubectl` is not available on the machine, record that validation was skipped and continue with the `rg` and `git diff --check` validations.

- [ ] **Step 6: Commit**

Run:

```powershell
git add infra/kustomize/overlays/dev/services/kustomization.yaml
git commit -m "deploy: baseline dev images to latest tags"
```

Expected: commit succeeds with only `kustomization.yaml` changed.

### Task 3: Update the Argo runbook

**Files:**
- Modify: `docs/argo-k8s-dev-runbook.md`

- [ ] **Step 1: Locate stale delivery wording**

Run:

```powershell
rg -n "update-manifests|kustomization.yaml|sha-|dev-latest|Image Updater|GitHub Actions|\\.argocd-source|Deploy" docs/argo-k8s-dev-runbook.md
```

Expected: matches in the container update section. Some mojibake may appear in the existing Russian text; preserve the useful commands but replace the stale normal-flow wording.

- [ ] **Step 2: Replace the normal automatic update flow**

In `docs/argo-k8s-dev-runbook.md`, update the automatic container update section so it states this flow:

````markdown
## Container Updates

### Automatic flow

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

GitHub Actions does not update Kubernetes manifests and does not call Argo CD. Image Updater is the only automatic writer of deployed image versions.
````

Do not remove the existing verification commands for pod images, rollout history, Image Updater logs, or Argo CD sync; keep them after the updated flow.

- [ ] **Step 3: Remove stale claims about GitHub Actions committing `kustomization.yaml`**

Delete or rewrite text that says GitHub Actions commits image tags to `infra/kustomize/overlays/dev/services/kustomization.yaml`.

Use this replacement wording where the runbook explains Git write-back:

```markdown
`write-back-method: git` means Image Updater commits Argo CD source overrides to the repository after it resolves a newer digest. Git remains the audit log for the exact image digest selected by the cluster-side updater.
```

- [ ] **Step 4: Verify the runbook no longer describes CI manifest writes as normal flow**

Run:

```powershell
rg -n "GitHub Actions.*commit|Actions.*kustomization|update-manifests|kustomize edit set image|commits image tags" docs/argo-k8s-dev-runbook.md
```

Expected: no output and exit code `1`.

- [ ] **Step 5: Commit**

Run:

```powershell
git add docs/argo-k8s-dev-runbook.md
git commit -m "docs: document image updater delivery flow"
```

Expected: commit succeeds with only the runbook changed.

### Task 4: Final validation

**Files:**
- Verify: `.github/workflows/build-deploy.yml`
- Verify: `infra/kustomize/overlays/dev/services/kustomization.yaml`
- Verify: `infra/argocd/orr-dev-app.yaml`
- Verify: `docs/argo-k8s-dev-runbook.md`

- [ ] **Step 1: Check for whitespace and patch formatting issues**

Run:

```powershell
git diff --check HEAD~3..HEAD
```

Expected: no output and exit code `0`.

- [ ] **Step 2: Confirm there is one automatic image-state writer**

Run:

```powershell
rg -n "kustomize edit set image|update-manifests|git push origin main|workflow_run|name: Deploy" .github infra docs
```

Expected: no output from `.github`. Matches in `infra` are acceptable only for Argo CD Image Updater configuration. Matches in `docs` are acceptable only if they describe Image Updater ownership, not GitHub Actions deploy ownership.

- [ ] **Step 3: Confirm baseline tags and Image Updater annotations**

Run:

```powershell
rg -n "newTag: dev-latest|write-back-method: git:secret:argocd/repo-openrussiarouter|update-strategy: digest" infra/kustomize/overlays/dev/services/kustomization.yaml infra/argocd/orr-dev-app.yaml
```

Expected: six `newTag: dev-latest` matches in `kustomization.yaml`, six `update-strategy: digest` matches in `orr-dev-app.yaml`, and one `write-back-method` match.

- [ ] **Step 4: Build the solution to ensure unrelated project state is healthy**

Run:

```powershell
dotnet restore OpenRussiaRouter.sln --configfile NuGet.Config -nr:false -m:1
dotnet build OpenRussiaRouter.sln --no-restore -nr:false -m:1
```

Expected: both commands exit with code `0`. This change does not alter application code, but the final branch should still be buildable.

- [ ] **Step 5: Check working tree**

Run:

```powershell
git status --short
```

Expected: no output.

## Self-Review Notes

- Spec coverage: Task 1 removes GitHub Actions manifest writes and the misleading deploy workflow. Task 2 establishes `dev-latest` baselines while preserving Image Updater annotations. Task 3 updates operator documentation. Task 4 validates the final ownership model.
- Placeholder scan: this plan contains no unresolved placeholder markers or undefined follow-up work.
- Type and path consistency: all paths match the current repository layout under `C:\Programs\OpenRussiaRouter`.
