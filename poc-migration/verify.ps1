<#
.SYNOPSIS
  Headless end-to-end proof for the WebForms -> API migration spike.
  Applies DB scripts, starts the API + shell-sim, then runs PASS/FAIL checks.
  Exits non-zero if any check fails.

.PREREQUISITES
  Set JWT_SECRET in your environment (or a .env file) before running:
    $env:JWT_SECRET = "your-secret-here"
#>

param(
  [string]$SqlServer  = "ndessdev\ndessdev",
  [string]$Database   = "ndess_dev03",
  [string]$ApiUrl     = "http://localhost:5050",
  [string]$ShellUrl   = "http://localhost:5000",
  [int]   $StartupSec = 6
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"
$script:failures = 0

function Pass([string]$msg) { Write-Host "  PASS  $msg" -ForegroundColor Green }
function Fail([string]$msg) { Write-Host "  FAIL  $msg" -ForegroundColor Red; $script:failures++ }
function Section([string]$msg) { Write-Host "`n=== $msg ===" -ForegroundColor Cyan }

# --- 0. Check JWT_SECRET ---
Section "Prerequisites"
if (-not $env:JWT_SECRET) { Fail "JWT_SECRET env var not set. Set it and rerun."; exit 1 }
Pass "JWT_SECRET is set"

# --- 1. Apply DB scripts ---
Section "Database"
$root = $PSScriptRoot
try {
  sqlcmd -S $SqlServer -d $Database -E -i "$root\db\setup.sql"        | Out-Null
  sqlcmd -S $SqlServer -d $Database -E -i "$root\db\seed.sql"         | Out-Null
  sqlcmd -S $SqlServer -d $Database -E -i "$root\db\migrate_crud.sql" | Out-Null
  Pass "DB setup + seed applied"
} catch {
  Fail "DB setup failed: $_"; exit 1
}

# Verify the proc runs and returns a Tier column
$procOut = sqlcmd -S $SqlServer -d $Database -E -Q "EXEC poc.usp_ListCustomers;" 2>&1
if ($procOut -match "Gold|Silver|Bronze") {
  Pass "poc.usp_ListCustomers returns Tier column (business logic in proc)"
} else {
  Fail "poc.usp_ListCustomers did not return expected Tier values"
}

# --- 2. Start API + shell-sim ---
Section "Starting services"

# Kill any stale processes on the target ports
@(5050, 5000) | ForEach-Object {
  $port = $_
  $pids = netstat -ano | Select-String ":$port\s" | ForEach-Object {
    ($_ -split "\s+")[-1]
  } | Sort-Object -Unique
  $pids | Where-Object { $_ -match "^\d+$" } | ForEach-Object {
    try { Stop-Process -Id $_ -Force -ErrorAction SilentlyContinue } catch {}
  }
}

$apiProc  = Start-Process dotnet -ArgumentList "run","--no-build","-c","Release","--project","$root\api","--urls",$ApiUrl  -PassThru -WindowStyle Hidden
$shellProc = Start-Process dotnet -ArgumentList "run","--no-build","-c","Release","--project","$root\legacy-shell-sim","--urls",$ShellUrl -PassThru -WindowStyle Hidden

Write-Host "  Waiting ${StartupSec}s for services to start…"
Start-Sleep -Seconds $StartupSec

try {
  $h = Invoke-WebRequest "$ApiUrl/health" -UseBasicParsing -TimeoutSec 5
  Pass "API /health -> $($h.StatusCode)"
} catch { Fail "API not responding: $_" }

# --- 3. Token endpoint ---
Section "Shell token endpoint"
$token = $null
try {
  $r    = Invoke-WebRequest "$ShellUrl/token" -Method POST -UseBasicParsing -TimeoutSec 5
  $token = ($r.Content | ConvertFrom-Json).access_token
  if ($token -and $token.StartsWith("eyJ")) { Pass "Shell /token returns a JWT" }
  else { Fail "Shell /token did not return a JWT" }
} catch { Fail "Shell /token failed: $_" }

# --- 4. JWT validation: no token ---
Section "API JWT validation"
try {
  Invoke-WebRequest "$ApiUrl/api/customers" -UseBasicParsing -TimeoutSec 5 | Out-Null
  Fail "No token: expected 401, got 200"
} catch {
  $code = $_.Exception.Response.StatusCode.value__
  if ($code -eq 401) { Pass "No token -> 401 Unauthorized" }
  else                { Fail  "No token -> got $code (expected 401)" }
}

# --- 5. JWT validation: tampered token ---
if ($token) {
  $parts    = $token.Split(".")
  $tampered = $parts[0] + "." + $parts[1] + ".TAMPERED_SIGNATURE_XXXXXX"
  try {
    Invoke-WebRequest "$ApiUrl/api/customers" -Headers @{ Authorization="Bearer $tampered" } -UseBasicParsing -TimeoutSec 5 | Out-Null
    Fail "Tampered token: expected 401, got 200"
  } catch {
    $code = $_.Exception.Response.StatusCode.value__
    if ($code -eq 401) { Pass "Tampered token -> 401 Unauthorized" }
    else                { Fail  "Tampered token -> got $code (expected 401)" }
  }
}

# --- 6. Valid token: 200 + proc data with Tier ---
if ($token) {
  try {
    $r         = Invoke-WebRequest "$ApiUrl/api/customers" -Headers @{ Authorization="Bearer $token" } -UseBasicParsing -TimeoutSec 5
    $paged     = $r.Content | ConvertFrom-Json
    $customers = $paged.items
    $hasTier   = @($customers | Where-Object { $_.tier -ne $null -and $_.tier -ne "" })
    if ($r.StatusCode -eq 200 -and $customers.Count -gt 0 -and $hasTier.Count -eq $customers.Count) {
      Pass "Valid token -> 200, $($paged.totalCount) customers, all have Tier (proc business logic)"
      $customers | ForEach-Object {
        Write-Host "    $($_.name) | Spend: $($_.totalSpend) | Tier: $($_.tier)" -ForegroundColor Gray
      }
    } else {
      Fail "Valid token returned $($r.StatusCode) with $($customers.Count) customers, Tier in $($hasTier.Count)"
    }
  } catch { Fail "Valid token request failed: $_" }
}

# --- 7. CORS header check ---
Section "CORS"
if ($token) {
  try {
    $headers = @{
      Authorization = "Bearer $token"
      Origin        = "http://localhost:8081"
    }
    $r = Invoke-WebRequest "$ApiUrl/api/customers?page=1&pageSize=5" -Headers $headers -UseBasicParsing -TimeoutSec 5
    $acao = $r.Headers["Access-Control-Allow-Origin"]
    if ($acao -eq "http://localhost:8081") {
      Pass "CORS: Access-Control-Allow-Origin = http://localhost:8081"
    } else {
      Fail "CORS header missing or wrong: '$acao'"
    }
  } catch { Fail "CORS check failed: $_" }
}

# --- Cleanup ---
Section "Cleanup"
try { Stop-Process -Id $apiProc.Id   -Force -ErrorAction SilentlyContinue } catch {}
try { Stop-Process -Id $shellProc.Id -Force -ErrorAction SilentlyContinue } catch {}
Pass "API + shell-sim processes stopped"

# --- Summary ---
Write-Host ""
if ($script:failures -eq 0) {
  Write-Host "ALL CHECKS PASSED" -ForegroundColor Green
  exit 0
} else {
  Write-Host "$($script:failures) CHECK(S) FAILED" -ForegroundColor Red
  exit 1
}
