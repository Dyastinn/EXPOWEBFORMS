# Proof Map — Acceptance Criteria → Evidence

Each criterion maps to a specific file/endpoint and the exact command or browser step to confirm it.

---

## 1. Stored procedure with real business logic runs in SQL Server

**Criterion:** A stored procedure with a computed/derived field runs in SQL Server, standing in for
business logic that lives in a proc.

**Evidence:** `db/setup.sql` lines 20–36 — `poc.usp_ListCustomers` computes `Tier` via a `CASE`
expression on `TotalSpend` (Gold ≥ 10000, Silver ≥ 1000, Bronze otherwise).

**Confirm:**
```sql
EXEC poc.usp_ListCustomers;
-- Returns 5 rows; Tier column is Gold/Silver/Bronze based on TotalSpend
```
Or via sqlcmd:
```powershell
sqlcmd -S "ndessdev\ndessdev" -d "ndess_dev03" -E -Q "EXEC poc.usp_ListCustomers;"
```

---

## 2. .NET API calls the proc via Dapper (not EF, not inline SQL)

**Evidence:**
- `api/Controllers/CustomersController.cs` lines 14–16: calls `db.QueryAsync<Customer>` with
  `CommandType.StoredProcedure` and proc name `poc.usp_ListCustomers`.
- `api/api.csproj`: `Dapper 2.1.35` is the only ORM/data-access package — no EF anywhere.
- `api/Program.cs` line 32: connection is `Microsoft.Data.SqlClient.SqlConnection`.

**Confirm:**
```powershell
# Valid token -> 200 + JSON with Tier from proc
$t = ((Invoke-WebRequest http://localhost:5000/token -Method POST).Content | ConvertFrom-Json).access_token
Invoke-WebRequest http://localhost:5050/api/customers -Headers @{Authorization="Bearer $t"} | Select-Object -Expand Content
```

---

## 3. API validates JWT — no/tampered token → 401, valid → 200

**Evidence:** `api/Program.cs` lines 17–32 — `AddJwtBearer` with `ValidateIssuer`, `ValidateAudience`,
`ValidateIssuerSigningKey`, `ValidateLifetime`, `ClockSkew = Zero`.
`api/Controllers/CustomersController.cs` line 9: `[Authorize]`.

**Confirm:**
```powershell
# No token -> 401
Invoke-WebRequest http://localhost:5050/api/customers  # throws with 401

# Tampered token -> 401
Invoke-WebRequest http://localhost:5050/api/customers -Headers @{Authorization="Bearer eyJfake.eyJfake.invalidsig"}

# Valid token -> 200
$t = ((Invoke-WebRequest http://localhost:5000/token -Method POST).Content | ConvertFrom-Json).access_token
(Invoke-WebRequest http://localhost:5050/api/customers -Headers @{Authorization="Bearer $t"}).StatusCode  # 200
```
Or run `.\verify.ps1` — checks all three paths with PASS/FAIL output.

---

## 4. Legacy shell is the identity issuer; auth travels only as the token

**Evidence:**
- `legacy-shell-sim/Program.cs` lines 17–38: issues JWT (`poc-legacy-shell` as issuer) when `POST /token` is called.
- `api/Program.cs` line 21: `ValidIssuer = "poc-legacy-shell"` — API only accepts tokens from the shell.
- No session cookies cross the boundary; the API has no knowledge of the shell's session state.

**Confirm:** Token endpoint: `POST http://localhost:5000/token` → JWT; API consumes only that token.
The API has no endpoint that accepts a cookie or session ID.

**Real WebForms equivalent:** `legacy-shell/Handlers/TokenHandler.ashx.cs` — identical token contract.

---

## 5. Shell hosts Expo app in iframe and sends token via postMessage with strict targetOrigin

