# AGENTS.md

## What this is

.NET 10 + Aspire 13.5 demo app (solution file is `AspireApp.slnx`, not `.sln`). No central package management, no `Directory.Build.props`, no lint/format config — CI build + tests are the only gate (`.github/workflows/ci.yml`).

Projects:

- `AspireApp.AppHost` — Aspire orchestrator; the real entrypoint for running the app (`AppHost.cs`). Wires Redis (`cache`), `apiservice`, `webfrontend` with health checks and `WaitFor` ordering.
- `AspireApp.ApiService` — REST API: EF Core + SQLite, Identity, JWT + refresh tokens, SignalR hub at `/hubs/collab`, Swagger in Development.
- `AspireApp.Web` — Blazor (interactive server) frontend. Calls the API via Aspire service discovery (`https+http://apiservice`) and uses Redis output caching (`cache`).
- `AspireApp.ServiceDefaults` — shared resiliency/health/telemetry extensions referenced by ApiService and Web.
- `AspireApp.Pos` — standalone WPF POS desktop app (`net10.0-windows`, Windows-only; has `EnableWindowsTargeting` so the ubuntu CI leg can build the solution). Referenced by the AppHost csproj but not launched as an Aspire resource. Not tested.
- `AspireApp.Tests` — NUnit + `Aspire.Hosting.Testing`; boots the whole AppHost.
- `AspireApp.ApiService.Tests` — xUnit + `WebApplicationFactory` integration tests.

## Commands

```powershell
# Run the whole app (intended way; needs .NET 10 SDK and Docker running for Redis)
dotnet run --project AspireApp.AppHost

# Verify exactly what CI does (order matters: build Release first, then --no-build tests)
dotnet restore AspireApp.slnx
dotnet build AspireApp.slnx --configuration Release --no-restore
dotnet test AspireApp.slnx --no-build -c Release   # runs both test projects

# Single test / focused run (both frameworks use VSTest filters; FullyQualifiedName~ works for NUnit and xUnit)
dotnet test AspireApp.ApiService.Tests/AspireApp.ApiService.Tests.csproj --filter "FullyQualifiedName~TokenServiceTests"
dotnet test AspireApp.Tests/AspireApp.Tests.csproj --filter "FullyQualifiedName~GetWebResourceRootReturnsOkStatusCode"

# EF Core migrations (from repo root)
dotnet ef migrations add <Name> -p AspireApp.ApiService -s AspireApp.ApiService
dotnet ef database update -p AspireApp.ApiService -s AspireApp.ApiService
```

## Gotchas an agent would otherwise miss

- **Two test frameworks:** `AspireApp.Tests` = NUnit (`Assert.That`), `AspireApp.ApiService.Tests` = xUnit (`Assert.Equal`). Don't mix styles or assume one framework repo-wide.
- **`AspireApp.Tests` boots the full AppHost** (spins up Redis container + both apps, 30s timeouts). It requires a container runtime (Docker or Podman); it's slow and the most likely suite to fail on a machine without containers or on CI runners.
- **Tests use environment name `"Testing"`**, not `"Development"`: `ApiService/Program.cs` switches to EF Core InMemory when `EnvironmentName == "Testing"` (tests set it via `builder.UseSetting("environment", "Testing")`). InMemory doesn't support `Migrate()` — code paths must stay guarded by `Database.IsRelational()`.
- **Migrations run automatically at API startup** (`db.Database.Migrate()` in `Program.cs`) and seed data runs too — no manual `database update` needed for dev runs. Migrations live in `AspireApp.ApiService/Migrations/`; never hand-edit generated migration files.
- **Seeded login:** `admin@aspireapp.local` / `Admin123!` (role `Admin`) from `Data/SeedData.cs`.
- **Don't run `AspireApp.Web` standalone with `dotnet run`** — it resolves `https+http://apiservice` and Redis connection `cache` through Aspire service discovery, which only exists when launched from the AppHost (README's "run both separately" flow predates this).
- **SQLite path is relative:** `Data Source=aspireapp.db` resolves against the API's working directory, so the DB file location varies by launch method. `AspireApp.ApiService/aspireapp.db*` are the dev artifacts; treat as disposable, don't commit changes to them.
- **JWT key quirk:** keys shorter than 32 bytes are SHA-256-hashed to derive the signing key (`Program.cs`). Changing `Jwt:Key` invalidates existing tokens.
- **`AspireApp.Pos` is Windows-only** (`net10.0-windows` WPF) and is in the solution (so it builds) but excluded from AppHost and tests. `capture-pos.ps1` screenshots a running `AspireApp.Pos` window into `pos-screenshot.png`.
- Dockerfiles exist in `AspireApp.ApiService/` and `AspireApp.Web/`; there is no deploy/publish pipeline configured (CI publish job is a placeholder).

## Docs

`README.md` is partly stale (separate-run flow, no mention of AppHost/Pos/tests). Trust the csproj/CI/AppHost files over the README.

## CI status

`ci.yml` runs on ubuntu + windows: restore → Release build of the slnx → both test suites (`--no-build`) → coverage report. As of 2026-09-23, all 5 historical runs failed at the coverage step because `ApiService.Tests` was missing from the slnx (fixed: it's now in the solution). If CI fails, check job logs before assuming the tests themselves broke.
