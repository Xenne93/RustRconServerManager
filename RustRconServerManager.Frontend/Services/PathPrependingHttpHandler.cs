using Microsoft.AspNetCore.Components.WebAssembly.Http;

namespace RustRconServerManager.Frontend.Services;

/// <summary>
/// HTTP message handler that ensures credentials (HttpOnly cookies) are included
/// with every request. Previously also prepended a tenant path base — that's gone now.
/// Kept as a class so DI registrations don't need to change.
/// </summary>
public class PathPrependingHttpHandler : HttpClientHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        request.SetBrowserRequestCredentials(BrowserRequestCredentials.Include);
        return base.SendAsync(request, cancellationToken);
    }
}
