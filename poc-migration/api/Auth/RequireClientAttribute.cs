namespace PocApi.Auth;

// Scaffolded for future use: restrict an endpoint to a specific client identity
// already resolved by ApiKeyMiddleware. Wire enforcement into ApiKeyMiddleware
// when needed — not required for the current POC.
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public sealed class RequireClientAttribute(string clientIdentity) : Attribute
{
    public string ClientIdentity { get; } = clientIdentity;
}
