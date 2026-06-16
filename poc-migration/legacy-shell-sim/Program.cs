using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.IdentityModel.Tokens;

var jwtSecret = Environment.GetEnvironmentVariable("JWT_SECRET")
    ?? throw new InvalidOperationException("JWT_SECRET environment variable is not set.");

// EXPO_APP_URL controls where the iframe points:
//   "http://localhost:8081"  → dev mode  (Metro bundler, two-origin, CORS required)
//   "/app/"                  → same-origin mode  (static Expo served from /app/, CORS not needed)
// Default: dev mode so `dotnet run` works without a build step.
var expoAppUrl = Environment.GetEnvironmentVariable("EXPO_APP_URL") ?? "http://localhost:8081";
var isSameOrigin = expoAppUrl.StartsWith('/');

// The origin the Expo iframe will appear to come from:
//   same-origin mode → the shell itself  (http://localhost:5000)
//   dev mode         → the Metro server  (http://localhost:8081)
// This is placed into the HTML so the postMessage targetOrigin is always correct.
var expoOriginPlaceholder = isSameOrigin ? "%%SELF_ORIGIN%%" : expoAppUrl;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddCors(opts =>
    opts.AddDefaultPolicy(p => p.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod()));

var app = builder.Build();
app.UseCors();

// Serve /app/* as static files from wwwroot/app/ (same-origin mode).
// UseDefaultFiles must precede UseStaticFiles so /app/ → wwwroot/app/index.html.
app.UseDefaultFiles();
app.UseStaticFiles();

// POST /token — issues a JWT interchangeable with the real WebForms shell's OWIN endpoint
app.MapPost("/token", () =>
{
    var key     = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSecret));
    var creds   = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
    var now     = DateTime.UtcNow;

    var token = new JwtSecurityToken(
        issuer:             "poc-legacy-shell",
        audience:           "poc-api",
        claims:             [
            new Claim(JwtRegisteredClaimNames.Sub,  "demo-user"),
            new Claim(JwtRegisteredClaimNames.Name, "Demo User"),
            new Claim(JwtRegisteredClaimNames.Jti,  Guid.NewGuid().ToString())
        ],
        notBefore:          now,
        expires:            now.AddHours(1),
        signingCredentials: creds);

    var tokenString = new JwtSecurityTokenHandler().WriteToken(token);
    return Results.Ok(new { access_token = tokenString, token_type = "Bearer", expires_in = 3600 });
});

// GET / — legacy shell host page with iframe + postMessage handoff
app.MapGet("/", (HttpContext ctx) =>
{
    // In same-origin mode the Expo app's origin equals the shell's own origin.
    // We compute it from the request so it works on any port.
    var selfOrigin  = $"{ctx.Request.Scheme}://{ctx.Request.Host}";
    var expoOrigin  = isSameOrigin ? selfOrigin : expoAppUrl;
    var iframeSrc   = isSameOrigin ? "/app/" : expoAppUrl;
    var modeLabel   = isSameOrigin
        ? "same-origin mode — Expo served from /app/ (static export)"
        : "dev mode — Expo on http://localhost:8081 (Metro)";

    var html = $$"""
        <!DOCTYPE html>
        <html lang="en">
        <head>
          <meta charset="UTF-8" />
          <title>Legacy WebForms Shell (Simulator)</title>
          <style>
            body { font-family: sans-serif; margin: 0; padding: 16px; background: #f4f4f4; }
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
          <iframe id="expo-frame" src="{{iframeSrc}}"></iframe>

          <script>
            const EXPO_ORIGIN = '{{expoOrigin}}';
            let pendingToken = null;
            let frameReady   = false;

            fetch('/token', { method: 'POST' })
              .then(r => r.json())
              .then(({ access_token }) => {
                pendingToken = access_token;
                document.getElementById('status').textContent = 'JWT obtained. Waiting for Expo frame…';
                trySend();
              })
              .catch(err => {
                document.getElementById('status').textContent = 'Token fetch failed: ' + err;
              });

            window.addEventListener('message', function(event) {
              if (event.origin !== EXPO_ORIGIN) return;
              if (event.data?.type === 'EXPO_READY') {
                frameReady = true;
                document.getElementById('status').textContent = 'Expo frame ready. Sending JWT…';
                trySend();
              }
            });

            function trySend() {
              if (!pendingToken || !frameReady) return;
              document.getElementById('expo-frame')
                .contentWindow.postMessage({ type: 'AUTH_TOKEN', token: pendingToken }, EXPO_ORIGIN);
              document.getElementById('status').textContent = 'JWT handed to Expo frame via postMessage ✓';
            }

            document.getElementById('expo-frame').addEventListener('load', function() {
              frameReady = true;
              trySend();
            });
          </script>
        </body>
        </html>
        """;

    return Results.Content(html, "text/html");
});

app.Run();
