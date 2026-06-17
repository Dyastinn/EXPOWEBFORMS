using System.Data;
using System.Reflection;
using System.Text;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Data.SqlClient;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi;
using PocApi.Auth;
using PocApi.Repositories;

var builder = WebApplication.CreateBuilder(args);

// ── Secrets — from env; dev-only fallbacks keep `dotnet run` working without setup ──────
var jwtSecret = Environment.GetEnvironmentVariable("JWT_SECRET")
    ?? (builder.Environment.IsDevelopment()
        ? "dev-only-secret-change-in-production-32chars!"
        : throw new InvalidOperationException("JWT_SECRET environment variable is not set."));

var jwtIssuer   = builder.Configuration["Jwt:Issuer"]!;
var jwtAudience = builder.Configuration["Jwt:Audience"]!;

// SHELL_API_KEY authenticates the WebForms shell to POST /api/auth/token (existing flow).
var shellApiKey = Environment.GetEnvironmentVariable("SHELL_API_KEY")
    ?? (builder.Environment.IsDevelopment()
        ? "poc-shell-api-key-change-in-production"
        : throw new InvalidOperationException("SHELL_API_KEY environment variable is not set."));

// Application-identification keys for ApiKeyMiddleware.
// Values are distributed to clients and are identifiers, not secrets.
// Set MOBILE_APP_API_KEY / LEGACY_SHELL_API_KEY in production to rotate them without code changes.
var mobileAppApiKey   = Environment.GetEnvironmentVariable("MOBILE_APP_API_KEY")   ?? "mobile-app-v1";
var legacyShellApiKey = Environment.GetEnvironmentVariable("LEGACY_SHELL_API_KEY") ?? "legacy-shell-v1";

// ── Authentication ───────────────────────────────────────────────────────────────────────
builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer           = true,
            ValidIssuer              = jwtIssuer,
            ValidateAudience         = true,
            ValidAudience            = jwtAudience,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey         = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSecret)),
            ValidateLifetime         = true,
            // 30-second allowance for clock drift; tighter than the 5-minute ASP.NET default.
            ClockSkew                = TimeSpan.FromSeconds(30),
        };
    });

builder.Services.AddAuthorization();

// ── API-key application identification ──────────────────────────────────────────────────
builder.Services.AddSingleton(new ApiKeyOptions
{
    KeyToIdentity = new Dictionary<string, string>
    {
        [mobileAppApiKey]   = "MobileApp",
        [legacyShellApiKey] = "LegacyShell",
    },
});

// ── Rate limiting — login endpoint (public brute-force surface) ──────────────────────────
builder.Services.AddRateLimiter(opts =>
{
    opts.AddSlidingWindowLimiter("login", o =>
    {
        o.PermitLimit          = 5;
        o.Window               = TimeSpan.FromMinutes(1);
        o.SegmentsPerWindow    = 2;
        o.QueueProcessingOrder = QueueProcessingOrder.OldestFirst;
        o.QueueLimit           = 0;
    });
    opts.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
});

// ── Keyed singletons so AuthController can receive secrets via constructor injection ─────
builder.Services.AddKeyedSingleton("JwtSecret",   jwtSecret);
builder.Services.AddKeyedSingleton("JwtIssuer",   jwtIssuer);
builder.Services.AddKeyedSingleton("JwtAudience", jwtAudience);
builder.Services.AddKeyedSingleton("ShellApiKey", shellApiKey);

// ── User credential validator — queries Login2.Username / Login2.Password ───────────────
builder.Services.AddScoped<IUserCredentialValidator, Login2UserCredentialValidator>();

// ── CORS ─────────────────────────────────────────────────────────────────────────────────
var allowedOrigins = builder.Configuration.GetSection("AllowedOrigins").Get<string[]>()!;
builder.Services.AddCors(opts =>
    opts.AddDefaultPolicy(p => p
        .WithOrigins(allowedOrigins)
        .AllowAnyHeader()
        .AllowAnyMethod()));

// ── Database / repositories ───────────────────────────────────────────────────────────────
builder.Services.AddScoped<IDbConnection>(_ =>
    new SqlConnection(builder.Configuration.GetConnectionString("DefaultConnection")));

builder.Services.AddScoped<ICustomerRepository, CustomerRepository>();

builder.Services.AddControllers();

// ── Swagger / OpenAPI ─────────────────────────────────────────────────────────────────────
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo
    {
        Title       = "Customers API",
        Version     = "v1",
        Description = "CRUD API for **poc.Customers** — backed by SQL Server stored procedures via Dapper.\n\n" +
                      "**To authenticate:** call `POST /api/auth/login` (user credentials) or " +
                      "`POST /api/auth/token` (shell API key), copy the `access_token`, " +
                      "then click **Authorize** and paste it in the Bearer field."
    });

    // JWT Bearer
    c.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Type         = SecuritySchemeType.Http,
        Scheme       = "bearer",
        BearerFormat = "JWT",
        In           = ParameterLocation.Header,
        Description  = "JWT access token from `POST /api/auth/login` or `POST /api/auth/token`.",
    });

    // X-Api-Key application identifier
    c.AddSecurityDefinition("ApiKey", new OpenApiSecurityScheme
    {
        Type        = SecuritySchemeType.ApiKey,
        In          = ParameterLocation.Header,
        Name        = "X-Api-Key",
        Description = "Application identifier. Use `mobile-app-v1` (Expo) or `legacy-shell-v1` (WebForms). " +
                      "Required on endpoints marked [RequireApiKey].",
    });

    c.AddSecurityRequirement(_ => new OpenApiSecurityRequirement
    {
        { new OpenApiSecuritySchemeReference("Bearer"), new List<string>() },
        { new OpenApiSecuritySchemeReference("ApiKey"), new List<string>() },
    });

    var xmlPath = Path.Combine(AppContext.BaseDirectory, $"{Assembly.GetExecutingAssembly().GetName().Name}.xml");
    if (File.Exists(xmlPath))
        c.IncludeXmlComments(xmlPath, includeControllerXmlComments: true);
});

// ── Pipeline ──────────────────────────────────────────────────────────────────────────────
var app = builder.Build();

app.UseSwagger();
app.UseSwaggerUI(c =>
{
    c.SwaggerEndpoint("/swagger/v1/swagger.json", "Customers API v1");
    c.RoutePrefix    = "swagger";
    c.DocumentTitle  = "Customers API — PoC";
});

// UseRouting must be explicit so ApiKeyMiddleware (below) sees populated endpoint metadata
// via HttpContext.GetEndpoint(). Without this call, routing runs after custom middleware
// and GetEndpoint() would always return null.
app.UseRouting();

app.UseCors();
app.UseRateLimiter();

// Application-identification middleware — runs after routing, before auth.
app.UseMiddleware<ApiKeyMiddleware>();

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

app.MapGet("/health", () => Results.Ok(new { status = "ok" }));

app.Run();
