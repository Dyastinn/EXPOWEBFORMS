using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.IdentityModel.Tokens;

namespace PocApi.Controllers;

/// <summary>Token issuance and validation endpoints for the Expo iframe auth gate.</summary>
[ApiController]
[Route("api/auth")]
public sealed class AuthController(
    [FromKeyedServices("JwtSecret")]   string jwtSecret,
    [FromKeyedServices("JwtIssuer")]   string jwtIssuer,
    [FromKeyedServices("JwtAudience")] string jwtAudience,
    [FromKeyedServices("ShellApiKey")] string shellApiKey) : ControllerBase
{
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

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSecret));
        var now = DateTime.UtcNow;

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
            signingCredentials: new SigningCredentials(key, SecurityAlgorithms.HmacSha256));

        var tokenString = new JwtSecurityTokenHandler().WriteToken(token);
        return Ok(new { access_token = tokenString, token_type = "Bearer", expires_in = 3600 });
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
