#Requires -Version 7
<#
  Грузит load/.env в окружение процесса, затем вызывает k6 run с переданными аргументами.
  Пример: ./load/run.ps1 load/scenarios/smoke.js
#>

# Находим k6.exe: либо он уже в PATH, либо в типичных install-путях Windows.
$k6Exe = (Get-Command k6 -ErrorAction SilentlyContinue).Source
if (-not $k6Exe) {
    $candidates = @(
        "$env:ProgramFiles\k6\k6.exe",
        "${env:ProgramFiles(x86)}\k6\k6.exe",
        "$env:LOCALAPPDATA\Programs\k6\k6.exe"
    )
    $k6Exe = $candidates | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
}
if (-not $k6Exe) {
    throw "k6 не найден. Установите: winget install GrafanaLabs.k6 (и откройте НОВЫЙ терминал)."
}

$envFile = Join-Path $PSScriptRoot '.env'
if (Test-Path -LiteralPath $envFile) {
    Get-Content -LiteralPath $envFile | ForEach-Object {
        $line = $_.Trim()
        if ($line -and -not $line.StartsWith('#')) {
            $idx = $line.IndexOf('=')
            if ($idx -gt 0) {
                $name = $line.Substring(0, $idx).Trim()
                $val  = $line.Substring($idx + 1).Trim().Trim('"').Trim("'")
                Set-Item -Path "Env:$name" -Value $val
            }
        }
    }
} else {
    Write-Warning "load/.env не найден — k6 запустится с переменными текущего окружения."
}

& $k6Exe run $args
