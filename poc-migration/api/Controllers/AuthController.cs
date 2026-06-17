using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.IdentityModel.Tokens;
using PocApi.Auth;
using PocApi.DTOs;

namespace PocApi.Controllers;

/// <summary>Token issuance and validation endpoints.</summary>
[ApiController]
[Route("api/auth")]
public sealed class AuthController(
    [FromKeyedServices("JwtSecret")]   string jwtSecret,
    [FromKeyedServices("JwtIssuer")]   string jwtIssuer,
    [FromKeyedServices("JwtAudience")] string jwtAudience,
    [FromKeyedServices("ShellApiKey")] string shellApiKey,
    IUserCredentialValidator           credentialValidator) : ControllerBase
{
    // HMAC-SHA256 is acceptable for a single-API POC.
    // Prefer RS256/ES256 when multiple services will independently validate tokens.
    private SigningCredentials SigningCreds =>
        new(new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSecret)), SecurityAlgorithms.HmacSha256);

    /// <summary>
    /// Issues a signed JWT for a trusted shell caller.
    /// The caller must supply the pre-shared <c>X-Api-Key</c> header.
    /// Only the API holds <c>JWT_SECRET</c>; shells can no longer forge tokens.
    /// </summary>
    [HttpPost("token")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(object), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public IActionResult IssueToken([FromHeader(Name = "X-Api-Key")] string? apiKey)
    {
        if (string.IsNullOrEmpty(apiKey) || apiKey != shellApiKey)
            return Unauthorized(new { error = "invalid_client", error_description = "Missing or invalid API key." });

        var now   = DateTime.UtcNow;
        var token = new JwtSecurityToken(
            issuer:             jwtIssuer,
            audience:           jwtAudience,
            claims:             [
                new Claim(JwtRegisteredClaimNames.Sub,  "demo-user"),
                new Claim(JwtRegisteredClaimNames.Name, "Demo User"),
                new Claim(JwtRegisteredClaimNames.Jti,  Guid.NewGuid().ToString()),
            ],
            notBefore:          now,
            expires:            now.AddHours(1),
            signingCredentials: SigningCreds);

        var tokenString = new JwtSecurityTokenHandler().WriteToken(token);
        return Ok(new { access_token = tokenString, token_type = "Bearer", expires_in = 3600 });
    }

    /// <summary>
    /// Authenticates a user with credentials and issues a short-lived JWT (15 min).
    /// Requires a valid <c>X-Api-Key</c> so only registered client applications can call this endpoint.
    /// Wire <see cref="IUserCredentialValidator"/> in Program.cs to activate real credential checking.
    /// </summary>
    /// <remarks>Rate-limited: 5 requests per minute per IP.</remarks>
    [HttpPost("login")]
    [AllowAnonymous]
    [RequireApiKey]
    [EnableRateLimiting("login")]
    [ProducesResponseType(typeof(object), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    public async Task<IActionResult> Login([FromBody] LoginRequest request, CancellationToken ct)
    {
        var identity = await credentialValidator.ValidateAsync(request.Username, request.Password, ct);
        if (identity is null)
            return Unauthorized(new { error = "invalid_credentials", error_description = "Username or password is incorrect." });

        var now    = DateTime.UtcNow;
        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub,  identity.Sub),
            new(JwtRegisteredClaimNames.Name, identity.Name),
            new(JwtRegisteredClaimNames.Jti,  Guid.NewGuid().ToString()),
        };

        foreach (var role in identity.Roles)
            claims.Add(new Claim(ClaimTypes.Role, role));

        var token = new JwtSecurityToken(
            issuer:             jwtIssuer,
            audience:           jwtAudience,
            claims:             claims,
            notBefore:          now,
            expires:            now.AddMinutes(15),
            signingCredentials: SigningCreds);

        var tokenString = new JwtSecurityTokenHandler().WriteToken(token);
        return Ok(new { access_token = tokenString, token_type = "Bearer", expires_in = 900 });
    }

    /// <summary>
    /// Validates the caller's JWT — used by the shell-sim auth gate before serving the Expo iframe.
    /// The JWT middleware returns 401 automatically for missing, invalid, or expired tokens.
    /// </summary>
    [HttpGet("validate")]
    [Authorize]
    [ProducesResponseType(typeof(object), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public IActionResult Validate()
    {
        var sub  = User.FindFirst("sub")?.Value;
        var name = User.FindFirst("name")?.Value;
        return Ok(new { sub, name, valid = true });
    }
}
