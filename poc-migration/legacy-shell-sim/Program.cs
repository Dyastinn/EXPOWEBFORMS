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
            body   { font-family: sans-serif; margin: 0; padding: 16px; background: #f4f4f4; }
            header { background: #1a3c5e; color: #fff; padding: 12px 16px; border-radius: 4px; margin-bottom: 12px; }
            .mode  { font-size: 0.75rem; opacity: 0.8; }
            iframe { width: 100%; height: 600px; border: 2px solid #1a3c5e; border-radius: 4px; background: #fff; }
            #status { font-size: 0.85rem; color: #555; margin-bottom: 8px; }
          </style>
        </head>
        <body>
          <header>
            <strong>Legacy WebForms Shell</strong> &mdash; Logged in as <em>Demo User</em>
            <div class="mode">{{modeLabel}}</div>
          </header>
          <div id="status">Fetching JWT…</div>
          <iframe id="expo-frame" src="{{iframeSrc}}" style="width:100%;height:600px;border:2px solid #1a3c5e;border-radius:4px;background:#fff;"></iframe>

          <script>
            var frame        = document.getElementById('expo-frame');
            var targetOrigin = {{targetOriginJs}};
            var pendingToken = null;
            var frameReady   = false;

            function trySendToken() {
              if (pendingToken && frameReady) {
                frame.contentWindow.postMessage({ type: 'AUTH_TOKEN', token: pendingToken }, targetOrigin);
                document.getElementById('status').textContent = 'Expo app loaded ✓';
              }
            }

            frame.addEventListener('load', function() {
              frameReady = true;
              document.getElementById('status').textContent = pendingToken
                ? 'Expo loaded. Sending token…'
                : 'Expo loaded. Waiting for token…';
              trySendToken();
            });

            fetch('/token', { method: 'POST' })
              .then(function(r) { return r.json(); })
              .then(function(data) {
                pendingToken = data.access_token;
                document.getElementById('status').textContent = frameReady
                  ? 'Token obtained. Sending…'
                  : 'Token obtained. Waiting for Expo to load…';
                trySendToken();
              })
              .catch(function(err) {
                document.getElementById('status').textContent = 'Token fetch failed: ' + err;
              });
          </script>
        </body>
        </html>
        """;

    return Results.Content(html, "text/html");
});

app.Run();
