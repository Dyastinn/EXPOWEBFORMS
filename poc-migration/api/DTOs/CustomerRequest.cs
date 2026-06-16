using System.ComponentModel.DataAnnotations;

namespace PocApi.DTOs;

/// <summary>Request body for creating or replacing a customer.</summary>
public sealed class CustomerRequest
{
    /// <summary>Display name. 1–100 characters.</summary>
    /// <example>Alice Chen</example>
    [Required]
    [MaxLength(100, ErrorMessage = "Name must be 100 characters or fewer.")]
    public required string Name { get; init; }

    /// <summary>Unique email address. 1–200 characters.</summary>
    /// <example>alice@example.com</example>
    [Required]
    [EmailAddress(ErrorMessage = "Email is not a valid address.")]
    [MaxLength(200, ErrorMessage = "Email must be 200 characters or fewer.")]
    public required string Email { get; init; }

    /// <summary>Lifetime spend in USD (0 – 9,999,999.99). Determines the computed Tier.</summary>
    /// <example>15250.00</example>
    [Range(0, 9_999_999.99, ErrorMessage = "TotalSpend must be between 0 and 9,999,999.99.")]
    public decimal TotalSpend { get; init; }
}
