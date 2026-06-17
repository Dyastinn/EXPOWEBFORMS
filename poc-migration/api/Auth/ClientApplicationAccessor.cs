namespace PocApi.Auth;

public static class ClientApplicationAccessor
{
    internal const string ItemsKey = "ClientApplication";

    /// <summary>
    /// Returns the resolved client identity set by <see cref="ApiKeyMiddleware"/>, or null
    /// if the current endpoint does not carry <see cref="RequireApiKeyAttribute"/>.
    /// </summary>
    public static string? GetClientApplication(this HttpContext ctx) =>
        ctx.Items.TryGetValue(ItemsKey, out var v) ? v as string : null;
}
