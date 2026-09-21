using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.Extensions.Options;

namespace AspireApp.Web.Services;

/// <summary>
/// Minimal IAuthenticationService for Blazor Server.
///
/// AddAuthorizationCore() registers the authorization side but not this abstraction,
/// which AuthorizeRouteView resolves. There is no cookie/OIDC handler here because the
/// session is a JWT held by ApiAuthService, so the sign-in/out calls are no-ops and only
/// AuthenticateAsync needs to answer - it delegates to the authentication state provider.
/// </summary>
public class JwtAuthenticationService : IAuthenticationService
{
    private readonly AuthenticationStateProvider _stateProvider;

    public JwtAuthenticationService(AuthenticationStateProvider stateProvider)
    {
        _stateProvider = stateProvider;
    }

    public async Task<AuthenticateResult> AuthenticateAsync(HttpContext context, string? scheme)
    {
        var state = await _stateProvider.GetAuthenticationStateAsync();
        return state.User.Identity?.IsAuthenticated == true
            ? AuthenticateResult.Success(new AuthenticationTicket(state.User, scheme ?? "jwt"))
            : AuthenticateResult.NoResult();
    }

    public Task ChallengeAsync(HttpContext context, string? scheme, AuthenticationProperties? properties)
        => Task.CompletedTask;

    public Task ForbidAsync(HttpContext context, string? scheme, AuthenticationProperties? properties)
        => Task.CompletedTask;

    public Task SignInAsync(HttpContext context, string? scheme, System.Security.Claims.ClaimsPrincipal principal,
        AuthenticationProperties? properties)
        => Task.CompletedTask;

    public Task SignOutAsync(HttpContext context, string? scheme, AuthenticationProperties? properties)
        => Task.CompletedTask;
}

/// <summary>
/// Supplies the redirect scheme IAuthenticationService implementations are expected to take;
/// unused here but part of the interface's registration shape.
/// </summary>
public class JwtAuthenticationSchemeProvider : AuthenticationSchemeProvider
{
    public JwtAuthenticationSchemeProvider(IOptions<AuthenticationOptions> options)
        : base(options)
    {
    }
}
