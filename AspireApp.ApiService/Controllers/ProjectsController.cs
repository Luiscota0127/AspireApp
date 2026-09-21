using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using AspireApp.ApiService.Data;
using AspireApp.ApiService.Models;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace AspireApp.ApiService.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class ProjectsController : ControllerBase
{
    private readonly ApplicationDbContext _db;
    private readonly Microsoft.AspNetCore.SignalR.IHubContext<AspireApp.ApiService.Hubs.CollaborationHub> _hub;
    private readonly ILogger<ProjectsController> _logger;

    public ProjectsController(ApplicationDbContext db, Microsoft.AspNetCore.SignalR.IHubContext<AspireApp.ApiService.Hubs.CollaborationHub> hub, ILogger<ProjectsController> logger)
    {
        _db = db;
        _hub = hub;
        _logger = logger;
    }

    [HttpGet]
    public async Task<IActionResult> Get()
    {
        // project into a DTO: returning the entity graph would serialize
        // Project.Tasks and Task.Project back and forth until the JSON depth limit.
        var projects = await _db.Projects
            .AsNoTracking()
            .Select(p => new AspireApp.ApiService.Dto.ProjectCollabDto(p.Id, p.Name, p.Description))
            .ToListAsync();
        return Ok(projects);
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Get(Guid id)
    {
        var project = await _db.Projects
            .AsNoTracking()
            .Where(p => p.Id == id)
            .Select(p => new AspireApp.ApiService.Dto.ProjectCollabDto(p.Id, p.Name, p.Description))
            .FirstOrDefaultAsync();
        if (project == null) return NotFound();
        return Ok(project);
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] ProjectCreateDto dto)
    {
        _logger.LogInformation("Create project called. UserAuthenticated={IsAuthenticated}", User?.Identity?.IsAuthenticated ?? false);
        _logger.LogInformation("Claims: {Claims}", string.Join(';', User?.Claims.Select(c => c.Type + "=" + c.Value) ?? Array.Empty<string>()));
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.Identity?.Name;
        var project = new Project { Name = dto.Name, Description = dto.Description, OwnerId = userId };
        _db.Projects.Add(project);
        await _db.SaveChangesAsync();
        // notify clients
        await _hub.Clients.All.SendCoreAsync("ProjectCreated", new object[] { new AspireApp.ApiService.Dto.ProjectCollabDto(project.Id, project.Name, project.Description) });
        // notify project group subscribers
        await _hub.Clients.Group($"project-{project.Id}").SendCoreAsync("ProjectCreated", new object[] { new AspireApp.ApiService.Dto.ProjectCollabDto(project.Id, project.Name, project.Description) });
        return CreatedAtAction(nameof(Get), new { id = project.Id }, project);
    }

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, [FromBody] ProjectUpdateDto dto)
    {
        var project = await _db.Projects.FindAsync(id);
        if (project == null) return NotFound();
        // allow only owner or admin to update
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.Identity?.Name;
        var isAdmin = User.IsInRole("Admin");
        if (!isAdmin && project.OwnerId != userId)
            return Forbid();

        project.Name = dto.Name;
        project.Description = dto.Description;
        await _db.SaveChangesAsync();
        await _hub.Clients.All.SendCoreAsync("ProjectUpdated", new object[] { new AspireApp.ApiService.Dto.ProjectCollabDto(project.Id, project.Name, project.Description) });
        await _hub.Clients.Group($"project-{project.Id}").SendCoreAsync("ProjectUpdated", new object[] { new AspireApp.ApiService.Dto.ProjectCollabDto(project.Id, project.Name, project.Description) });
        return NoContent();
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id)
    {
        var project = await _db.Projects.FindAsync(id);
        if (project == null) return NotFound();
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.Identity?.Name;
        var isAdmin = User.IsInRole("Admin");
        if (!isAdmin && project.OwnerId != userId)
            return Forbid();

        _db.Projects.Remove(project);
        await _db.SaveChangesAsync();
        await _hub.Clients.All.SendCoreAsync("ProjectDeleted", new object[] { id });
        await _hub.Clients.Group($"project-{id}").SendCoreAsync("ProjectDeleted", new object[] { id });
        return NoContent();
    }

    [HttpPost("{id:guid}/members")]
    public async Task<IActionResult> AddMember(Guid id, [FromBody] AddMemberDto dto)
    {
        var project = await _db.Projects.FindAsync(id);
        if (project == null) return NotFound();

        // only owner or admin
        var userId = User.FindFirstValue(System.Security.Claims.ClaimTypes.NameIdentifier) ?? User.Identity?.Name;
        var isAdmin = User.IsInRole("Admin");
        if (!isAdmin && project.OwnerId != userId) return Forbid();

        var user = await _db.Users.FirstOrDefaultAsync(u => u.Email == dto.Email);
        if (user == null) return BadRequest("User not found");

        if (await _db.ProjectMembers.AnyAsync(pm => pm.ProjectId == id && pm.UserId == user.Id))
            return BadRequest("User already member");

        var member = new ProjectMember { ProjectId = id, UserId = user.Id, Role = dto.Role ?? "Member" };
        _db.ProjectMembers.Add(member);
        await _db.SaveChangesAsync();

        await _hub.Clients.All.SendCoreAsync("ProjectMemberAdded", new object[] { id, user.Id, member.Role });
        await _hub.Clients.Group($"project-{id}").SendCoreAsync("ProjectMemberAdded", new object[] { id, user.Id, member.Role });

        // return a DTO to avoid serializing EF navigation properties (prevents circular-reference errors)
        var resultDto = new AspireApp.ApiService.Dto.ProjectMemberDto(member.Id, member.ProjectId, member.UserId, user.Email, member.Role);
        return Ok(resultDto);
    }

    [HttpDelete("{id:guid}/members/{memberId:guid}")]
    public async Task<IActionResult> RemoveMember(Guid id, Guid memberId)
    {
        var project = await _db.Projects.FindAsync(id);
        if (project == null) return NotFound();

        var member = await _db.ProjectMembers.FindAsync(memberId);
        if (member == null || member.ProjectId != id) return NotFound();

        var userId = User.FindFirstValue(System.Security.Claims.ClaimTypes.NameIdentifier) ?? User.Identity?.Name;
        var isAdmin = User.IsInRole("Admin");
        if (!isAdmin && project.OwnerId != userId) return Forbid();

        _db.ProjectMembers.Remove(member);
        await _db.SaveChangesAsync();

        await _hub.Clients.All.SendCoreAsync("ProjectMemberRemoved", new object[] { id, member.UserId });
        await _hub.Clients.Group($"project-{id}").SendCoreAsync("ProjectMemberRemoved", new object[] { id, member.UserId });
        return NoContent();
    }

    [HttpGet("{id:guid}/members")]
    public async Task<IActionResult> GetMembers(Guid id)
    {
        var members = await _db.ProjectMembers
            .Where(pm => pm.ProjectId == id)
            .Select(pm => new AspireApp.ApiService.Dto.ProjectMemberDto(pm.Id, pm.ProjectId, pm.UserId, pm.User!.Email, pm.Role))
            .ToListAsync();
        return Ok(members);
    }
}

public record AddMemberDto(string Email, string? Role);

public record ProjectCreateDto(string Name, string? Description);
public record ProjectUpdateDto(string Name, string? Description);
