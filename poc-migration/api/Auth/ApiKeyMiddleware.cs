namespace PocApi.Auth;

/// <summary>
/// Enforces application identification via the X-Api-Key header on any endpoint
/// decorated with <see cref="RequireApiKeyAttribute"/>. Endpoints without the
/// attribute are not affected.
///
/// ORDERING REQUIREMENT: must be registered after app.UseRouting() so that
/// HttpContext.GetEndpoint() is already populated when this middleware runs.
/// </summary>
public sealed class ApiKeyMiddleware(
    RequestDelegate          next,
    ApiKeyOptions            options,
    ILogger<ApiKeyMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext ctx)
    {
        // GetEndpoint() returns null when no route matched — skip enforcement.
        var attr = ctx.GetEndpoint()?.Metadata.GetMetadata<RequireApiKeyAttribute>();
        if (attr is null)
        {
            await next(ctx);
            return;
        }

        var headerValue = ctx.Request.Headers["X-Api-Key"].FirstOrDefault();

        if (string.IsNullOrEmpty(headerValue) ||
            !options.KeyToIdentity.TryGetValue(headerValue, out var identity))
        {
            // Log absence/invalidity but never the raw key value.
            logger.LogWarning("API key validation failed — {Method} {Path}",
                ctx.Request.Method, ctx.Request.Path);

            ctx.Response.StatusCode  = StatusCodes.Status401Unauthorized;
            ctx.Response.ContentType = "application/json";
            await ctx.Response.WriteAsync("{\"error\":\"Invalid application key\"}");
            return;
        }

        // Log the resolved identity, never the raw key.
        logger.LogInformation("Client identified as {ClientIdentity} — {Method} {Path}",
            identity, ctx.Request.Method, ctx.Request.Path);

        ctx.Items[ClientApplicationAccessor.ItemsKey] = identity;

        await next(ctx);
    }
}
