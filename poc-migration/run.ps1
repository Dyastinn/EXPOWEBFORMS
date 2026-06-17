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
foreach ($port in @(5000, 5001, 5050)) {
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
# API and shell-sim are SDK-style projects; legacy-shell is handled separately below.

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


$msbuildExe = $null
$vswhere = Join-Path ${env:ProgramFiles(x86)} "Microsoft Visual Studio\Installer\vswhere.exe"

if (Test-Path $vswhere) {
    $found = & $vswhere `
        -latest `
        -requires Microsoft.Component.MSBuild `
        -find "MSBuild\**\Bin\MSBuild.exe" 2>$null

    if ($found) {
        $msbuildExe = @($found)[0]
    }
}

if (-not $msbuildExe) {
    $msbuildCmd = Get-Command msbuild.exe -ErrorAction SilentlyContinue
    if ($msbuildCmd) {
        $msbuildExe = $msbuildCmd.Source
    }
}

if ($msbuildExe -and (Test-Path $msbuildExe)) {
    Write-Host "Building legacy-shell via MSBuild..." -ForegroundColor Cyan
    Write-Host "  Using: $msbuildExe" -ForegroundColor DarkGray

    $msbuildArgs = @(
        "$root\legacy-shell\LegacyShell.csproj"
        "/t:Build"
        "/p:Configuration=Release"
        "/p:Platform=AnyCPU"
        "/nologo"
        "/verbosity:minimal"
    )

    $buildOutput = & $msbuildExe @msbuildArgs 2>&1

    if ($LASTEXITCODE -ne 0) {
        Write-Host "  legacy-shell build failed." -ForegroundColor Red

        $buildOutput |
            Where-Object { $_ -match "error" } |
            ForEach-Object { Write-Host "  $_" -ForegroundColor Red }

        exit 1
    }

    Write-Host "  legacy-shell build OK." -ForegroundColor Green
}
else {
    Write-Host "  MSBuild not found - legacy-shell will be compiled on first IIS Express request." -ForegroundColor Yellow
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

# ── Step 6: Start legacy-shell (real WebForms .NET 4.8) via IIS Express ────────
$iisExpress = "${env:ProgramFiles}\IIS Express\iisexpress.exe"
if (-not (Test-Path $iisExpress)) {
  $iisExpress = "${env:ProgramFiles(x86)}\IIS Express\iisexpress.exe"
}
if (Test-Path $iisExpress) {
  Write-Host "Starting legacy-shell (http://localhost:5001)..." -ForegroundColor Cyan
  $shellPath = (Resolve-Path "$root\legacy-shell").Path
  Start-Process powershell -ArgumentList "-NoExit","-Command","& '$iisExpress' /path:'$shellPath' /port:5001 /systray:false"
  Start-Sleep -Seconds 2
} else {
  Write-Host "  IIS Express not found - skipping legacy-shell." -ForegroundColor Yellow
  Write-Host "  To run it, open poc-migration\legacy-shell\LegacyShell.csproj in Visual Studio." -ForegroundColor Yellow
}

# ── Step 7: Start Expo web app on port 8081 ───────────────────────────────────
Write-Host "Starting Expo web app (http://localhost:8081)..." -ForegroundColor Cyan
Start-Process powershell -ArgumentList "-NoExit","-Command","Set-Location '$root\app'; npx expo start --web --port 8081"

Write-Host ""
Write-Host "Stack is starting. Wait ~10s, then:" -ForegroundColor Green
Write-Host "  Shell sim (login required):  http://localhost:5000          (ASP.NET Core - runs always)" -ForegroundColor Yellow
Write-Host "  Legacy WebForms shell:        http://localhost:5001          (ASP.NET 4.8 - requires IIS Express)" -ForegroundColor Yellow
Write-Host "  Swagger UI:                   http://localhost:5050/swagger  (API docs + test)" -ForegroundColor Yellow
Write-Host "  Expo standalone:              http://localhost:8081          (app only)" -ForegroundColor Yellow
Write-Host ""
Write-Host "Run .\verify.ps1 for the headless end-to-end check." -ForegroundColor Cyan
