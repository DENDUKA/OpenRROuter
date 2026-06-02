#!/usr/bin/env pwsh
param(
    [Parameter(ValueFromRemainingArguments)]
    [string[]] $Services,

    [string] $PushFrom = "",
    [string] $PushTo   = ""
)

$ErrorActionPreference = "Stop"

$REGISTRY = "ghcr.io"
$OWNER    = "denduka"
$SHA      = git rev-parse --short HEAD

$ALL_SERVICES = [ordered]@{
    "api-gateway"     = "src/OpenRussiaRouter.ApiGateway/Dockerfile"
    "auth-service"    = "src/OpenRussiaRouter.AuthService/Dockerfile"
    "billing-service" = "src/OpenRussiaRouter.BillingService/Dockerfile"
    "control-plane"   = "src/OpenRussiaRouter.ControlPlane/Dockerfile"
    "collector"       = "src/OpenRussiaRouter.CollectorService/Dockerfile"
    "webapp"          = "src/OpenRussiaRouter.WebApp/Dockerfile"
}

$GLOBAL_PATTERNS = @(
    '^\.github/',
    '^OpenRussiaRouter\.sln$',
    '^NuGet\.Config$',
    '^global\.json$',
    '^Directory\.Build\.props$',
    '^Directory\.Packages\.props$',
    '^src/OpenRussiaRouter\.BuildingBlocks/',
    '^src/OpenRussiaRouter\.Contracts/'
)

$SERVICE_PATTERNS = [ordered]@{
    "api-gateway"     = '^src/OpenRussiaRouter\.ApiGateway/'
    "auth-service"    = '^src/OpenRussiaRouter\.AuthService/'
    "billing-service" = '^src/OpenRussiaRouter\.BillingService/'
    "control-plane"   = '^src/OpenRussiaRouter\.ControlPlane/'
    "collector"       = '^src/OpenRussiaRouter\.CollectorService/'
    "webapp"          = '^src/OpenRussiaRouter\.WebApp/'
}

function Get-ChangedServices([string]$From, [string]$To) {
    $zero = "0000000000000000000000000000000000000000"
    if ([string]::IsNullOrEmpty($From) -or $From -eq $zero) {
        Write-Host "  (new branch - building all services)" -ForegroundColor DarkGray
        return [string[]]$ALL_SERVICES.Keys
    }

    $changed = @(git diff --name-only $From $To 2>$null)
    if ($changed.Count -eq 0) { return @() }

    foreach ($file in $changed) {
        foreach ($p in $GLOBAL_PATTERNS) {
            if ($file -match $p) {
                Write-Host "  Global file changed: $file - building all services" -ForegroundColor DarkGray
                return [string[]]$ALL_SERVICES.Keys
            }
        }
    }

    $detected = [System.Collections.Generic.List[string]]::new()
    foreach ($svc in $SERVICE_PATTERNS.Keys) {
        $pattern = $SERVICE_PATTERNS[$svc]
        foreach ($file in $changed) {
            if ($file -match $pattern) {
                $detected.Add($svc)
                break
            }
        }
    }
    return [string[]]$detected
}

# Determine what to build
if ($Services.Count -gt 0) {
    foreach ($s in $Services) {
        if (-not $ALL_SERVICES.ContainsKey($s)) {
            Write-Error "Unknown service '$s'. Valid: $($ALL_SERVICES.Keys -join ', ')"
        }
    }
    $toBuild = $Services
} elseif ($PushFrom -or $PushTo) {
    $toBuild = Get-ChangedServices -From $PushFrom -To $PushTo
    if ($toBuild.Count -eq 0) {
        Write-Host "No service changes detected - skipping Docker build." -ForegroundColor Yellow
        exit 0
    }
} else {
    $toBuild = [string[]]$ALL_SERVICES.Keys
}

Write-Host "`n==> Services to build: $($toBuild -join ', ')" -ForegroundColor Cyan

Write-Host "`n==> docker login $REGISTRY ..." -ForegroundColor Cyan
if ($env:GITHUB_TOKEN) {
    docker login $REGISTRY -u $OWNER -p $env:GITHUB_TOKEN
} else {
    docker login $REGISTRY
}
if ($LASTEXITCODE -ne 0) { Write-Error "docker login failed (exit $LASTEXITCODE)" }

foreach ($svc in $toBuild) {
    $dockerfile = $ALL_SERVICES[$svc]
    $tagSha    = "$REGISTRY/$OWNER/${svc}:sha-$SHA"
    $tagLatest = "$REGISTRY/$OWNER/${svc}:dev-latest"

    Write-Host "`n==> Building $svc ..." -ForegroundColor Cyan
    docker build --file $dockerfile --tag $tagSha --tag $tagLatest .
    if ($LASTEXITCODE -ne 0) { Write-Error "docker build failed for $svc" }

    Write-Host "==> Pushing $svc ..." -ForegroundColor Cyan
    docker push $tagSha
    if ($LASTEXITCODE -ne 0) { Write-Error "docker push failed for $svc ($tagSha)" }
    docker push $tagLatest
    if ($LASTEXITCODE -ne 0) { Write-Error "docker push failed for $svc ($tagLatest)" }

    Write-Host "    OK  $tagLatest" -ForegroundColor Green
}

Write-Host "`nDone. Built: $($toBuild -join ', ')" -ForegroundColor Green
