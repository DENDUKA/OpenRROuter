#Requires -Version 7
<#
  Грузит load/.env в окружение процесса, затем вызывает k6 run с переданными аргументами.
  Пример: ./load/run.ps1 load/scenarios/smoke.js
#>
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

k6 run $args
