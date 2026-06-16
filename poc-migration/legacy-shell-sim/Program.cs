using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.IdentityModel.Tokens;

// JWT secret must match the API — both read from the same env var
var jwtSecret = Environment.GetEnvironmentVariable("JWT_SECRET")
    ?? throw new InvalidOperationException("JWT_SECRET environment variable is not set.");

var builder = WebApplication.CreateBuilder(args);

// Allow any origin on this dev shell so the browser can fetch /token
builder.Services.AddCors(opts =>
    opts.AddDefaultPolicy(p => p.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod()));

var app = builder.Build();
app.UseCors();

// POST /token  — issues a JWT signed with the shared HS256 secret.
// In production this would be the real Forms-Auth-backed OWIN endpoint on the WebForms host.
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

// GET /  — the "legacy shell" host page: iframe + postMessage handoff
app.MapGet("/", () => Results.Content("""
<!DOCTYPE html>
<html lang="en">
<head>
  <meta charset="UTF-8" />
  <title>Legacy WebForms Shell (Simulator)</title>
  <style>
    body { font-family: sans-serif; margin: 0; padding: 16px; background: #f4f4f4; }
    header { background: #1a3c5e; color: #fff; padding: 12px 16px; border-radius: 4px; margin-bottom: 12px; }
    iframe { width: 100%; height: 600px; border: 2px solid #1a3c5e; border-radius: 4px; background: #fff; }
    #status { font-size: 0.85rem; color: #555; margin-bottom: 8px; }
  </style>
</head>
<body>
  <header>
    <strong>Legacy WebForms Shell</strong> &mdash; Logged in as <em>Demo User</em>
    &nbsp;(ASP.NET Core simulator; real WebForms shell in /legacy-shell/)
  </header>
  <div id="status">Fetching JWT from token endpoint…</div>
  <iframe id="expo-frame" src="http://localhost:8081"></iframe>

  <script>
    const EXPO_ORIGIN = 'http://localhost:8081';
    let pendingToken = null;
    let frameReady   = false;

    // Step 1: fetch a JWT from our own /token endpoint
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

    // Step 2: listen for the Expo app to signal it is ready
    window.addEventListener('message', function(event) {
      // Verify origin strictly before acting on any message
      if (event.origin !== EXPO_ORIGIN) return;
      if (event.data?.type === 'EXPO_READY') {
        frameReady = true;
        document.getElementById('status').textContent = 'Expo frame ready. Sending JWT…';
        trySend();
      }
    });

    function trySend() {
      if (!pendingToken || !frameReady) return;
      // Strict targetOrigin — only the Expo web app at the known origin receives the token
      document.getElementById('expo-frame')
        .contentWindow.postMessage({ type: 'AUTH_TOKEN', token: pendingToken }, EXPO_ORIGIN);
      document.getElementById('status').textContent = 'JWT handed to Expo frame via postMessage ✓';
    }

    // Fallback: also send on iframe load in case EXPO_READY fired before token arrived
    document.getElementById('expo-frame').addEventListener('load', function() {
      frameReady = true;
      trySend();
    });
  </script>
</body>
</html>
""", "text/html"));

app.Run();
