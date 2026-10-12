using System.Net.Http.Headers;

namespace Felcra.Profiling.UI.Shared.Api;

/// <summary>Tells the API which language to answer in (validation messages, reports, server errors).</summary>
public sealed class LanguageHandler : DelegatingHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        request.Headers.AcceptLanguage.Clear();
        request.Headers.AcceptLanguage.Add(new StringWithQualityHeaderValue(Language));
        return base.SendAsync(request, cancellationToken);
    }
}
