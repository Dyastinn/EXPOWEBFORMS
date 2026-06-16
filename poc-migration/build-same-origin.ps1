<#
.SYNOPSIS
  Builds the Expo app as static files and places them under the shell-sim's
  wwwroot/app/ so both shell and Expo app share http://localhost:5000 as their
  origin (no cross-origin, no CORS needed for the embed).

.USAGE
  .\build-same-origin.ps1

  Then start the shell-sim with EXPO_APP_URL=/app/:
    $env:EXPO_APP_URL = "/app/"
    $env:JWT_SECRET   = "your-secret-here"
    dotnet run --project legacy-shell-sim --urls http://localhost:5000

  Open http://localhost:5000 — shell and Expo app are the same origin.
#>

$root    = $PSScriptRoot
$appDir  = Join-Path $root "app"
$outDir  = Join-Path $root "legacy-shell-sim\wwwroot\app"

Write-Host "Building Expo web app (static export)..." -ForegroundColor Cyan
Push-Location $appDir
try {
    # Remove previous build
    if (Test-Path $outDir) { Remove-Item $outDir -Recurse -Force }

    # Export to a temp location first so we can patch paths before moving
    $tmpOut = Join-Path $env:TEMP "expo-same-origin-build"
    if (Test-Path $tmpOut) { Remove-Item $tmpOut -Recurse -Force }

    & cmd /c "npx expo export --platform web --output-dir `"$tmpOut`"" 2>&1
    if ($LASTEXITCODE -ne 0) { throw "expo export failed (exit $LASTEXITCODE)" }

    # Patch index.html: make absolute /_expo/ and /favicon paths relative so the
    # files work when served from any subpath (e.g. /app/).
    $indexPath = Join-Path $tmpOut "index.html"
    $html = Get-Content $indexPath -Raw
    # /_expo/... → ./_expo/... and /favicon → ./favicon
    $html = $html -replace 'src="/_expo/', 'src="./_expo/'
    $html = $html -replace "src='/_expo/", "src='./_expo/"
    $html = $html -replace 'href="/_expo/', 'href="./_expo/'
    $html = $html -replace 'href="/favicon', 'href="./favicon'
    Set-Content $indexPath $html -Encoding UTF8

    # Move the patched build into the shell-sim's wwwroot/app/
    Move-Item $tmpOut $outDir

    Write-Host "  Static build placed at: $outDir" -ForegroundColor Green
    Write-Host ""
    Write-Host "To run in same-origin mode:" -ForegroundColor Yellow
    Write-Host '  $env:EXPO_APP_URL = "/app/"'
    Write-Host '  $env:JWT_SECRET   = "8vK2rNf7QxP4mT9zYcL6wHs3JdEa5UgB1XyR8kMz0NpV7qCt4FwS2hJn9DbL6eGx"'
    Write-Host "  dotnet run --project legacy-shell-sim --urls http://localhost:5000"
    Write-Host ""
    Write-Host "Then open http://localhost:5000 (Expo at /app/, same origin, no CORS for the embed)" -ForegroundColor Green
} finally {
    Pop-Location
}
