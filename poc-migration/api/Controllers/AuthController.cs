using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace PocApi.Controllers;

/// <summary>Token validation endpoint — called by the shell-sim to gate Expo iframe access.</summary>
[ApiController]
[Route("api/auth")]
[Authorize]
public sealed class AuthController : ControllerBase
{
    /// <summary>
    /// Returns 200 with the caller's identity claims if the <c>Authorization</c> header contains a
    /// valid, unexpired JWT issued by <c>poc-legacy-shell</c> for audience <c>poc-api</c>.
    /// The JWT middleware returns 401 automatically for missing, invalid, or expired tokens —
    /// no explicit token parsing is needed here.
    /// </summary>
    [HttpGet("validate")]
    [ProducesResponseType(typeof(object), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public IActionResult Validate()
    {
        var sub  = User.FindFirst("sub")?.Value;
        var name = User.FindFirst("name")?.Value;
        return Ok(new { sub, name, valid = true });
    }
}
