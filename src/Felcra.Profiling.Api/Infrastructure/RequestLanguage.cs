using System.Globalization;

namespace Felcra.Profiling.Api.Infrastructure;

internal static class RequestLanguage
{
    /// <summary>
    /// Answers each request in the language of its Accept-Language header (the clients send the user's choice):
    /// validation messages, errors and reports. Only the UI culture changes; number and date formats stay as they are.
    /// Requests without a Malay or English preference get English.
    /// </summary>
    public static IApplicationBuilder UseRequestLanguage(this IApplicationBuilder app) =>
        app.Use(async (context, next) =>
        {
            var language = Parse(context.Request.Headers.AcceptLanguage.ToString()) ?? English;
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(CultureName(language));
            await next();
        });
}
