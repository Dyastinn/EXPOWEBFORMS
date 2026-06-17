using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

// ── Configuration ──────────────────────────────────────────────────────────────
var expoAppUrl   = Environment.GetEnvironmentVariable("EXPO_APP_URL")         ?? "http://localhost:8081";
var isSameOrigin = expoAppUrl.StartsWith('/');
var apiBaseUrl   = Environment.GetEnvironmentVariable("API_BASE_URL")          ?? "http://localhost:5050";
var loginApiKey  = Environment.GetEnvironmentVariable("LEGACY_SHELL_API_KEY") ?? "legacy-shell-v1";

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddCors(opts =>
    opts.AddDefaultPolicy(p => p.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod()));
builder.Services.AddDistributedMemoryCache();
builder.Services.AddSession(opts =>
{
    opts.Cookie.HttpOnly   = true;
    opts.Cookie.IsEssential = true;
    opts.IdleTimeout       = TimeSpan.FromMinutes(60);
});
builder.Services.AddHttpClient("api", client =>
{
    client.BaseAddress = new Uri(apiBaseUrl.TrimEnd('/') + "/");
    client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
});

var app = builder.Build();
app.UseCors();
app.UseSession();
app.UseDefaultFiles();
app.UseStaticFiles();

// ── GET /login ─────────────────────────────────────────────────────────────────
app.MapGet("/login", (HttpContext ctx) =>
{
    if (ctx.Session.GetString("auth_jwt") != null)
        return Results.Redirect("/");
    return Results.Content(LoginHtml(), "text/html");
});

// ── POST /login ────────────────────────────────────────────────────────────────
app.MapPost("/login", async (HttpContext ctx, IHttpClientFactory factory) =>
{
    var form     = await ctx.Request.ReadFormAsync();
    var username = form["username"].ToString().Trim();
    var password = form["password"].ToString();

    if (string.IsNullOrEmpty(username) || string.IsNullOrEmpty(password))
        return Results.Content(LoginHtml("Username and password are required."), "text/html");

    var http    = factory.CreateClient("api");
    var payload = JsonSerializer.Serialize(new { username, password });
    using var req = new HttpRequestMessage(HttpMethod.Post, "api/auth/login");
    req.Content = new StringContent(payload, Encoding.UTF8, "application/json");
    req.Headers.Add("X-Api-Key", loginApiKey);

    HttpResponseMessage resp;
    string body;
    try
    {
        resp = await http.SendAsync(req);
        body = await resp.Content.ReadAsStringAsync();
    }
    catch (Exception ex)
    {
        return Results.Content(LoginHtml("Could not reach the API: " + HtmlEncode(ex.Message)), "text/html");
    }

    if (resp.StatusCode == HttpStatusCode.OK)
    {
        using var doc = JsonDocument.Parse(body);
        if (doc.RootElement.TryGetProperty("access_token", out var tokEl))
        {
            ctx.Session.SetString("auth_jwt", tokEl.GetString()!);
            ctx.Session.SetString("username",  username);
            return Results.Redirect("/");
        }
        return Results.Content(LoginHtml("Unexpected response from login service."), "text/html");
    }

    return (int)resp.StatusCode switch
    {
        401 => Results.Content(LoginHtml("Invalid username or password."), "text/html"),
        429 => Results.Content(LoginHtml("Too many attempts. Please wait a moment and try again."), "text/html"),
        _   => Results.Content(LoginHtml($"Login failed (HTTP {(int)resp.StatusCode})."), "text/html"),
    };
});

// ── POST /logout ───────────────────────────────────────────────────────────────
app.MapPost("/logout", (HttpContext ctx) =>
{
    ctx.Session.Clear();
    return Results.Redirect("/login");
});

