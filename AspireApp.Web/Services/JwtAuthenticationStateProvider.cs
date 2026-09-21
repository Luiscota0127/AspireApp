using Microsoft.AspNetCore.Components.Authorization;
using System.Security.Claims;
using System.Text.Json;

namespace AspireApp.Web.Services;

/// <summary>
/// Bridges ApiAuthService's JWT to Blazor's authorization pipeline so that
/// AuthorizeRouteView can decide whether a visitor is signed in.
///
/// ApiAuthService owns the token (it is scoped to the circuit, same as this provider),
/// so this reads from it rather than reading storage a second time and risking the two
/// drifting apart.
/// </summary>
public class JwtAuthenticationStateProvider : AuthenticationStateProvider
{
    private readonly ApiAuthService _authService;
    private ClaimsPrincipal _anonymous = new(new ClaimsIdentity());

    public JwtAuthenticationStateProvider(ApiAuthService authService)
    {
        _authService = authService;
        // the token is owned by ApiAuthService; react whenever it changes
        _authService.AuthenticationStateChanged += NotifyAuthenticationStateChanged;
    }

    /// <summary>Anonymous when there is no token, or when the token has already expired.</summary>
    public override Task<AuthenticationState> GetAuthenticationStateAsync()
    {
        var token = _authService.JwtToken;
        if (string.IsNullOrEmpty(token))
        {
            return Task.FromResult(new AuthenticationState(_anonymous));
        }

        var claims = ParseClaims(token);
        if (claims.Count == 0)
        {
            return Task.FromResult(new AuthenticationState(_anonymous));
        }

        var identity = new ClaimsIdentity(claims, authenticationType: "jwt");
        return Task.FromResult(new AuthenticationState(new ClaimsPrincipal(identity)));
    }

    /// <summary>Re-evaluates the token; call after login, on token refresh, and on logout.</summary>
    public void NotifyAuthenticationStateChanged()
    {
        NotifyAuthenticationStateChanged(GetAuthenticationStateAsync());
    }

    private static List<Claim> ParseClaims(string token)
    {
        var claims = new List<Claim>();
        try
        {
            var parts = token.Split('.');
            if (parts.Length < 2) return claims;

            var payload = parts[1].Replace('-', '+').Replace('_', '/');
            switch (payload.Length % 4)
            {
                case 2: payload += "=="; break;
                case 3: payload += "="; break;
            }

            var json = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(payload));
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            // reject an expired token so the redirect happens instead of a 401 storm
            if (root.TryGetProperty("exp", out var expEl) &&
                expEl.TryGetInt64(out var exp) &&
                DateTimeOffset.FromUnixTimeSeconds(exp) <= DateTimeOffset.UtcNow)
            {
                return claims;
            }

            foreach (var property in root.EnumerateObject())
            {
                switch (property.Name)
                {
                    case "sub":
                    case "nameid":
                        claims.Add(new Claim(ClaimTypes.NameIdentifier, property.Value.ToString()));
                        break;
                    case "email":
                        claims.Add(new Claim(ClaimTypes.Email, property.Value.ToString()));
                        break;
                    case "unique_name":
                    case "name":
                        claims.Add(new Claim(ClaimTypes.Name, property.Value.ToString()));
                        break;
                    case "role":
                        claims.Add(new Claim(ClaimTypes.Role, property.Value.ToString()));
                        break;
                }
            }
        }
        catch
        {
            // a malformed token simply means "not signed in"
            claims.Clear();
        }

        return claims;
    }
}
