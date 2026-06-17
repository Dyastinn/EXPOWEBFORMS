using System.ComponentModel.DataAnnotations;

namespace PocApi.DTOs;

public sealed record LoginRequest(
    [Required] string Username,
    [Required] string Password);