// ── GET / — auth-protected shell page ─────────────────────────────────────────
app.MapGet("/", (HttpContext ctx) =>
{
    var jwt = ctx.Session.GetString("auth_jwt");
    if (jwt == null) return Results.Redirect("/login");

    var username       = HtmlEncode(ctx.Session.GetString("username") ?? "User");
    var iframeSrc      = isSameOrigin ? "/app/" : expoAppUrl;
    var targetOriginJs = isSameOrigin ? "window.location.origin" : $"'{expoAppUrl}'";
    var modeLabel      = isSameOrigin
        ? "same-origin mode — Expo served from /app/"
        : "dev mode — Expo on http://localhost:8081 (Metro)";
    var jwtJs          = JsonSerializer.Serialize(jwt); // JSON-encoded string literal incl. quotes

    var html = $$"""
        <!DOCTYPE html>
        <html lang="en">
        <head>
          <meta charset="UTF-8" />
          <title>Legacy WebForms Shell (Simulator)</title>
          <style>
            *, *::before, *::after { box-sizing: border-box; }
            body   { font-family: sans-serif; margin: 0; padding: 16px; background: #f4f4f4; }
            header {
              background: #1a3c5e; color: #fff; padding: 12px 16px;
              border-radius: 4px; margin-bottom: 10px;
              display: flex; align-items: center; gap: 8px; flex-wrap: wrap;
            }
            .logout-btn {
              margin-left: auto; font-size: 12px; color: #a8c4e0;
              text-decoration: underline; cursor: pointer;
              background: none; border: none; padding: 0;
            }
            .logout-btn:hover { color: #fff; }
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
            .log-ts    { color: #5a5a7a; width: 86px; flex-shrink: 0; font-size: 10px; }
            .log-msg   { flex: 1; }
            .log-detail { color: #6a9955; font-size: 10px; }
            #dbg-log { max-height: 180px; overflow-y: auto; }
          </style>
        </head>
        <body>
          <header>
            <strong>Legacy WebForms Shell (Sim)</strong> &mdash; Logged in as
            <em>{{username}}</em>
            &nbsp;(ASP.NET Core .NET 10 Simulator)
            <form method="post" action="/logout" style="margin:0;margin-left:auto">
              <button type="submit" class="logout-btn">Sign out</button>
            </form>
          </header>
          <div id="status">Loading…</div>

          <div id="layout">
            <iframe id="expo-frame" src="{{iframeSrc}}"></iframe>

            <div id="dbg">
              <div id="dbg-header" onclick="document.getElementById('dbg-body').style.display=document.getElementById('dbg-body').style.display==='none'?'block':'none'">
                <span>Shell Debug</span><span>▲</span>
              </div>
              <div id="dbg-body">
                <div class="dbg-section">Identity</div>
                <div class="dbg-row"><span class="dbg-key">Shell origin</span><span class="dbg-val" id="d-origin"></span></div>
                <div class="dbg-row"><span class="dbg-key">Mode</span><span class="dbg-val">{{modeLabel}}</span></div>
                <div class="dbg-row"><span class="dbg-key">Framework</span><span class="dbg-val">ASP.NET Core .NET 10 (Simulator)</span></div>
                <div class="dbg-row"><span class="dbg-key">iframe src</span><span class="dbg-val">{{iframeSrc}}</span></div>
                <div class="dbg-row"><span class="dbg-key">postMessage target</span><span class="dbg-val" id="d-target"></span></div>

                <div class="dbg-section">Token Flow</div>
                <div class="dbg-row"><span class="dbg-key">Auth method</span><span class="dbg-val">POST /login → POST /api/auth/login (server-side)</span></div>
                <div class="dbg-row"><span class="dbg-key">Token storage</span><span class="dbg-val">ASP.NET Core Session["auth_jwt"]</span></div>
                <div class="dbg-row"><span class="dbg-key">JWT_SECRET held</span><span class="dbg-val" style="color:#e57373">NO — API only</span></div>

                <div class="dbg-section">JWT Received</div>
                <div class="dbg-jwt" id="d-jwt">(loading…)</div>

                <div class="dbg-section">Decoded Payload</div>
                <div class="dbg-pre" id="d-payload">(loading…)</div>

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
                  var expDate  = new Date(payload.exp * 1000);
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

            // JWT injected server-side from session — no client-side API call needed.
            var __shellJwt = {{jwtJs}};
            if (__shellJwt) {
              pendingToken = __shellJwt;
              dbgSetJwt(__shellJwt);
              dbgLog('JWT loaded from Session', 'issued via POST /api/auth/login');
              document.getElementById('status').textContent = frameReady
                ? 'Token obtained. Sending…'
                : 'Token obtained. Waiting for Expo to load…';
              trySendToken();
            } else {
              dbgLog('No JWT in Session — redirecting to login');
              document.getElementById('status').textContent = 'No token — redirecting…';
              window.location.href = '/login';
            }
          </script>
        </body>
        </html>
        """;

    return Results.Content(html, "text/html");
});

