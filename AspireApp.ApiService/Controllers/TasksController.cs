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
public class TasksController : ControllerBase
{
    private readonly ApplicationDbContext _db;
    private readonly Microsoft.AspNetCore.SignalR.IHubContext<AspireApp.ApiService.Hubs.CollaborationHub> _hub;

    public TasksController(ApplicationDbContext db, Microsoft.AspNetCore.SignalR.IHubContext<AspireApp.ApiService.Hubs.CollaborationHub> hub)
    {
        _db = db;
        _hub = hub;
    }

    [HttpGet]
    public async Task<IActionResult> Get()
    {
        // project the entities into DTOs so EF navigation properties are never serialized
        var tasks = await _db.Tasks
            .AsNoTracking()
            .Select(t => new AspireApp.ApiService.Dto.TaskCollabDto(
                t.Id, t.Title, t.Description, (int)t.Status, t.ProjectId, t.AssignedToId))
            .ToListAsync();
        return Ok(tasks);
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Get(Guid id)
    {
        var task = await _db.Tasks
            .AsNoTracking()
            .Where(t => t.Id == id)
            .Select(t => new AspireApp.ApiService.Dto.TaskCollabDto(
                t.Id, t.Title, t.Description, (int)t.Status, t.ProjectId, t.AssignedToId))
            .FirstOrDefaultAsync();
        if (task == null) return NotFound();
        return Ok(task);
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] TaskCreateDto dto)
    {
        var project = await _db.Projects.FindAsync(dto.ProjectId);
        if (project == null) return BadRequest("Project not found");

        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.Identity?.Name;
        var task = new TaskItem { Title = dto.Title, Description = dto.Description, ProjectId = dto.ProjectId, AssignedToId = dto.AssignedToId };
        _db.Tasks.Add(task);
        await _db.SaveChangesAsync();
        var collabDto = new AspireApp.ApiService.Dto.TaskCollabDto(task.Id, task.Title, task.Description, (int)task.Status, task.ProjectId, task.AssignedToId);
        await _hub.Clients.All.SendCoreAsync("TaskCreated", new object[] { collabDto });
        await _hub.Clients.Group($"project-{task.ProjectId}").SendCoreAsync("TaskCreated", new object[] { collabDto });
        // return the DTO, not the EF entity: task.Project is tracked, and serializing
        // the entity would walk Project.Tasks -> Task.Project -> ... until the JSON depth limit.
        return CreatedAtAction(nameof(Get), new { id = task.Id }, collabDto);
    }

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, [FromBody] TaskUpdateDto dto)
    {
        var task = await _db.Tasks.FindAsync(id);
        if (task == null) return NotFound();
        // allow only assigned user, project owner or admin
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.Identity?.Name;
        var isAdmin = User.IsInRole("Admin");
        var project = await _db.Projects.FindAsync(task.ProjectId);
        var isOwner = project != null && project.OwnerId == userId;
        if (!isAdmin && task.AssignedToId != userId && !isOwner)
            return Forbid();

        task.Title = dto.Title;
        task.Description = dto.Description;
        task.Status = dto.Status;
        task.DueDate = dto.DueDate;
        task.AssignedToId = dto.AssignedToId;
        await _db.SaveChangesAsync();
        var collabDto = new AspireApp.ApiService.Dto.TaskCollabDto(task.Id, task.Title, task.Description, (int)task.Status, task.ProjectId, task.AssignedToId);
        await _hub.Clients.All.SendCoreAsync("TaskUpdated", new object[] { collabDto });
        await _hub.Clients.Group($"project-{task.ProjectId}").SendCoreAsync("TaskUpdated", new object[] { collabDto });
        return NoContent();
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id)
    {
        var task = await _db.Tasks.FindAsync(id);
        if (task == null) return NotFound();
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.Identity?.Name;
        var isAdmin = User.IsInRole("Admin");
        var project = await _db.Projects.FindAsync(task.ProjectId);
        var isOwner = project != null && project.OwnerId == userId;
        if (!isAdmin && task.AssignedToId != userId && !isOwner)
            return Forbid();

        _db.Tasks.Remove(task);
        await _db.SaveChangesAsync();
        await _hub.Clients.All.SendCoreAsync("TaskDeleted", new object[] { id });
        await _hub.Clients.Group($"project-{task.ProjectId}").SendCoreAsync("TaskDeleted", new object[] { id });
        return NoContent();
    }
}

public record TaskCreateDto(Guid ProjectId, string Title, string? Description, string? AssignedToId);
public record TaskUpdateDto(string Title, string? Description, AspireApp.ApiService.Models.TaskStatus Status, DateTime? DueDate, string? AssignedToId);
