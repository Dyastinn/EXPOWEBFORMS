namespace PocApi.Auth;

public sealed class ApiKeyOptions
{
    public required IReadOnlyDictionary<string, string> KeyToIdentity { get; init; }
}
