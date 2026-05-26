using Microsoft.AspNetCore.Http;

namespace Itm.Order.Api.Handlers;

public class AuthForwardingDelegatingHandler : DelegatingHandler
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public AuthForwardingDelegatingHandler(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var authHeader = _httpContextAccessor.HttpContext?
            .Request.Headers["Authorization"]
            .FirstOrDefault();

        if (!string.IsNullOrEmpty(authHeader) && !request.Headers.Contains("Authorization"))
        {
            request.Headers.TryAddWithoutValidation("Authorization", authHeader);
        }

        return base.SendAsync(request, cancellationToken);
    }
}