using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.Logging;
using Microsoft.AspNetCore.Components;

namespace AspireApp.Web.Services;

public class CollaborationService : IAsyncDisposable
{
        public event Func<ConnectionState, Task>? OnConnectionChanged;

    private readonly HubConnection _hub;
    private readonly ILogger<CollaborationService> _logger;

    public event Func<ApiAuthService.ProjectDto, Task>? OnProjectCreated;
    public event Func<ApiAuthService.ProjectDto, Task>? OnProjectUpdated;
    public event Func<Guid, Task>? OnProjectDeleted;
    public event Func<Guid, string?, string?, Task>? OnProjectMemberAdded;

    public event Func<ApiAuthService.TaskDto, Task>? OnTaskCreated;
    public event Func<ApiAuthService.TaskDto, Task>? OnTaskUpdated;
    public event Func<Guid, Task>? OnTaskDeleted;

        private readonly ApiAuthService _authService;

        public enum ConnectionState { Connected, Reconnecting, Disconnected }

    public CollaborationService(NavigationManager nav, ILogger<CollaborationService> logger, ApiAuthService authService)
    {
        _logger = logger;
        _authService = authService;
        var hubUrl = new Uri("https+http://apiservice/hubs/collab");
        _hub = new HubConnectionBuilder()
            .WithUrl(hubUrl.ToString(), options =>
            {
                options.AccessTokenProvider = () => Task.FromResult(_authService.JwtToken);
            })
            .WithAutomaticReconnect(new[] { TimeSpan.Zero, TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(30) })
            .Build();

        _hub.On<Guid>("ProjectDeleted", async id => await (OnProjectDeleted?.Invoke(id) ?? Task.CompletedTask));
        _hub.On<ApiAuthService.ProjectDto>("ProjectCreated", async p => await (OnProjectCreated?.Invoke(p) ?? Task.CompletedTask));
        _hub.On<ApiAuthService.ProjectDto>("ProjectUpdated", async p => await (OnProjectUpdated?.Invoke(p) ?? Task.CompletedTask));

        _hub.On<Guid, string, string>("ProjectMemberAdded", async (projectId, userId, role) =>
        {
            if (OnProjectMemberAdded != null) await OnProjectMemberAdded.Invoke(projectId, userId, role);
        });

        _hub.On<ApiAuthService.TaskDto>("TaskCreated", async dto => await (OnTaskCreated?.Invoke(dto) ?? Task.CompletedTask));
        _hub.On<ApiAuthService.TaskDto>("TaskUpdated", async dto => await (OnTaskUpdated?.Invoke(dto) ?? Task.CompletedTask));
        _hub.On<Guid>("TaskDeleted", async id => await (OnTaskDeleted?.Invoke(id) ?? Task.CompletedTask));

        // maintain a set of subscribed project ids for resubscription after reconnect
        _subscribedProjects = new HashSet<Guid>();
    }

    private readonly HashSet<Guid> _subscribedProjects;

    // reference counts, because several components may watch the same project at once
    private readonly Dictionary<Guid, int> _projectRefCounts = new();
    private readonly SemaphoreSlim _startLock = new(1, 1);
    private bool _handlersWired;

        public async Task StartAsync()
        {
            // Starting an already-started hub connection throws, and every component
            // that shows connection state calls this, so it must be idempotent.
            await _startLock.WaitAsync();
            try
            {
                if (_hub.State == HubConnectionState.Connected)
                {
                    return;
                }

                // attempt refresh before starting connection
                await _authService.TryRefreshTokenAsync();

                // wire the reconnect handlers exactly once
                if (!_handlersWired)
                {
                    _hub.Reconnecting += async ex =>
                    {
                        await NotifyConnectionChanged(ConnectionState.Reconnecting);
                    };

                    _hub.Reconnected += async connectionId =>
                    {
                        await NotifyConnectionChanged(ConnectionState.Connected);
                        // resubscribe to project groups
                        await ResubscribeAllAsync();
                    };

                    _handlersWired = true;
                }

                if (_hub.State == HubConnectionState.Disconnected)
                {
                    await _hub.StartAsync();
                }

                await NotifyConnectionChanged(ConnectionState.Connected);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error starting hub connection");
                await NotifyConnectionChanged(ConnectionState.Disconnected);
            }
            finally
            {
                _startLock.Release();
            }
        }

        private async Task NotifyConnectionChanged(ConnectionState state)
        {
            try
            {
                if (OnConnectionChanged != null) await OnConnectionChanged.Invoke(state);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error delivering connection state");
            }
        }

    public async ValueTask DisposeAsync()
    {
        await _hub.DisposeAsync();
    }

    public async Task SubscribeProjectAsync(Guid projectId)
    {
        // count the watchers: the first one joins the group, the rest just bump the count
        var count = _projectRefCounts.TryGetValue(projectId, out var existing) ? existing : 0;
        _projectRefCounts[projectId] = count + 1;

        if (count > 0)
        {
            return;
        }

        if (_subscribedProjects.Add(projectId))
        {
            try
            {
                await _hub.InvokeAsync("JoinProjectGroup", projectId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error subscribing to project {ProjectId}", projectId);
            }
        }
    }

    public async Task UnsubscribeProjectAsync(Guid projectId)
    {
        // only the last watcher actually leaves the group
        if (_projectRefCounts.TryGetValue(projectId, out var count))
        {
            if (count > 1)
            {
                _projectRefCounts[projectId] = count - 1;
                return;
            }

            _projectRefCounts.Remove(projectId);
        }

        if (_subscribedProjects.Remove(projectId))
        {
            try
            {
                await _hub.InvokeAsync("LeaveProjectGroup", projectId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error unsubscribing from project {ProjectId}", projectId);
            }
        }
    }

    private async Task ResubscribeAllAsync()
    {
        foreach (var pid in _subscribedProjects.ToArray())
        {
            try
            {
                await _hub.InvokeAsync("JoinProjectGroup", pid);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to resubscribe to project {ProjectId}", pid);
            }
        }
    }
}
