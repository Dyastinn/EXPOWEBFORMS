# Run Order

## Prerequisites

- .NET 10 SDK (`dotnet --version` → 10.x)
- Node.js 18+ (`node -v`)
- SQL Server reachable at `ndessdev\ndessdev` with Windows auth
- `sqlcmd` in PATH

## Step 0 — Set the shared JWT secret

```powershell
$env:JWT_SECRET = "your-secret-at-least-32-chars-here"
```

Both the API and the shell-sim **must** share the same value. Never commit this.

## Step 1 — Apply DB scripts (one time, idempotent)

```powershell
sqlcmd -S "ndessdev\ndessdev" -d "ndess_dev03" -E -i "db\setup.sql"
sqlcmd -S "ndessdev\ndessdev" -d "ndess_dev03" -E -i "db\seed.sql"
```

Verify: `sqlcmd -S "ndessdev\ndessdev" -d "ndess_dev03" -E -Q "EXEC poc.usp_ListCustomers;"`

## Step 2 — Start the API (port 5050)

```powershell
# In terminal 1
dotnet run --project api --urls http://localhost:5050
```

Verify: `curl http://localhost:5050/health` → `{"status":"ok"}`

## Step 3 — Start the shell simulator (port 5000)

```powershell
# In terminal 2
dotnet run --project legacy-shell-sim --urls http://localhost:5000
```

Verify: `(Invoke-WebRequest http://localhost:5000/token -Method POST).Content`

## Step 4 — Start the Expo web app (port 8081)

```powershell
# In terminal 3
cd app
npx expo start --web --port 8081
```

Metro will bundle and serve the app at http://localhost:8081.

## Step 5 — Run the headless verify script

```powershell
# JWT_SECRET must be set in the same session
.\verify.ps1
```

Expected output: all `PASS` lines, exit code 0.

## Step 6 — Visual browser proof

1. Open **http://localhost:5000** in a browser.
2. You should see the "Legacy WebForms Shell" header and an iframe.
3. The iframe loads the Expo app at http://localhost:8081.
4. The shell fetches a JWT from `/token` and sends it to the iframe via `postMessage`.
5. The Expo app receives the token, calls `GET /api/customers`, and renders the customer table with Tier badges.

**What success looks like:** The iframe shows "Loaded 5 customers from stored proc via API ✓" and a table with Gold/Silver/Bronze tiers.

## Step 7 — Teardown (remove all poc objects)

```powershell
sqlcmd -S "ndessdev\ndessdev" -d "ndess_dev03" -E -i "db\teardown.sql"
```

## Real WebForms shell (optional — requires VS 2022)

See `legacy-shell/README.md` for building and running the real .NET Framework 4.8 WebForms shell instead of the simulator.
