<#
.SYNOPSIS
  Brings up the full PoC stack: DB, API, shell-sim, Expo web app.
  Opens each service in a new terminal window.

.USAGE
  # 1. Set your JWT secret (same value for API and shell)
  $env:JWT_SECRET = "your-secret-here-at-least-32-chars"

  # 2. Run this script
  .\run.ps1

  # 3. Open http://localhost:5000 in your browser for the visual proof.
#>

$root = $PSScriptRoot

if (-not $env:JWT_SECRET) {
  Write-Host "ERROR: Set JWT_SECRET before running:" -ForegroundColor Red
  Write-Host '  $env:JWT_SECRET = "your-secret-here"' -ForegroundColor Yellow
  exit 1
}

# Step 1: Apply DB scripts
Write-Host "Applying DB scripts..." -ForegroundColor Cyan
sqlcmd -S "ndessdev\ndessdev" -d "ndess_dev03" -E -i "$root\db\setup.sql" | Out-Null
sqlcmd -S "ndessdev\ndessdev" -d "ndess_dev03" -E -i "$root\db\seed.sql"  | Out-Null
Write-Host "  DB ready." -ForegroundColor Green

# Step 2: Start API on port 5050
Write-Host "Starting API (http://localhost:5050)..." -ForegroundColor Cyan
$apiArgs = "run --no-build -c Release --project `"$root\api`" --urls http://localhost:5050"
Start-Process powershell -ArgumentList "-NoExit","-Command","`$env:JWT_SECRET='$env:JWT_SECRET'; dotnet $apiArgs"

Start-Sleep -Seconds 2

# Step 3: Start shell-sim on port 5000
Write-Host "Starting shell-sim (http://localhost:5000)..." -ForegroundColor Cyan
$simArgs = "run --no-build -c Release --project `"$root\legacy-shell-sim`" --urls http://localhost:5000"
Start-Process powershell -ArgumentList "-NoExit","-Command","`$env:JWT_SECRET='$env:JWT_SECRET'; dotnet $simArgs"

Start-Sleep -Seconds 2

# Step 4: Start Expo web app on port 8081
Write-Host "Starting Expo web app (http://localhost:8081)..." -ForegroundColor Cyan
Start-Process powershell -ArgumentList "-NoExit","-Command","Set-Location '$root\app'; npx expo start --web --port 8081"

Write-Host ""
Write-Host "Stack is starting. Wait ~10s, then:" -ForegroundColor Green
Write-Host "  Browser proof:  http://localhost:5000   (shell with iframe)" -ForegroundColor Yellow
Write-Host "  API direct:     http://localhost:5050/health" -ForegroundColor Yellow
Write-Host "  Expo standalone: http://localhost:8081  (app only, no token)" -ForegroundColor Yellow
Write-Host ""
Write-Host "Run .\verify.ps1 for the headless end-to-end check." -ForegroundColor Cyan
