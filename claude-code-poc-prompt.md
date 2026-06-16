# Claude Code task: feasibility spike for a WebForms → API migration

## Objective

Build a **runnable vertical slice** that proves one risky claim end to end: that a legacy
**ASP.NET WebForms** shell can hand a **JWT** to an embedded **Expo / React Native web** app,
which calls a modern **.NET API**, which **reuses a SQL Server stored procedure** (via Dapper)
to return data — all across origins, with the API rejecting unauthenticated calls.

This is a **spike to de-risk an architecture decision**, not the product. One entity, one or two
stored procedures, no polish. Optimize for *proving the integration contract works*, then stop.

Context: I am evaluating migrating an enterprise WebForms app (VB.NET, business logic mostly in
stored procedures) onto a .NET API consumed by React Native (mobile + web), embedding the new web
UI into the existing WebForms shell during transition. Prove the spine of that works.

## My environment (already installed — do NOT use Docker)

- **.NET 10** SDK
- **SQL Server** — a real dev instance. Use this connection (Windows auth):
  `Data Source=ndessdev\ndessdev;Initial Catalog=ndess_dev03;Integrated Security=True;Trusted_Connection=Yes;`
- **Node.js** / npx
- Host OS is **Windows** (named instance + Trusted_Connection).

Verify these are reachable before building (`dotnet --version`, a test SQL connection, `node -v`).
If something is missing or the DB won't connect, **stop and tell me exactly what's wrong** rather
than guessing or faking it.

### Connection-string handling (avoid the common failures)

- Use **`Microsoft.Data.SqlClient`** (the .NET 10 default; not `System.Data.SqlClient`).
- `Microsoft.Data.SqlClient` defaults to `Encrypt=True` and **will fail with a certificate error**
  against a local instance with no trusted cert. **Add `TrustServerCertificate=True`** for this dev
  spike. Effective string:
  `Data Source=ndessdev\ndessdev;Initial Catalog=ndess_dev03;Integrated Security=True;TrustServerCertificate=True;`
- In `appsettings.json` the backslash must be escaped: `Data Source=ndessdev\\ndessdev;...`.
- `Integrated Security` means the API connects as the **Windows identity running the process** — no
  secret to store. Run the API under an account that can reach `ndess_dev03`.

## Protect my dev database (non-negotiable)

`ndess_dev03` is a shared dev database, not a sandbox. Therefore:

- Create **all** PoC objects under a dedicated **`poc` schema** (e.g. `poc.Customers`,
  `poc.usp_ListCustomers`). Make every script **idempotent** (drop-if-exists then create).
- Provide a **`db/teardown.sql`** that removes everything in the `poc` schema and the schema itself.
- **Do not** create, alter, read, or delete any object outside the `poc` schema. The only permitted
  query against existing objects is reading metadata (`sys.procedures`, `sys.schemas`) to confirm
  there's no `poc`-schema collision.
- Apply scripts with `sqlcmd` (or PowerShell `Invoke-Sqlcmd`) using the same Windows-auth connection.

### Optional, higher-value: prove reuse against a real proc

The strongest proof of "reuse existing stored procedures" is to call one of *mine*. If — and only if —
I supply the name of an existing **read-only** stored procedure in `ndess_dev03`, wire the API to call
**that** proc instead of the synthetic one, treating it as read-only (never alter it). If I don't
supply one, use the synthetic `poc` proc below. Ask me once at the start; don't block waiting.

## What counts as "proof" — acceptance criteria

Build it so **every item is demonstrably true and self-verifiable**. Produce a `PROOF.md` mapping each
criterion to the file/endpoint that satisfies it and the exact command or browser step to confirm it.

1. A **stored procedure runs in SQL Server** containing real logic (a computed/derived field or
   filtering), standing in for "business logic that lives in a proc."
2. The **.NET API calls that proc via Dapper** (not EF, not inline SQL) and returns its result as JSON.
3. The API **validates a JWT** (signature + issuer + audience) before touching the proc. No token or a
   tampered token → **401**; valid token → **200** with proc data.
4. A **legacy shell issues the JWT** (it is the identity issuer; the API is the resource server). No
   shared server session crosses the boundary — auth travels only as the token.
5. The shell **hosts the Expo web app in an `<iframe>`** and **hands the token to it via `postMessage`**
   with a strict `targetOrigin`; the iframe **verifies `event.origin`** before accepting it.
6. The embedded app holds the token **in memory only** (a module/state variable), **never in
   `localStorage`/`sessionStorage`**. Add a code comment stating why.
7. The handoff and API call **work across different origins** (shell, app, API on different localhost
   ports), with **CORS** on the API scoped to the app origin. This is the part that would otherwise
   break — it must visibly work.
8. The **same Expo codebase also runs as a mobile target** (builds/launches for iOS or Android, or at
   minimum the mobile entry point is wired to the same API). Proves "one codebase, web + mobile."

