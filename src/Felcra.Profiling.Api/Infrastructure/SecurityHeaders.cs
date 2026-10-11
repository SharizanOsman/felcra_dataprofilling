namespace Felcra.Profiling.Api.Infrastructure;

internal static class SecurityHeaders
{
    /// <summary>
    /// Everything is served from this site: the Blazor clients need 'wasm-unsafe-eval' to run WebAssembly,
    /// MudBlazor sets inline styles, photos and QR codes use blob: / data: URLs, and the scanner needs the camera.
    /// </summary>
    private const string ContentSecurityPolicy =
        "default-src 'self'; " +
        "script-src 'self' 'wasm-unsafe-eval'; " +
        "style-src 'self' 'unsafe-inline'; " +
        "img-src 'self' data: blob:; " +
        "font-src 'self' data:; " +
        "connect-src 'self'; " +
        "media-src 'self' blob:; " +
        "worker-src 'self' blob:; " +
        "manifest-src 'self'; " +
        "object-src 'none'; " +
        "base-uri 'self'; " +
        "form-action 'self'; " +
        "frame-ancestors 'none'";

    public static IApplicationBuilder UseSecurityHeaders(this IApplicationBuilder app) =>
        app.Use(async (context, next) =>
        {
            var headers = context.Response.Headers;
            headers.XContentTypeOptions = "nosniff";
            headers.XFrameOptions = "DENY";
            headers["Referrer-Policy"] = "strict-origin-when-cross-origin";
            headers["Cross-Origin-Opener-Policy"] = "same-origin";
            headers["Permissions-Policy"] = "camera=(self), microphone=(), geolocation=(), payment=(), usb=()";

            var path = context.Request.Path;
            // The development-only API explorer loads its own scripts; everything else gets the strict policy.
            if (!path.StartsWithSegments("/scalar"))
                headers.ContentSecurityPolicy = ContentSecurityPolicy;
            // API answers hold farm data: never keep them in shared or browser caches.
            if (path.StartsWithSegments("/api"))
                headers.CacheControl = "no-store";

            await next();
        });
}
