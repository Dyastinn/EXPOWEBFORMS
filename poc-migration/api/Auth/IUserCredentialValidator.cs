namespace PocApi.Auth;

/// <summary>
/// Validates a username/password pair against the application's user store.
/// Wire in your own implementation via Program.cs:
///   builder.Services.AddScoped&lt;IUserCredentialValidator, YourValidator&gt;();
/// </summary>
public interface IUserCredentialValidator
{
    /// <summary>
    /// Returns the user's identity if credentials are valid; null if they are not.
    /// Never log the password or any credential material.
    /// </summary>
    Task<UserIdentity?> ValidateAsync(string username, string password, CancellationToken ct = default);
}

public sealed record UserIdentity(string Sub, string Name, IReadOnlyList<string> Roles);

/// <summary>
/// Default no-op implementation registered at startup. Replace it with a real
/// validator before exposing the login endpoint to users.
/// </summary>
internal sealed class NullUserCredentialValidator : IUserCredentialValidator
{
    public Task<UserIdentity?> ValidateAsync(string username, string password, CancellationToken ct = default)
        => Task.FromResult<UserIdentity?>(null);
}
