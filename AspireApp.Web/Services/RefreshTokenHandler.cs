using System.Net.Http.Headers;
using System.Net.Http.Json;

namespace AspireApp.Web.Services;

public class RefreshTokenHandler : DelegatingHandler
{
    private readonly ApiAuthService _authService;
    private readonly IHttpClientFactory _httpFactory;

    public RefreshTokenHandler(ApiAuthService authService, IHttpClientFactory httpFactory)
    {
        _authService = authService;
        _httpFactory = httpFactory;
    }

    /// <summary>
    /// IHttpClientFactory builds its handlers from the root scope, so the ApiAuthService injected
    /// into this constructor is not necessarily the circuit's instance. The caller therefore sets
    /// the Authorization header itself (see ApiAuthService.SendAuthorizedAsync) and this handler
    /// only has to deal with refreshing an expired token.
    /// </summary>
    private string? CurrentToken => _authService.JwtToken;

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        // Attach current token if present
        if (!string.IsNullOrEmpty(_authService.JwtToken))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _authService.JwtToken);
        }

        var response = await base.SendAsync(request, cancellationToken);
        if (response.StatusCode != System.Net.HttpStatusCode.Unauthorized)
            return response;

        // Try to refresh token
        var refreshed = await _authService.TryRefreshTokenAsync();
        if (!refreshed)
            return response; // still unauthorized

        // Retry original request with new token
        var cloned = await CloneHttpRequestMessageAsync(request);
        cloned.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _authService.JwtToken);
        return await base.SendAsync(cloned, cancellationToken);
    }

    private static async Task<HttpRequestMessage> CloneHttpRequestMessageAsync(HttpRequestMessage req)
    {
        var clone = new HttpRequestMessage(req.Method, req.RequestUri);

        // Copy the request's content (via a MemoryStream) into the cloned object
        if (req.Content != null)
        {
            var ms = new MemoryStream();
            await req.Content.CopyToAsync(ms);
            ms.Position = 0;
            clone.Content = new StreamContent(ms);

            // copy the content headers
            if (req.Content.Headers != null)
            {
                foreach (var h in req.Content.Headers)
                    clone.Content.Headers.Add(h.Key, h.Value);
            }
        }

        clone.Version = req.Version;

        foreach (var header in req.Headers)
            clone.Headers.TryAddWithoutValidation(header.Key, header.Value);

        // Note: HttpRequestMessage.Properties is obsolete. Do not copy it.
        // HttpRequestMessage.Options does not expose a public enumerator for keys,
        // so we avoid cloning arbitrary request options to prevent relying on internal APIs.

        return clone;
    }
}