## The WebForms shell — attempt the real one, since I'm on Windows

ASP.NET WebForms runs only on .NET Framework (Windows/IIS). On this Windows host that may be runnable:

- **Build the real WebForms shell** under `legacy-shell/`: a `.aspx` host page + code-behind, an
  **OWIN JWT token endpoint** (`Microsoft.Owin.Security.Jwt`), and the `postMessage` handoff script.
- **Check whether it can actually run here** (IIS Express, or the .NET Framework 4.8 targeting pack /
  Visual Studio dev server). If yes, run it and use it for the end-to-end demo.
- If the WebForms dev tooling is **not** present, build a **minimal ASP.NET Core "shell simulator"**
  under `legacy-shell-sim/` (same .NET toolchain) that mints an **interchangeable** JWT (same claims,
  same signing) and serves the iframe host page — and tell me exactly what to install to run the real
  WebForms shell instead.
- The token from the WebForms shell and from the simulator must be byte-for-byte interchangeable.

## Stack & versions

- **.NET 10** for the API (and the shell simulator if needed). C#.
- **Dapper** + **Microsoft.Data.SqlClient** for proc access.
- **Expo** (current stable SDK) / React Native + TypeScript, **web** target (react-native-web) + a
  mobile entry point.
- JWT: for the spike use **HS256 with a shared secret** via env var so issuer and API interoperate
  simply. In `PROOF.md`, note production should move to **RS256 + a JWKS endpoint** published by the
  shell — but don't build that now.

## Example domain (keep it trivial) — used only if I don't supply a real proc

A `poc.Customers` table and `poc.usp_ListCustomers` returning customers **plus a derived `Tier`
column computed in SQL** (e.g. from a spend/total field). The derived column is the point: business
logic living in the proc, reused unchanged by the new API.

## Repository layout

```
/poc-migration
  /db                 -- poc-schema table + proc + seed (idempotent) + teardown.sql; applied via sqlcmd
  /api                -- .NET 10 Web API: JWT validation, Dapper -> proc, CORS
  /legacy-shell       -- REAL WebForms .aspx + OWIN token endpoint + README (run on Windows/IIS)
  /legacy-shell-sim   -- minimal ASP.NET Core fallback: mints same JWT, serves iframe host page
  /app                -- Expo RN app: embedded web mode (token via postMessage) + mobile entry
  run.md / run.ps1    -- ordered commands to bring the whole stack up (no Docker)
  verify.ps1 / verify.sh -- headless end-to-end proof (see below)
  README.md           -- prerequisites + how to run + manual browser steps for the visual proof
  PROOF.md            -- maps each acceptance criterion to evidence + how to confirm
```

## Deliverables

1. All projects wired and **actually running** — not scaffolded and left broken.
2. An ordered, no-Docker run sequence (`dotnet run` for api + shell, `npx expo` for the app, `sqlcmd`
   for the db scripts), in `run.md` (and a `run.ps1` for Windows).
3. **A headless verify script** (`verify.ps1`, plus `verify.sh` if convenient) that proves the
   non-visual chain automatically: applies the db scripts, starts dependencies, requests a token from
   the shell, calls the API **without** a token (expect 401), with a **tampered** token (expect 401),
   and with a **valid** token (expect 200 + the proc's derived `Tier` field present). Print PASS/FAIL
   per check; non-zero exit on any failure.
4. `README.md` with copy-paste run steps and the **manual browser steps** for the visual proof: open
   the shell page → iframe loads the Expo web app → token handed over via `postMessage` → app renders
   proc data from the API. State what success looks like.
5. `PROOF.md` mapping criteria → evidence, plus a `db/teardown.sql` to remove all PoC objects.

## Non-negotiables (do not shortcut)

- The proc call, JWT validation, and cross-origin `postMessage` handoff must be **real**. Do not mock
  the database, stub token validation, or hard-code the data the iframe shows.
- The 401 path must actually reject, not a comment claiming it would.
- Nothing outside the `poc` schema is created/altered/read/deleted (except metadata checks).
- JWT secret in **env var**, never committed. Provide `.env.example`. Token in memory only in the app.

## Out of scope (resist scope creep)

Refresh tokens, multiple modules, production hardening, the reverse-proxy alternative, EF, CI, a real
Forms Auth user store (a stub login that issues the JWT is fine), UI styling beyond "data is visibly
on screen." Note refresh tokens and RS256/JWKS in `PROOF.md` as the obvious next steps.

## Working agreement

- Plan briefly, then build. **Run it and iterate until the verify script passes and the browser flow
  works.** Don't declare done until you've actually executed the end-to-end path.
- If a prerequisite or platform limit blocks a step, stop and tell me precisely what's needed — don't
  fake a result to look finished.
- When done, give me: the exact commands to run it myself, the verify-script output, and a 5–10 line
  summary of what was proven and what remains genuinely unproven.
