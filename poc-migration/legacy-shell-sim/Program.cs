using System.Net.Http.Headers;

// EXPO_APP_URL controls where the iframe points:
//   "http://localhost:8081"  → dev mode  (Metro bundler, two-origin, no server-side auth gate)
//   "/app/"                  → same-origin mode  (static Expo behind the auth gate below)
// Default: dev mode so `dotnet run` works without a build step.
var expoAppUrl   = Environment.GetEnvironmentVariable("EXPO_APP_URL") ?? "http://localhost:8081";
var isSameOrigin = expoAppUrl.StartsWith('/');

// API base URL used to call /api/auth/token (issuance) and /api/auth/validate (gate check).
var apiBaseUrl  = Environment.GetEnvironmentVariable("API_BASE_URL")  ?? "http://localhost:5050";

// SHELL_API_KEY — pre-shared credential sent to POST /api/auth/token so the API
// can confirm this shell is an authorised caller before issuing a JWT.
var shellApiKey = Environment.GetEnvironmentVariable("SHELL_API_KEY") ?? "poc-shell-api-key-change-in-production";

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddCors(opts =>
    opts.AddDefaultPolicy(p => p.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod()));

// IHttpClientFactory is used by the /app/ auth gate to call the API validate endpoint.
builder.Services.AddHttpClient("api", client =>
{
    client.BaseAddress = new Uri(apiBaseUrl.TrimEnd('/') + "/");
    client.DefaultRequestHeaders.Accept.Add(
        new MediaTypeWithQualityHeaderValue("application/json"));
});

var app = builder.Build();
app.UseCors();

// Serve /app/* as static files from wwwroot/app/ (same-origin mode).
app.UseDefaultFiles();
app.UseStaticFiles();

// POST /token — proxies to POST /api/auth/token so the API is the sole JWT issuer.
// The shell no longer holds JWT_SECRET; it authenticates to the API with SHELL_API_KEY.
app.MapPost("/token", async (IHttpClientFactory factory) =>
{
    var http = factory.CreateClient("api");
    using var req = new HttpRequestMessage(HttpMethod.Post, "api/auth/token");
    req.Headers.Add("X-Api-Key", shellApiKey);

    try
    {
        var apiResp = await http.SendAsync(req);
        var body    = await apiResp.Content.ReadAsStringAsync();
        return Results.Content(body, "application/json", statusCode: (int)apiResp.StatusCode);
    }
    catch (Exception ex)
    {
        return Results.Problem($"Token service unavailable: {ex.Message}", statusCode: 503);
    }
});

