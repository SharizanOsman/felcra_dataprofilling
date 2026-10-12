# Runs every browser suite against a local development API, resetting the database between groups.
# Needs: Docker database (docker compose up -d db), a built solution (dotnet build), npm install in this folder,
# and the Pagoh census CSV (client data, not in the repository).
#   powershell -File tests\e2e\run-all.ps1 [-Csv <path-to-census.csv>]
param(
    [string]$Csv = (Join-Path $PSScriptRoot "..\..\Document\pagoh-census-2026-07.csv"),
    [string]$DbContainer = "felcra_dataprofilling-db-1"
)
$ErrorActionPreference = "Continue"
$repo = (Resolve-Path (Join-Path $PSScriptRoot "..\..")).Path
$e2e = $PSScriptRoot
$out = Join-Path $e2e "out"
$publish = Join-Path $out "publish"
$Csv = (Resolve-Path $Csv).Path
New-Item -ItemType Directory -Force $out | Out-Null
# The suites sign in many times a minute from one address; lift the sign-in rate limit for the APIs started here.
$env:RateLimiting__LoginPermitsPerMinute = "1000"

function Wait-Health($url) { for ($i = 0; $i -lt 90; $i++) { try { if ((Invoke-WebRequest "$url/health" -UseBasicParsing -TimeoutSec 2).StatusCode -eq 200) { return } } catch {}; Start-Sleep 1 } }
function Stop-Apis { Get-CimInstance Win32_Process | Where-Object { $_.CommandLine -match "Felcra.Profiling.Api" -and $_.Name -notmatch "powershell|pwsh" } | ForEach-Object { Stop-Process -Id $_.ProcessId -Force -ErrorAction SilentlyContinue }; Start-Sleep 2 }
function Reset-Db {
    Stop-Apis
    docker exec $DbContainer psql -U felcra -d postgres -q -c "DROP DATABASE IF EXISTS felcra_profiling WITH (FORCE);" -c "CREATE DATABASE felcra_profiling OWNER felcra;" | Out-Null
    Start-Process dotnet -ArgumentList "run --project `"$repo\src\Felcra.Profiling.Api`" --launch-profile http --no-build" -WindowStyle Hidden
    Wait-Health "http://localhost:5237"
    Start-Sleep 3 # seeding finishes just after the health check answers
}
function Import-Pagoh($base = "http://localhost:5237") {
    $env:BASE = $base
    Push-Location $e2e; node import-pagoh.mjs $Csv | Select-Object -Last 1; Pop-Location
    Remove-Item Env:BASE
}
$summary = @()
function Run($name, $script, [string[]]$scriptArgs) {
    Write-Output "=== $name"
    Push-Location $e2e
    $result = node $script @scriptArgs 2>&1 | ForEach-Object { "$_" }
    Pop-Location
    $result | Where-Object { $_ -match "^FAIL" } | ForEach-Object { Write-Output $_ }
    $last = $result | Where-Object { $_ -match "passed" } | Select-Object -Last 1
    if (-not $last) { $last = $result | Select-Object -Last 1 }
    Write-Output $last
    $script:summary += "$name : $last"
}

Reset-Db
Run "Admin (master data, farms, users)" "admin-e2e.mjs" @($out)
Run "Language and farm map" "language-map-e2e.mjs" @("http://localhost:5237", $out)
Run "Census and import" "census-e2e.mjs" @($Csv, $out)

Reset-Db
Import-Pagoh
Run "Analytics and dashboard" "analytics-e2e.mjs" @("http://localhost:5237", $out)
Run "Reports" "reports-e2e.mjs" @("http://localhost:5237", $out, "$repo\src\Felcra.Profiling.Api\App_Data\mail")
Run "Field app (QR scan, recording)" "field-e2e.mjs" @($out)
Run "Account (change password)" "account-e2e.mjs" @("http://localhost:5237")

# The offline suite needs the published build (service worker with offline cache).
Reset-Db
Stop-Apis
dotnet publish "$repo\src\Felcra.Profiling.Api" -c Release -o $publish 2>&1 | Select-String " error " | Select-Object -First 5
$env:ASPNETCORE_ENVIRONMENT = "Development"; $env:ASPNETCORE_URLS = "http://localhost:5299"
Start-Process dotnet -ArgumentList "`"$publish\Felcra.Profiling.Api.dll`"" -WorkingDirectory $publish -WindowStyle Hidden
Remove-Item Env:ASPNETCORE_URLS
Wait-Health "http://localhost:5299"; Start-Sleep 3
Import-Pagoh "http://localhost:5299"
Run "Offline and sync" "offline-e2e.mjs" @("http://localhost:5299", $out)

Reset-Db
Import-Pagoh
Run "Breeding and reminders" "breeding-e2e.mjs" @("http://localhost:5237", $out)

Write-Output "`n==== SUMMARY"; $summary