app.Run();

// ── Helpers ────────────────────────────────────────────────────────────────────
static string HtmlEncode(string s) =>
    s.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;")
     .Replace("\"", "&quot;").Replace("'", "&#39;");

static string LoginHtml(string? error = null)
{
    var errorBlock = error is null ? "" : $"""
        <span class="login-error">{HtmlEncode(error)}</span>
        """;
    return $$"""
        <!DOCTYPE html>
        <html lang="en">
        <head>
          <meta charset="UTF-8" />
          <title>Sign in — Legacy Shell (Sim)</title>
          <style>
            *, *::before, *::after { box-sizing: border-box; }
            body {
              font-family: sans-serif; margin: 0; background: #f4f4f4;
              display: flex; justify-content: center; align-items: center;
              min-height: 100vh;
            }
            .login-card {
              background: #fff; border-radius: 10px; padding: 36px 32px;
              width: 100%; max-width: 380px;
              border: 1px solid #dde1e7;
              box-shadow: 0 4px 14px rgba(0,0,0,0.08);
            }
            .login-title    { font-size: 20px; font-weight: 700; color: #1a3c5e; margin: 0 0 4px; }
            .login-subtitle { font-size: 13px; color: #888; margin: 0 0 26px; }
            .field          { margin-bottom: 14px; }
            label           { display: block; font-size: 13px; font-weight: 600; color: #444; margin-bottom: 4px; }
            .text-input {
              width: 100%; padding: 9px 10px; font-size: 14px;
              border: 1px solid #ccc; border-radius: 4px;
              background: #fafafa; outline: none;
            }
            .text-input:focus { border-color: #1a3c5e; box-shadow: 0 0 0 2px rgba(26,60,94,0.12); }
            .btn-primary {
              width: 100%; padding: 11px; font-size: 14px; font-weight: 700;
              background: #1a3c5e; color: #fff;
              border: none; border-radius: 4px; cursor: pointer; margin-top: 6px;
            }
            .btn-primary:hover  { background: #15304e; }
            .btn-primary:active { background: #0f2236; }
            .login-error {
              display: block; background: #fdecea; color: #c0392b;
              border: 1px solid #f5c6c6; border-radius: 4px;
              padding: 9px 12px; font-size: 13px; margin-bottom: 16px;
            }
          </style>
        </head>
        <body>
          <form method="post" action="/login">
            <div class="login-card">
              <p class="login-title">Legacy Shell (Sim)</p>
              <p class="login-subtitle">Sign in to continue &mdash; ASP.NET Core Simulator</p>

              {{errorBlock}}

              <div class="field">
                <label for="username">Username</label>
                <input id="username" name="username" type="text" class="text-input" autocomplete="username" />
              </div>
              <div class="field">
                <label for="password">Password</label>
                <input id="password" name="password" type="password" class="text-input" autocomplete="current-password" />
              </div>

              <button type="submit" class="btn-primary">Sign in</button>
            </div>
          </form>
        </body>
        </html>
        """;
}
