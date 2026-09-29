using MaarifPlatform.Infrastructure.Auth;
using MaarifPlatform.Api.Dtos;
using MaarifPlatform.Domain.Entities;
using MaarifPlatform.Domain.Enums;
using MaarifPlatform.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace MaarifPlatform.Api.Controllers;

/// <summary>Sprint 7 Auth/RBAC — kullanıcı yönetimi, yalnızca Admin.</summary>
[ApiController]
[Route("api/users")]
[Authorize(Roles = "Admin")]
public class UsersController(MaarifDbContext db, UserManagementService users) : ControllerBase
{
    [HttpPost]
    public async Task<ActionResult<UserResponse>> Create(CreateUserRequest request, CancellationToken ct)
    {
        try
        {
            var user = await users.CreateAsync(request.Name, request.Email, request.Password, request.Role, ct);
            return CreatedAtAction(nameof(GetById), new { id = user.Id }, ToResponse(user));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ex.Message);
        }
    }

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<UserResponse>>> List(CancellationToken ct)
    {
        var users = await db.Users.OrderBy(u => u.Name).ToListAsync(ct);
        return users.Select(ToResponse).ToList();
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<UserResponse>> GetById(Guid id, CancellationToken ct)
    {
        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == id, ct);
        return user is null ? NotFound() : ToResponse(user);
    }

    private static UserResponse ToResponse(AppUser user) =>
        new(user.Id, user.Name, user.Email, user.Role.ToString(), user.CreatedAt);
}
