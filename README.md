# AspireApp

A fullstack reference app on **.NET Aspire**: an orchestrated host, a REST API with JWT authentication, and a Blazor Server frontend wired together through service discovery.

It's the kind of starter you wish you'd had when picking up Aspire — service orchestration, auth with refresh tokens, caching, Dockerfiles, and two real test suites, all wired up.

## Projects

| Project | What it is |
|---|---|
| `AspireApp.AppHost` | The Aspire orchestrator and intended entrypoint. Wires Redis, the API, and the web frontend with health checks and `WaitFor` startup ordering. |
| `AspireApp.ApiService` | REST API: EF Core + SQLite, ASP.NET Core Identity, JWT + refresh tokens, SignalR hub at `/hubs/collab`, Swagger in Development. |
| `AspireApp.Web` | Blazor Server frontend. Calls the API through Aspire service discovery (`https+http://apiservice`) and uses Redis output caching. |
| `AspireApp.ServiceDefaults` | Shared health checks, resilience, and telemetry extensions referenced by both apps. |
| `AspireApp.Pos` | Standalone WPF point-of-sale client (`net10.0-windows`). Windows-only, not launched as an Aspire resource, not covered by tests. |
| `AspireApp.Tests` | NUnit + `Aspire.Hosting.Testing` — boots the entire AppHost. |
| `AspireApp.ApiService.Tests` | xUnit + `WebApplicationFactory` integration tests. |

## Running it

Needs the .NET 10 SDK and a container runtime (Docker or Podman) for Redis.

```bash
dotnet run --project AspireApp.AppHost
```

## Setup

Apply migrations to create the database:

```bash
dotnet tool install --global dotnet-ef
dotnet ef migrations add InitialCreate -p AspireApp.ApiService -s AspireApp.ApiService
dotnet ef database update -p AspireApp.ApiService -s AspireApp.ApiService
```

## Tests

```bash
dotnet restore AspireApp.slnx
dotnet build AspireApp.slnx --configuration Release --no-restore
dotnet test AspireApp.slnx --no-build -c Release
```

Single suite or focused run:

```bash
dotnet test AspireApp.ApiService.Tests/AspireApp.ApiService.Tests.csproj \
  --filter "FullyQualifiedName~TokenServiceTests"
```

### Things that will bite you

- **Two test frameworks.** `AspireApp.Tests` is NUnit (`Assert.That`); `AspireApp.ApiService.Tests` is xUnit (`Assert.Equal`). Don't mix styles.
- **`AspireApp.Tests` boots the full AppHost** — it spins up a Redis container and both applications with 30s timeouts. It needs a container runtime, it's slow, and it's the most likely suite to fail on a machine without Docker.
- **The solution is `AspireApp.slnx`, not `.sln`.** Tools that only understand `.sln` will not find it.

## Before deploying

- Replace `Jwt:Key` in `AspireApp.ApiService/appsettings.json` with a real secret
- If the frontend and API run on different hosts or ports, add CORS (`AddCors` / `UseCors` in `Program.cs`)

## Possible next steps

- Automatic token refresh in Blazor
- More integration and end-to-end test coverage
