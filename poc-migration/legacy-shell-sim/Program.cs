using System.IdentityModel.Tokens.Jwt;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text;
using Microsoft.IdentityModel.Tokens;

// JWT secret — must be set in production via environment variable.
// In Development a hardcoded fallback lets `dotnet run` work without extra setup.
var isDev = string.Equals(
    Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ?? "Production",
    "Development",
    StringComparison.OrdinalIgnoreCase);

var jwtSecret = Environment.GetEnvironmentVariable("JWT_SECRET")
    ?? (isDev
        ? "dev-only-secret-change-in-production-32chars!"
        : throw new InvalidOperationException("JWT_SECRET environment variable is not set."));

// EXPO_APP_URL controls where the iframe points:
//   "http://localhost:8081"  → dev mode  (Metro bundler, two-origin, no server-side auth gate)
//   "/app/"                  → same-origin mode  (static Expo behind the auth gate below)
// Default: dev mode so `dotnet run` works without a build step.
var expoAppUrl   = Environment.GetEnvironmentVariable("EXPO_APP_URL") ?? "http://localhost:8081";
var isSameOrigin = expoAppUrl.StartsWith('/');

// API base URL used to call /api/auth/validate for server-side token enforcement.
var apiBaseUrl = Environment.GetEnvironmentVariable("API_BASE_URL") ?? "http://localhost:5050";

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

// ── Iframe auth gate (/app/* — same-origin static export only) ────────────────
// For every entry-point request (the index.html), require a bearer token supplied
// as a query parameter.  The token is validated against the API; if valid a session
// cookie is issued so subsequent sub-resource requests (JS bundles, images) pass
// through freely — they are useless without the already-delivered HTML.
// Dev-mode (Metro on :8081) skips this gate because Metro has no middleware layer;
// in that mode the Expo client still reads the token from the URL client-side, and
// the API validates it on every authenticated call.
if (isSameOrigin)
{
    app.UseWhen(
        ctx => ctx.Request.Path.StartsWithSegments("/app"),
        branch => branch.Use(async (ctx, next) =>
        {
            var path    = ctx.Request.Path.Value ?? "";
            var isEntry = path is "/app" or "/app/"
                          || path.EndsWith("/app/index.html", StringComparison.OrdinalIgnoreCase);

            // Sub-resources (bundles, images) bypass the token check once index.html is served.
            if (!isEntry) { await next(); return; }

            // Prefer the query-param token (first iframe load); fall back to session cookie.
            var token      = ctx.Request.Query["token"].FirstOrDefault();
            var fromCookie = string.IsNullOrEmpty(token);
            if (fromCookie)
                token = ctx.Request.Cookies["expo_session"];

            if (string.IsNullOrEmpty(token))
            {
                await WriteUnauthorized(ctx, "Missing authorization token.");
                return;
            }

            // Validate the token against the API — enforced server-side.
            var http = ctx.RequestServices
                          .GetRequiredService<IHttpClientFactory>()
                          .CreateClient("api");
            http.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", token);

            HttpResponseMessage apiResponse;
            try
            {
                apiResponse = await http.GetAsync("api/auth/validate");
            }
            catch (Exception ex)
            {
                ctx.Response.StatusCode  = StatusCodes.Status503ServiceUnavailable;
                ctx.Response.ContentType = "text/plain";
                await ctx.Response.WriteAsync(
                    $"Authorization service unavailable: {ex.Message}");
                return;
            }

            if (!apiResponse.IsSuccessStatusCode)
            {
                if (fromCookie)
                    ctx.Response.Cookies.Delete("expo_session");

                await WriteUnauthorized(ctx, "Token is invalid or expired.");
                return;
            }

            // Token validated — issue a session cookie for sub-resource requests.
            if (!fromCookie)
            {
                ctx.Response.Cookies.Append("expo_session", token, new CookieOptions
                {
                    HttpOnly = true,
                    SameSite = SameSiteMode.Strict,
                    Secure   = false,   // set true behind HTTPS in production
                    MaxAge   = TimeSpan.FromHours(1),
                    Path     = "/app",
                });
            }

            // Restrict framing: only this shell origin may embed the Expo app.
            ctx.Response.Headers.ContentSecurityPolicy =
                $"frame-ancestors {ctx.Request.Scheme}://{ctx.Request.Host}";

            await next();
        }));
}