// GET / — legacy shell host page.
app.MapGet("/", () =>
{
    var iframeSrc      = isSameOrigin ? "/app/" : expoAppUrl;
    // postMessage targetOrigin: same-origin uses the page's own origin; dev uses the Metro URL.
    var targetOriginJs = isSameOrigin ? "window.location.origin" : $"'{expoAppUrl}'";
    var modeLabel      = isSameOrigin
        ? "same-origin mode — Expo served from /app/"
        : "dev mode — Expo on http://localhost:8081 (Metro)";

    var html = $$"""
        <!DOCTYPE html>
        <html lang="en">
        <head>
          <meta charset="UTF-8" />
          <title>Legacy WebForms Shell (Simulator)</title>
          <style>
            *, *::before, *::after { box-sizing: border-box; }
            body   { font-family: sans-serif; margin: 0; padding: 16px; background: #f4f4f4; }
            header { background: #1a3c5e; color: #fff; padding: 12px 16px; border-radius: 4px; margin-bottom: 10px; }
            .mode  { font-size: 0.75rem; opacity: 0.8; }
            #status { font-size: 0.85rem; color: #555; margin-bottom: 8px; }
            #layout { display: flex; gap: 12px; align-items: flex-start; }
            #expo-frame { flex: 1; min-width: 0; height: 600px; border: 2px solid #1a3c5e; border-radius: 4px; background: #fff; }

            /* Debug panel */
            #dbg { width: 380px; flex-shrink: 0; background: #1e1e2e; border: 1px solid #3a3a5c;
                   border-radius: 6px; font-family: monospace; font-size: 12px; overflow: hidden; }
            #dbg-header { background: #2a2a40; color: #7c8cf8; padding: 8px 12px; cursor: pointer;
                          display: flex; justify-content: space-between; align-items: center; font-weight: 700;
                          font-size: 11px; letter-spacing: 0.8px; text-transform: uppercase; user-select: none; }
            #dbg-body { padding: 12px; max-height: 580px; overflow-y: auto; }
            .dbg-section { color: #7c8cf8; font-weight: 700; font-size: 10px; text-transform: uppercase;
                           letter-spacing: 0.8px; margin: 10px 0 4px; }
            .dbg-section:first-child { margin-top: 0; }
            .dbg-row { display: flex; gap: 8px; border-bottom: 1px solid #2a2a40; padding: 3px 0; }
            .dbg-key { color: #9cdcfe; width: 120px; flex-shrink: 0; }
            .dbg-val { color: #ce9178; flex: 1; word-break: break-all; }
            .dbg-jwt { color: #4ec9b0; background: #252540; padding: 6px; border-radius: 4px;
                       word-break: break-all; font-size: 10px; margin: 4px 0; }
            .dbg-pre { background: #252540; color: #b5cea8; padding: 8px; border-radius: 4px;
                       white-space: pre; overflow-x: auto; font-size: 11px; margin: 4px 0; }
            .log-entry { display: flex; gap: 8px; border-bottom: 1px solid #2a2a40; padding: 3px 0; }
            .log-ts  { color: #5a5a7a; width: 86px; flex-shrink: 0; font-size: 10px; }
            .log-msg { flex: 1; }
            .log-detail { color: #6a9955; font-size: 10px; }
            #dbg-log { max-height: 180px; overflow-y: auto; }
          </style>
        </head>
        <body>
          <header>
            <strong>Legacy WebForms Shell (Sim)</strong> &mdash; Logged in as <em>Demo User</em>
            <div class="mode">{{modeLabel}}</div>
          </header>
          <div id="status">Fetching JWT…</div>

          <div id="layout">
            <iframe id="expo-frame" src="{{iframeSrc}}"></iframe>

            <!-- Shell Debug Panel -->
            <div id="dbg">
              <div id="dbg-header" onclick="document.getElementById('dbg-body').style.display=document.getElementById('dbg-body').style.display==='none'?'block':'none'">
                <span>Shell Debug</span><span id="dbg-toggle">▲</span>
              </div>
              <div id="dbg-body">
                <div class="dbg-section">Identity</div>
                <div class="dbg-row"><span class="dbg-key">Shell origin</span><span class="dbg-val" id="d-origin"></span></div>
                <div class="dbg-row"><span class="dbg-key">Mode</span><span class="dbg-val">{{modeLabel}}</span></div>
                <div class="dbg-row"><span class="dbg-key">iframe src</span><span class="dbg-val">{{iframeSrc}}</span></div>
                <div class="dbg-row"><span class="dbg-key">postMessage target</span><span class="dbg-val" id="d-target"></span></div>

                <div class="dbg-section">Token Flow</div>
                <div class="dbg-row"><span class="dbg-key">Token endpoint</span><span class="dbg-val">POST /token → POST /api/auth/token</span></div>
                <div class="dbg-row"><span class="dbg-key">Auth method</span><span class="dbg-val">X-Api-Key header (SHELL_API_KEY)</span></div>
                <div class="dbg-row"><span class="dbg-key">JWT_SECRET held</span><span class="dbg-val" style="color:#e57373">NO — API only</span></div>

                <div class="dbg-section">JWT Received</div>
                <div class="dbg-jwt" id="d-jwt">(waiting…)</div>

                <div class="dbg-section">Decoded Payload</div>
                <div class="dbg-pre" id="d-payload">(waiting…)</div>

                <div class="dbg-section">postMessage Sent</div>
                <div class="dbg-row"><span class="dbg-key">Sent at</span><span class="dbg-val" id="d-pm-time">—</span></div>
                <div class="dbg-row"><span class="dbg-key">targetOrigin</span><span class="dbg-val" id="d-pm-target">—</span></div>
                <div class="dbg-row"><span class="dbg-key">Payload type</span><span class="dbg-val" id="d-pm-type">—</span></div>

                <div class="dbg-section">Event Log</div>
                <div id="dbg-log"></div>
              </div>
            </div>
          </div>

          <script>
            var frame        = document.getElementById('expo-frame');
            var targetOrigin = {{targetOriginJs}};
            var pendingToken = null;
            var frameReady   = false;

            // ── Debug helpers ─────────────────────────────────────────────
            document.getElementById('d-origin').textContent = window.location.origin;
            document.getElementById('d-target').textContent =
              typeof targetOrigin === 'string' ? targetOrigin : window.location.origin;

            function dbgLog(msg, detail) {
              var ts  = new Date().toISOString().slice(11, 23);
              var row = document.createElement('div');
              row.className = 'log-entry';
              row.innerHTML = '<span class="log-ts">' + ts + '</span>'
                + '<span class="log-msg">' + msg
                + (detail ? '<div class="log-detail">' + detail + '</div>' : '')
                + '</span>';
              var log = document.getElementById('dbg-log');
              log.appendChild(row);
              log.scrollTop = log.scrollHeight;
            }

            function dbgSetJwt(token) {
              document.getElementById('d-jwt').textContent = token;
              try {
                var parts   = token.split('.');
                var payload = JSON.parse(atob(parts[1].replace(/-/g,'+').replace(/_/g,'/')));
                if (typeof payload.exp === 'number') {
                  var expDate = new Date(payload.exp * 1000);
                  var secsLeft = Math.round((expDate - Date.now()) / 1000);
                  payload._exp_readable = expDate.toLocaleTimeString()
                    + ' (' + Math.floor(secsLeft/60) + 'm ' + (secsLeft%60) + 's remaining)';
                }
                if (typeof payload.iat === 'number')
                  payload._iat_readable = new Date(payload.iat * 1000).toLocaleTimeString();
                document.getElementById('d-payload').textContent = JSON.stringify(payload, null, 2);
              } catch(e) {
                document.getElementById('d-payload').textContent = 'decode error: ' + e;
              }
            }

            // ── postMessage flow ──────────────────────────────────────────
            function trySendToken() {
              if (pendingToken && frameReady) {
                frame.contentWindow.postMessage({ type: 'AUTH_TOKEN', token: pendingToken }, targetOrigin);
                var sentAt = new Date().toISOString().slice(11, 23);
                document.getElementById('d-pm-time').textContent   = sentAt;
                document.getElementById('d-pm-target').textContent = typeof targetOrigin === 'string' ? targetOrigin : window.location.origin;
                document.getElementById('d-pm-type').textContent   = 'AUTH_TOKEN';
                dbgLog('postMessage sent', 'type=AUTH_TOKEN  target=' + (typeof targetOrigin === 'string' ? targetOrigin : window.location.origin));
                document.getElementById('status').textContent = 'Token sent via postMessage ✓';
              }
            }

            frame.addEventListener('load', function() {
              frameReady = true;
              dbgLog('iframe load event fired', 'src={{iframeSrc}}');
              document.getElementById('status').textContent = pendingToken
                ? 'Expo loaded. Sending token…'
                : 'Expo loaded. Waiting for token…';
              trySendToken();
            });

            dbgLog('Fetching token from API…', 'POST /token');
            fetch('/token', { method: 'POST' })
              .then(function(r) {
                dbgLog('Shell /token response', 'HTTP ' + r.status);
                return r.json();
              })
              .then(function(data) {
                pendingToken = data.access_token;
                dbgSetJwt(data.access_token);
                dbgLog('JWT stored in shell memory (pending postMessage)');
                document.getElementById('status').textContent = frameReady
                  ? 'Token obtained. Sending…'
                  : 'Token obtained. Waiting for Expo to load…';
                trySendToken();
              })
              .catch(function(err) {
                dbgLog('Token fetch FAILED', String(err));
                document.getElementById('status').textContent = 'Token fetch failed: ' + err;
              });
          </script>
        </body>
        </html>
        """;

    return Results.Content(html, "text/html");
});

app.Run();
