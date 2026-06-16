# Real WebForms Shell

This is a genuine ASP.NET WebForms project targeting **.NET Framework 4.8**, runnable on IIS Express.
It uses the same JWT contract (HS256, issuer `poc-legacy-shell`, audience `poc-api`) as the
`legacy-shell-sim`, so the API accepts tokens from either source interchangeably.

## What's here

| File | Purpose |
|------|---------|
| `Default.aspx` | Iframe host page — loads the Expo app and sends the JWT via `postMessage` |
| `Default.aspx.cs` | Code-behind — stub login, sets the username label |
| `Handlers/TokenHandler.ashx` | HTTP handler that issues HS256 JWTs |
| `Handlers/TokenHandler.ashx.cs` | Token creation logic (System.IdentityModel.Tokens.Jwt 6.x) |
| `web.config` | ASP.NET + IIS Express config |
| `LegacyShell.csproj` | Old-style web application project (VS 2022 / MSBuild 17) |

## Build + Run from Visual Studio 2022

1. Open `LegacyShell.csproj` in VS 2022.
2. VS will restore NuGet packages automatically (`System.IdentityModel.Tokens.Jwt 6.36.0`, `Newtonsoft.Json 13.0.3`).
3. Set `JWT_SECRET` in your environment **before** starting (VS reads the process environment):
   ```
   setx JWT_SECRET "your-secret-here" /M
   ```
   Or add it to the Debug launch profile in VS → Properties → Debug → Environment Variables.
4. Press **F5** — IIS Express starts on port 5001 (or whichever VS allocates).
5. Navigate to `http://localhost:5001/`.

## Build + Run from command line (no VS GUI needed)

Requires MSBuild from VS 2022 build tools:

```powershell
# Locate MSBuild
$msbuild = Get-ChildItem "C:\Program Files\Microsoft Visual Studio\2022" -Recurse -Filter "MSBuild.exe" |
  Where-Object { $_.FullName -match "Current\\Bin\\MSBuild.exe" } |
  Select-Object -First 1 -ExpandProperty FullName

# Restore + Build
& $msbuild LegacyShell.csproj /t:Restore /p:Configuration=Debug
& $msbuild LegacyShell.csproj /p:Configuration=Debug

# Run with IIS Express on port 5001
$env:JWT_SECRET = "your-secret-here"
& "C:\Program Files (x86)\IIS Express\iisexpress.exe" /path:"$PWD" /port:5001
```

Then open http://localhost:5001/.

## Token interchangeability

The shell-sim (`legacy-shell-sim/`) issues byte-for-byte equivalent tokens:
- **Algorithm:** HS256
- **Issuer:** `poc-legacy-shell`
- **Audience:** `poc-api`
- **Signing key:** the same `JWT_SECRET` env var

The API validates both identically. Swap the shell origin in `App.tsx` from `:5000` to `:5001`
to use the real WebForms shell instead of the simulator.

## Production path

Replace HS256 + shared secret with **RS256 + JWKS endpoint** published by the WebForms shell.
The API then fetches the public key from the JWKS URL and validates signatures without any
shared secret. See `PROOF.md` for details.
