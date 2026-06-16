<#
.SYNOPSIS
  Brings up the full PoC stack: DB, API, shell-sim, Expo web app.
  Builds the .NET projects once up front, then opens each service in a new terminal window.

.USAGE
  # Just run it. JWT_SECRET defaults to the embedded value below;
  # override by setting $env:JWT_SECRET first if you want a different one.
  .\run.ps1

  # Then open http://localhost:5000 in your browser for the visual proof.
#>

$root = $PSScriptRoot

# JWT secret: use env var if set, otherwise fall back to the embedded PoC value.
if (-not $env:JWT_SECRET) {
  $env:JWT_SECRET = "8vK2rNf7QxP4mT9zYcL6wHs3JdEa5UgB1XyR8kMz0NpV7qCt4FwS2hJn9DbL6eGx"
}

# ── Step 1: Stop any leftover processes from a previous run ───────────────────
Write-Host "Stopping any previous services..." -ForegroundColor Cyan
foreach ($port in @(5000, 5050)) {
  Get-NetTCPConnection -LocalPort $port -State Listen -ErrorAction SilentlyContinue |
    ForEach-Object { Stop-Process -Id $_.OwningProcess -Force -ErrorAction SilentlyContinue }
}
Start-Sleep -Seconds 1
Write-Host "  Done." -ForegroundColor Green

# ── Step 2: Apply DB scripts ──────────────────────────────────────────────────
Write-Host "Applying DB scripts..." -ForegroundColor Cyan
sqlcmd -S "ndessdev\ndessdev" -d "ndess_dev03" -E -i "$root\db\setup.sql"       | Out-Null
sqlcmd -S "ndessdev\ndessdev" -d "ndess_dev03" -E -i "$root\db\seed.sql"        | Out-Null
sqlcmd -S "ndessdev\ndessdev" -d "ndess_dev03" -E -i "$root\db\migrate_crud.sql"| Out-Null
Write-Host "  DB ready." -ForegroundColor Green

# ── Step 3: Build both .NET projects (fail fast, show errors) ─────────────────
Write-Host "Building API + shell-sim (Release)..." -ForegroundColor Cyan

$apiBuild = dotnet build -c Release "$root\api" 2>&1
if ($LASTEXITCODE -ne 0) {
  Write-Host "API build failed:" -ForegroundColor Red
  $apiBuild | Where-Object { $_ -match "error" } | ForEach-Object { Write-Host "  $_" -ForegroundColor Red }
  exit 1
}

$simBuild = dotnet build -c Release "$root\legacy-shell-sim" 2>&1
if ($LASTEXITCODE -ne 0) {
  Write-Host "shell-sim build failed:" -ForegroundColor Red
  $simBuild | Where-Object { $_ -match "error" } | ForEach-Object { Write-Host "  $_" -ForegroundColor Red }
  exit 1
}

Write-Host "  Build OK." -ForegroundColor Green

# ── Step 4: Start API on port 5050 ────────────────────────────────────────────
Write-Host "Starting API (http://localhost:5050)..." -ForegroundColor Cyan
$apiArgs = "run --no-build -c Release --project `"$root\api`" --urls http://localhost:5050"
Start-Process powershell -ArgumentList "-NoExit","-Command","`$env:JWT_SECRET='$env:JWT_SECRET'; dotnet $apiArgs"

Start-Sleep -Seconds 2

# ── Step 5: Start shell-sim on port 5000 ──────────────────────────────────────
Write-Host "Starting shell-sim (http://localhost:5000)..." -ForegroundColor Cyan
$simArgs = "run --no-build -c Release --project `"$root\legacy-shell-sim`" --urls http://localhost:5000"
Start-Process powershell -ArgumentList "-NoExit","-Command","`$env:JWT_SECRET='$env:JWT_SECRET'; dotnet $simArgs"

Start-Sleep -Seconds 2

# ── Step 6: Start Expo web app on port 8081 ───────────────────────────────────
Write-Host "Starting Expo web app (http://localhost:8081)..." -ForegroundColor Cyan
Start-Process powershell -ArgumentList "-NoExit","-Command","Set-Location '$root\app'; npx expo start --web --port 8081"

Write-Host ""
Write-Host "Stack is starting. Wait ~10s, then:" -ForegroundColor Green
Write-Host "  Browser proof:   http://localhost:5000          (shell with iframe)" -ForegroundColor Yellow
Write-Host "  Swagger UI:      http://localhost:5050/swagger  (API docs + test)" -ForegroundColor Yellow
Write-Host "  Expo standalone: http://localhost:8081          (app only)" -ForegroundColor Yellow
Write-Host ""
Write-Host "Run .\verify.ps1 for the headless end-to-end check." -ForegroundColor Cyan