**Evidence:**
- `legacy-shell-sim/Program.cs` lines 52–80 (HTML served at `GET /`):
  - `<iframe src="http://localhost:8081">` embeds the Expo app.
  - `iframe.contentWindow.postMessage({ type:'AUTH_TOKEN', token }, EXPO_ORIGIN)` — `EXPO_ORIGIN` is
    the hardcoded constant `'http://localhost:8081'` (strict targetOrigin, not `'*'`).
  - Parent also listens for `EXPO_READY` message and verifies `event.origin !== EXPO_ORIGIN` before acting.

**Confirm:** Open http://localhost:5000, open DevTools → Console, observe postMessage call.
Or inspect Network tab — no cross-origin credential leakage.

---

## 6. Token stored in memory only — never in localStorage/sessionStorage

**Evidence:** `app/App.tsx` line 13:
```typescript
let inMemoryToken: string | null = null;
```
Comment at line 9–12 explains the XSS rationale. Search the file for `localStorage` or
`sessionStorage` — zero occurrences.

**Confirm:**
```javascript
// In the browser console while on http://localhost:8081:
Object.keys(localStorage)   // should not contain 'token' or any JWT data
Object.keys(sessionStorage) // same
```

---

## 7. Handoff and API call work across different origins; CORS scoped to Expo origin

**Criterion:** Shell (port 5000), Expo app (port 8081), API (port 5050) are different origins.
CORS on the API is scoped to the app origin only.

**Evidence:**
- `api/appsettings.json` line 7: `"AllowedOrigins": ["http://localhost:8081", "http://localhost:19006"]`.
- `api/Program.cs` lines 34–37: `WithOrigins(allowedOrigins)` — no wildcard.
- The entire flow works: shell at :5000 serves an iframe from :8081 which calls an API at :5050.

**Confirm:**
```powershell
$t = ((Invoke-WebRequest http://localhost:5000/token -Method POST).Content | ConvertFrom-Json).access_token
$r = Invoke-WebRequest http://localhost:5050/api/customers `
  -Headers @{ Authorization="Bearer $t"; Origin="http://localhost:8081" }
$r.Headers["Access-Control-Allow-Origin"]  # should print: http://localhost:8081
```
Try `Origin: http://localhost:9999` — the header will be absent (blocked).

---

## 8. Same Expo codebase runs as both web and mobile

**Evidence:**
- `app/App.tsx` lines 30–51: `Platform.OS === 'web'` branch handles iframe/postMessage.
- `app/App.tsx` lines 54–63: `else` branch (mobile) fetches a token directly from the shell-sim.
- `app/package.json`: React Native 0.85.3 + react-native-web are both present.
- `app/app.json`: `"web"` and `"android"`/`"ios"` targets all configured.
- `app/index.ts`: `registerRootComponent(App)` — single entry point for all platforms.

**Confirm web:** `npx expo start --web --port 8081` — opens in browser.
**Confirm mobile entry point:** `npx expo start --android` (needs Android emulator/device) or
`npx expo start --ios` (needs macOS + Xcode). The mobile branch calls `fetchMobileToken()` and
then `fetchCustomers()` — the same data flow, no postMessage.

---

## Next steps (out of scope for this spike)

1. **RS256 + JWKS endpoint** — The WebForms shell publishes a JWKS URL; the API validates against
   the public key fetched from it. This removes the shared-secret requirement entirely and is the
   correct production architecture.

2. **Refresh tokens** — Issue a short-lived access token (15 min) and a longer-lived refresh token.
   The shell handles refresh; the Expo app just receives updated access tokens via postMessage.

3. **Forms Authentication → JWT bridge** — In the real WebForms app, the token endpoint reads
   the FormsAuthentication ticket to extract the real user identity before minting the JWT.

4. **Reverse proxy option** — An alternative to the iframe/postMessage pattern is a reverse proxy
   (e.g., YARP or nginx) that routes `/api/*` requests from the WebForms app's domain to the new
   API, avoiding cross-origin entirely. Trade-off: more ops complexity, no origin isolation.

5. **Mobile auth flow** — Replace the dev-only direct token fetch with a proper OAuth 2.0 /
   PKCE flow for the mobile target.
