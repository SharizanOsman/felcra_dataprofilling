namespace Felcra.Profiling.Api.Infrastructure;

internal static class SecurityHeaders
{
    /// <summary>Baseline headers; a full Content-Security-Policy is added during hardening (Step 9).</summary>
    public static IApplicationBuilder UseSecurityHeaders(this IApplicationBuilder app) =>
        app.Use(async (context, next) =>
        {
            var headers = context.Response.Headers;
            headers.XContentTypeOptions = "nosniff";
            headers.XFrameOptions = "DENY";
            headers["Referrer-Policy"] = "strict-origin-when-cross-origin";
            await next();
        });
}
