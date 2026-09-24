# Plan: Resume the Kanban web project

Status: in progress (Phase 0)

## Phase 0 — Verify and checkpoint the WIP

1. Run CI-equivalent verification from repo root:
   - `dotnet restore AspireApp.slnx`
   - `dotnet build AspireApp.slnx -c Release --no-restore`
   - `dotnet test AspireApp.ApiService.Tests/AspireApp.ApiService.Tests.csproj -c Release --no-build`
   - `dotnet test AspireApp.slnx -c Release --no-build`
   - If the Pos WPF project breaks the Linux leg of CI (it's `net10.0-windows` with no `EnableWindowsTargeting`), fix the csproj or workflow as part of the checkpoint.
2. Commit the checkpoint in logical pieces:
   - Web UI redesign (ShellChrome, nav rail, Kanban restyle, `boardDrag.js`, `aspireShell.js`)
   - `AspireApp.Pos` + its `slnx`/AppHost references
   - `AGENTS.md`
3. Exclude the SQLite dev database (`aspireapp.db*`) from the commit — restore them (disposable; seeded admin + migrations recreate on next run).
4. Reconcile AGENTS.md: it says Pos is "not part of the AppHost", but the uncommitted change adds an AppHost `ProjectReference`. Adjust wording (referenced for build, not launched as an Aspire resource).

## Phase 1 — Project CRUD UI

1. `ApiAuthService`: add `CreateProjectAsync`, `UpdateProjectAsync`, `DeleteProjectAsync` (API endpoints already exist: `POST/PUT/DELETE api/projects`).
2. `Dashboard.razor`:
   - "Nuevo proyecto" form (name + optional description) — what the Kanban empty state already links to.
   - Rename and delete actions per project card, two-step delete confirm (same pattern as Kanban drawer); surface 403 as "solo el propietario o un admin".
3. Real-time already works — Dashboard reloads on `ProjectCreated/Updated/Deleted` SignalR events; no collab changes needed.

## Phase 2 — API fixes (fix-as-we-go, with tests)

1. Scope filtering:
   - `GET api/projects` → only projects the caller owns or is a member of (Admin sees all).
   - `GET api/tasks` → only tasks in those projects.
   - Add/adjust tests in `AspireApp.ApiService.Tests` (home: `ProjectMembersAuthorizationTests`); run the xUnit suite after.
2. Assign-by-email bug: resolve entered email to member `UserId` in the Kanban composer before `CreateTaskAsync` (members list already loaded there), so `AssignedToId` is always a real user id. Optionally have API reject `AssignedToId` values that aren't project members.

## Phase 3 — Verify and commit

- Re-run full CI-equivalent sequence after each phase; commit per phase (`web: ...`, `api: ...` style).
- Task-editing drawer (title/description/due date) is out of scope — next feature after this; Phase 2 fixes clear its blockers.

Order: Phase 0 first (clean checkpoint), then Phase 1, then Phase 2 (filtering tests land with the filtering change).
