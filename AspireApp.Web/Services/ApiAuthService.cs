using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using System.Text.Json;
using System.Text;

namespace AspireApp.Web.Services;

public class ApiAuthService
{
    private readonly IHttpClientFactory _httpFactory;
    private readonly NavigationManager _nav;
    private readonly IJSRuntime _jsRuntime;

    public string? JwtToken { get; private set; }
    public string? RefreshToken { get; private set; }
    private readonly System.Threading.SemaphoreSlim _refreshLock = new(1, 1);

    /// <summary>
    /// Raised whenever the signed-in identity changes (login, refresh, logout).
    /// JwtAuthenticationStateProvider subscribes to this, which avoids a circular
    /// dependency between the two services.
    /// </summary>
    public event Action? AuthenticationStateChanged;

    public ApiAuthService(IHttpClientFactory httpFactory, NavigationManager nav, IJSRuntime jsRuntime)
    {
        _httpFactory = httpFactory;
        _nav = nav;
        _jsRuntime = jsRuntime;
    }

    /// <summary>
    /// Reads the persisted tokens back out of browser storage.
    /// Must be called from an interactive render: during the static prerender there is no
    /// JS interop available and this would silently leave JwtToken null.
    /// </summary>
    public async Task InitializeAsync()
    {
        var json = await _jsRuntime.InvokeAsync<string>("localStorage.getItem", "aspire_auth");
        if (string.IsNullOrEmpty(json))
        {
            return;
        }

        var stored = JsonSerializer.Deserialize<AuthStorage>(json);
        if (stored == null)
        {
            return;
        }

        JwtToken = stored.Token;
        RefreshToken = stored.RefreshToken;
        AuthenticationStateChanged?.Invoke();
    }

