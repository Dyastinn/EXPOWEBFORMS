# PoC: WebForms → API Migration Spike

Proves a single risky claim: a legacy ASP.NET WebForms shell can hand a JWT to an embedded
Expo/React Native web app, which calls a .NET 10 API, which reuses a SQL Server stored procedure
(via Dapper) — across origins — with the API rejecting unauthenticated calls.

## Architecture

```
[Browser]
  │
  ├── http://localhost:5000  ← Legacy Shell Simulator (ASP.NET Core, mimics WebForms)
  │     └── /token  (POST)  → issues HS256 JWT
  │     └── /       (GET)   → HTML page with <iframe src="http://localhost:8081">
  │                              └── postMessage({ type:'AUTH_TOKEN', token })
  │
  └── http://localhost:8081  ← Expo/React Native web app (Metro)
        └── receives token via postMessage, stores in memory only
        └── calls GET /api/customers with Bearer token
              │
              └── http://localhost:5050  ← .NET 10 API
                    └── validates JWT (issuer + audience + signature + expiry)
                    └── calls poc.usp_ListCustomers via Dapper
                    └── returns JSON with computed Tier column
```

## Quick start

```powershell
# 1. Set the shared JWT secret (same for API and shell)
$env:JWT_SECRET = "8vK2rNf7QxP4mT9zYcL6wHs3JdEa5UgB1XyR8kMz0NpV7qCt4FwS2hJn9DbL6eGx"

# 2. One-command launch (opens three terminal windows)
.\run.ps1

# 3. Headless verification
.\verify.ps1
```

For a step-by-step manual start, see `run.md`.

## Prerequisites

| Tool | Version | Check |
|------|---------|-------|
| .NET SDK | 10.x | `dotnet --version` |
| Node.js | 18+ | `node -v` |
| sqlcmd | any | `sqlcmd -?` |
| SQL Server | dev instance | `ndessdev\ndessdev`, Windows auth |

## Repository layout

```
/poc-migration
  /db                  SQL scripts (idempotent; poc schema only)
    setup.sql          creates poc schema + Customers table + usp_ListCustomers
    seed.sql           inserts 5 sample customers across Gold/Silver/Bronze tiers
    teardown.sql       drops everything in poc schema — run to clean up
  /api                 .NET 10 Web API: JWT auth, Dapper, CORS
  /legacy-shell        Real WebForms (.NET Fx 4.8) — open in VS 2022 to run
  /legacy-shell-sim    ASP.NET Core shell simulator — runs with dotnet run
  /app                 Expo SDK 56 app: web + mobile target
  run.md               Manual ordered run steps
  run.ps1              Automated launcher (opens 3 terminal windows)
  verify.ps1           Headless PASS/FAIL proof script
  PROOF.md             Acceptance criteria → evidence mapping
  .env.example         JWT_SECRET template
```

## Visual browser proof (manual steps)

1. Start all three services (see Quick start above).
2. Open **http://localhost:5000** in Chrome/Edge.
3. **Expected:** "Legacy WebForms Shell — Logged in as Demo User" header appears.
4. The iframe below it loads **http://localhost:8081** (Expo web app).
5. The shell fetches a JWT from `/token` and calls `iframe.contentWindow.postMessage(...)` with a strict `targetOrigin` of `http://localhost:8081`.
6. The Expo app verifies `event.origin === 'http://localhost:5000'` before accepting the token.
7. The app calls `GET http://localhost:5050/api/customers` with the Bearer token.
8. **Expected table in the iframe:**

   | Name | Email | Tier | Spend |
   |------|-------|------|-------|
   | David Kim | david@example.com | **Gold** | $22100.50 |
   | Alice Chen | alice@example.com | **Gold** | $15250.00 |
   | Bob Martinez | bob@example.com | **Silver** | $5430.75 |
   | Carol Davis | carol@example.com | **Bronze** | $800.00 |
   | Eve Robinson | eve@example.com | **Bronze** | $250.00 |

9. Status line reads: *"Loaded 5 customers from stored proc via API ✓"*

## Teardown

```powershell
sqlcmd -S "ndessdev\ndessdev" -d "ndess_dev03" -E -i "db\teardown.sql"
```

Removes `poc.usp_ListCustomers`, `poc.Customers`, and the `poc` schema.
Nothing outside the `poc` schema was touched.

## What's NOT proven (out of scope for this spike)

- Refresh tokens / token rotation
- RS256 + JWKS endpoint (noted in PROOF.md as the obvious next step)
- Production hardening, rate limiting, secrets management
- Multiple modules beyond Customers
- CI/CD pipeline
