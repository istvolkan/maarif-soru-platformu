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
public class UsersController(MaarifDbContext db, IPasswordHasher<AppUser> passwordHasher) : ControllerBase
{
    [HttpPost]
    public async Task<ActionResult<UserResponse>> Create(CreateUserRequest request, CancellationToken ct)
    {
        if (!Enum.TryParse<UserRole>(request.Role, ignoreCase: true, out var role))
        {
            return BadRequest($"Geçersiz rol: {request.Role}. Geçerli değerler: {string.Join(", ", Enum.GetNames<UserRole>())}");
        }

        var emailTaken = await db.Users.AnyAsync(u => u.Email == request.Email, ct);
        if (emailTaken)
        {
            return Conflict("Bu e-posta adresiyle zaten bir kullanıcı var.");
        }

        var user = new AppUser
        {
            Name = request.Name,
            Email = request.Email,
            Role = role
        };
        user.PasswordHash = passwordHasher.HashPassword(user, request.Password);

        db.Users.Add(user);
        await db.SaveChangesAsync(ct);

        return CreatedAtAction(nameof(GetById), new { id = user.Id }, ToResponse(user));
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