    private static DateTimeOffset? GetExpiryFromToken(string? token)
    {
        if (string.IsNullOrEmpty(token)) return null;
        try
        {
            var parts = token.Split('.');
            if (parts.Length < 2) return null;
            var payload = parts[1];
            // pad base64
            payload = payload.Replace('-', '+').Replace('_', '/');
            switch (payload.Length % 4)
            {
                case 2: payload += "=="; break;
                case 3: payload += "="; break;
            }
            var bytes = Convert.FromBase64String(payload);
            var json = Encoding.UTF8.GetString(bytes);
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("exp", out var expEl))
            {
                var seconds = expEl.GetInt64();
                return DateTimeOffset.FromUnixTimeSeconds(seconds);
            }
        }
        catch
        {
            // ignore parse errors
        }
        return null;
    }

    private bool IsTokenExpiringSoon(TimeSpan threshold)
    {
        var exp = GetExpiryFromToken(JwtToken);
        if (exp == null) return true;
        return exp <= DateTimeOffset.UtcNow.Add(threshold);
    }

    private HttpClient CreateClient()
    {
        return _httpFactory.CreateClient("ApiClient");
    }

    /// <summary>
    /// Sends a request with the current user's bearer token attached.
    /// The token cannot be attached by the HTTP handler: IHttpClientFactory builds handlers
    /// from the root scope, so a handler never sees the circuit's ApiAuthService instance.
    /// </summary>
    private async Task<HttpResponseMessage> SendAuthorizedAsync(
        HttpMethod method, string url, HttpContent? content = null)
    {
        await EnsureTokenLoadedAsync();

        using var request = new HttpRequestMessage(method, url) { Content = content };
        if (!string.IsNullOrEmpty(JwtToken))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", JwtToken);
        }

        return await CreateClient().SendAsync(request);
    }

    private bool _storageHydrated;

    /// <summary>
    /// Guarantees the in-memory token matches browser storage before an authenticated call.
    /// The circuit can outlive a navigation, and the first interactive render may not have
    /// happened yet, so this cannot rely on a component's lifecycle to have run first.
    /// Safe to call repeatedly: it only touches storage once.
    /// </summary>
    public async Task EnsureTokenLoadedAsync()
    {
        if (_storageHydrated || !string.IsNullOrEmpty(JwtToken))
        {
            return;
        }

        _storageHydrated = true;
        try
        {
            await InitializeAsync();
        }
        catch (InvalidOperationException)
        {
            // no JS interop yet (static prerender); retry on the next call
            _storageHydrated = false;
        }
    }

    public async Task<bool> LoginAsync(string email, string password)
    {
        var client = _httpFactory.CreateClient("ApiClientNoAuth");
        var resp = await client.PostAsJsonAsync("api/auth/login", new { Email = email, Password = password });
        if (!resp.IsSuccessStatusCode) return false;
        var data = await resp.Content.ReadFromJsonAsync<AuthResponse>();
        JwtToken = data?.Token;
        RefreshToken = data?.RefreshToken;
        // persist tokens in protected session storage
        var storage = System.Text.Json.JsonSerializer.Serialize(new AuthStorage { Token = JwtToken, RefreshToken = RefreshToken });
        await _jsRuntime.InvokeVoidAsync("localStorage.setItem", "aspire_auth", storage);
        AuthenticationStateChanged?.Invoke();
        return !string.IsNullOrEmpty(JwtToken);
    }

    public async Task<bool> RegisterAsync(string email, string password, string? displayName)
    {
        var client = _httpFactory.CreateClient("ApiClientNoAuth");
        var resp = await client.PostAsJsonAsync("api/auth/register", new { Email = email, Password = password, DisplayName = displayName });
        return resp.IsSuccessStatusCode;
    }

    public async Task LogoutAsync()
    {
        JwtToken = null;
        RefreshToken = null;
        _storageHydrated = true;
        await _jsRuntime.InvokeVoidAsync("localStorage.removeItem", "aspire_auth");
        AuthenticationStateChanged?.Invoke();
        _nav.NavigateTo("/login");
    }

    public async Task<ProjectDto[]?> GetProjectsAsync()
    {
        using var resp = await SendAuthorizedAsync(HttpMethod.Get, "api/projects");
        if (!resp.IsSuccessStatusCode) return null;
        return await resp.Content.ReadFromJsonAsync<ProjectDto[]>();
    }

    public async Task<ProjectMemberDto[]?> GetProjectMembersAsync(Guid projectId)
    {
        var client = CreateClient();
        var resp = await client.GetAsync($"api/projects/{projectId}/members");
        if (!resp.IsSuccessStatusCode) return null;
        return await resp.Content.ReadFromJsonAsync<ProjectMemberDto[]>();
    }

    public async Task<bool> AddProjectMemberAsync(Guid projectId, string email, string? role)
    {
        var client = CreateClient();
        var resp = await client.PostAsJsonAsync($"api/projects/{projectId}/members", new { Email = email, Role = role });
        return resp.IsSuccessStatusCode;
    }

    public async Task<bool> RemoveProjectMemberAsync(Guid projectId, Guid memberId)
    {
        var client = CreateClient();
        var resp = await client.DeleteAsync($"api/projects/{projectId}/members/{memberId}");
        return resp.IsSuccessStatusCode;
    }

    public record ProjectMemberDto(Guid Id, Guid ProjectId, string? UserId, string? Email, string Role);

    // Task DTOs and helpers for client
    public enum TaskStatus { Todo = 0, InProgress = 1, Done = 2 }
    public record TaskDto(Guid Id, string Title, string? Description, int Status, Guid ProjectId, string? AssignedToId, DateTime? DueDate);

    public async Task<TaskDto[]?> GetTasksByProjectAsync(Guid projectId)
    {
        using var resp = await SendAuthorizedAsync(HttpMethod.Get, "api/tasks");
        if (!resp.IsSuccessStatusCode) return null;
        var all = await resp.Content.ReadFromJsonAsync<TaskDto[]>();
        if (all == null) return Array.Empty<TaskDto>();
        return all.Where(t => t.ProjectId == projectId).ToArray();
    }

    public async Task<TaskDto?> GetTaskAsync(Guid taskId)
    {
        using var resp = await SendAuthorizedAsync(HttpMethod.Get, $"api/tasks/{taskId}");
        if (!resp.IsSuccessStatusCode) return null;
        return await resp.Content.ReadFromJsonAsync<TaskDto>();
    }

    public async Task<bool> UpdateTaskStatusAsync(Guid taskId, TaskStatus status)
    {
        var existing = await GetTaskAsync(taskId);
        if (existing == null) return false;

        // The API expects TaskUpdateDto: Title, Description, Status (enum), DueDate, AssignedToId
        var payload = new
        {
            Title = existing.Title,
            Description = existing.Description,
            Status = (int)status,
            DueDate = existing.DueDate,
            AssignedToId = existing.AssignedToId
        };

        using var resp = await SendAuthorizedAsync(
            HttpMethod.Put,
            $"api/tasks/{taskId}",
            JsonContent.Create(payload));
        return resp.IsSuccessStatusCode;
    }

    public async Task<Guid?> CreateTaskAsync(Guid projectId, string title, string? description, string? assignedToId)
    {
        var payload = new
        {
            ProjectId = projectId,
            Title = title,
            Description = description,
            AssignedToId = assignedToId
        };

        using var resp = await SendAuthorizedAsync(
            HttpMethod.Post,
            "api/tasks",
            JsonContent.Create(payload));
        if (!resp.IsSuccessStatusCode) return null;
        // API returns the created task DTO; attempt to read it
        var created = await resp.Content.ReadFromJsonAsync<TaskDto>();
        return created?.Id;
    }

    public async Task<bool> DeleteTaskAsync(Guid taskId)
    {
        using var resp = await SendAuthorizedAsync(HttpMethod.Delete, $"api/tasks/{taskId}");
        return resp.IsSuccessStatusCode;
    }

    public async Task<bool> TryRefreshTokenAsync()
    {
        if (string.IsNullOrEmpty(RefreshToken)) return false;

        await _refreshLock.WaitAsync();
        try
        {
            // If token exists and is not expiring soon, skip refresh
            if (!string.IsNullOrEmpty(JwtToken) && !IsTokenExpiringSoon(TimeSpan.FromSeconds(60)))
                return true;

            var client = _httpFactory.CreateClient("ApiClientNoAuth");
            var resp = await client.PostAsJsonAsync("api/auth/refresh", new { RefreshToken = RefreshToken });
            if (!resp.IsSuccessStatusCode) return false;
            var data = await resp.Content.ReadFromJsonAsync<AuthResponse>();
            if (data == null || string.IsNullOrEmpty(data.Token)) return false;
            JwtToken = data.Token;
            RefreshToken = data.RefreshToken;
            var json = System.Text.Json.JsonSerializer.Serialize(new AuthStorage { Token = JwtToken, RefreshToken = RefreshToken });
            await _jsRuntime.InvokeVoidAsync("localStorage.setItem", "aspire_auth", json);
            AuthenticationStateChanged?.Invoke();
            return true;
        }
        finally
        {
            _refreshLock.Release();
        }
    }

    // DTOs used by the client
    public record AuthResponse(string Token, string? RefreshToken, string? UserId, string? Email);
    public record ProjectDto(Guid Id, string Name, string? Description);
}