// Serve /app/* as static files from wwwroot/app/ (same-origin mode).
app.UseDefaultFiles();
app.UseStaticFiles();

// POST /token — issues a JWT interchangeable with the real WebForms shell's OWIN endpoint.
app.MapPost("/token", () =>
{
    var key   = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSecret));
    var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
    var now   = DateTime.UtcNow;

    var token = new JwtSecurityToken(
        issuer:             "poc-legacy-shell",
        audience:           "poc-api",
        claims:             [
            new Claim(JwtRegisteredClaimNames.Sub,  "demo-user"),
            new Claim(JwtRegisteredClaimNames.Name, "Demo User"),
            new Claim(JwtRegisteredClaimNames.Jti,  Guid.NewGuid().ToString()),
        ],
        notBefore:          now,
        expires:            now.AddHours(1),
        signingCredentials: creds);

    var tokenString = new JwtSecurityTokenHandler().WriteToken(token);
    return Results.Ok(new { access_token = tokenString, token_type = "Bearer", expires_in = 3600 });
});

// GET / — legacy shell host page.
// The iframe src is intentionally left blank in the initial HTML; client-side JS
// fetches the JWT first, then sets the src with the token as a query parameter so
// the auth gate sees it on the very first request to the Expo entry point.
app.MapGet("/", () =>
{
    var iframeSrc  = isSameOrigin ? "/app/" : expoAppUrl;
    var modeLabel  = isSameOrigin
        ? "same-origin mode — Expo served from /app/ (static export, server-side auth gate active)"
        : "dev mode — Expo on http://localhost:8081 (Metro, client-side token only)";

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
          <div id="status">Fetching JWT from token endpoint&hellip;</div>
          <!-- src is set by JS only after the token is obtained -->
          <iframe id="expo-frame" style="display:none;width:100%;height:600px;border:2px solid #1a3c5e;border-radius:4px;background:#fff;"></iframe>

          <script>
            fetch('/token', { method: 'POST' })
              .then(function(r) { return r.json(); })
              .then(function(data) {
                document.getElementById('status').textContent = 'JWT obtained. Loading Expo app…';
                var frame = document.getElementById('expo-frame');
                frame.src = '{{iframeSrc}}?token=' + encodeURIComponent(data.access_token);
                frame.style.display = '';
              })
              .catch(function(err) {
                document.getElementById('status').textContent = 'Token fetch failed: ' + err;
              });

            document.getElementById('expo-frame').addEventListener('load', function() {
              document.getElementById('status').textContent = 'Expo app loaded ✓';
            });
          </script>
        </body>
        </html>
        """;

    return Results.Content(html, "text/html");
});

app.Run();

// ── Helpers ───────────────────────────────────────────────────────────────────

static async Task WriteUnauthorized(HttpContext ctx, string reason)
{
    ctx.Response.StatusCode  = StatusCodes.Status401Unauthorized;
    ctx.Response.ContentType = "text/html";
    // $$""" — double-$ raw string: single { } are literal (correct for CSS rules);
    // {{reason}} is the C# interpolation.
    await ctx.Response.WriteAsync(
        $$"""
        <!DOCTYPE html>
        <html lang="en">
        <head>
          <meta charset="UTF-8" />
          <title>401 Unauthorized</title>
          <style>
            body { font-family: sans-serif; display: flex; align-items: center;
                   justify-content: center; min-height: 100vh; margin: 0; background: #f0f2f5; }
            .box { text-align: center; padding: 40px; background: #fff; border-radius: 8px;
                   box-shadow: 0 2px 8px rgba(0,0,0,.12); }
            h1   { color: #c0392b; margin: 0 0 12px; }
            p    { color: #666; margin: 4px 0; }
          </style>
        </head>
        <body>
          <div class="box">
            <h1>401 Unauthorized</h1>
            <p>{{reason}}</p>
            <p>A valid authorization token is required to access this application.</p>
          </div>
        </body>
        </html>
        """);
}
