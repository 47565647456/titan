using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Titan.API.Data;
using Titan.Abstractions.RateLimiting;

namespace Titan.API.Controllers;

/// <summary>
/// Admin user management endpoints for the dashboard.
/// Requires SuperAdmin role for all operations.
/// </summary>
[ApiController]
[Route("api/admin/users")]
[Tags("Admin - Users")]
[Authorize(Policy = "SuperAdmin")]
[RateLimitPolicy("Admin")]
public class AdminUsersController : ControllerBase
{
    private readonly UserManager<AdminUser> _userManager;
    private readonly RoleManager<AdminRole> _roleManager;
    private readonly ILogger<AdminUsersController> _logger;
    private readonly IValidator<CreateAdminUserRequest> _createValidator;
    private readonly IValidator<UpdateAdminUserRequest> _updateValidator;

    public AdminUsersController(
        UserManager<AdminUser> userManager,
        RoleManager<AdminRole> roleManager,
        ILogger<AdminUsersController> logger,
        IValidator<CreateAdminUserRequest> createValidator,
        IValidator<UpdateAdminUserRequest> updateValidator)
    {
        _userManager = userManager;
        _roleManager = roleManager;
        _logger = logger;
        _createValidator = createValidator;
        _updateValidator = updateValidator;
    }


    /// <summary>
    /// Get all admin users.
    /// </summary>
    /// <returns>List of all admin users.</returns>
    [HttpGet]
    [ProducesResponseType<List<AdminUserDto>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<List<AdminUserDto>>> GetAll()
    {
        var users = await _userManager.Users.ToListAsync();
        var result = new List<AdminUserDto>();

        foreach (var user in users)
        {
            var roles = await _userManager.GetRolesAsync(user);
            result.Add(new AdminUserDto
            {
                Id = user.Id,
                Email = user.Email!,
                DisplayName = user.DisplayName,
                Roles = roles.ToList(),
                CreatedAt = user.CreatedAt,
                LastLoginAt = user.LastLoginAt
            });
        }

        return Ok(result.OrderBy(u => u.Email));
    }

    /// <summary>
    /// Get available roles.
    /// </summary>
    /// <returns>List of available role names.</returns>
    [HttpGet("roles")]
    [ProducesResponseType<List<string>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<List<string>>> GetRoles()
    {
        var roles = await _roleManager.Roles.Select(r => r.Name!).ToListAsync();
        return Ok(roles);
    }

    /// <summary>
    /// Create a new admin user.
    /// </summary>
    /// <param name="request">User creation details including email, password, and roles.</param>
    /// <returns>The created admin user.</returns>
    [HttpPost]
    [ProducesResponseType<AdminUserDto>(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<AdminUserDto>> Create([FromBody] CreateAdminUserRequest request)
    {
        var validationResult = await _createValidator.ValidateAsync(request);
        if (!validationResult.IsValid)
        {
            return BadRequest(new { errors = validationResult.Errors.Select(e => e.ErrorMessage) });
        }

        var user = new AdminUser
        {
            UserName = request.Email,
            Email = request.Email,
            EmailConfirmed = true,
            DisplayName = request.DisplayName
        };

        var createResult = await _userManager.CreateAsync(user, request.Password);
        if (!createResult.Succeeded)
        {
            return BadRequest(new { errors = createResult.Errors.Select(e => e.Description) });
        }

        if (request.Roles.Count > 0)
        {
            await _userManager.AddToRolesAsync(user, request.Roles);
        }

        _logger.LogInformation("Created admin user {Email}", request.Email);

        var roles = await _userManager.GetRolesAsync(user);
        return CreatedAtAction(nameof(GetById), new { id = user.Id }, new AdminUserDto
        {
            Id = user.Id,
            Email = user.Email,
            DisplayName = user.DisplayName,
            Roles = roles.ToList(),
            CreatedAt = user.CreatedAt,
            LastLoginAt = user.LastLoginAt
        });
    }

    /// <summary>
    /// Get admin user by ID.
    /// </summary>
    /// <param name="id">The admin user identifier.</param>
    /// <returns>The requested admin user.</returns>
    [HttpGet("{id:guid}")]
    [ProducesResponseType<AdminUserDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<AdminUserDto>> GetById(Guid id)
    {
        var user = await _userManager.FindByIdAsync(id.ToString());
        if (user == null)
        {
            return NotFound();
        }

        var roles = await _userManager.GetRolesAsync(user);
        return Ok(new AdminUserDto
        {
            Id = user.Id,
            Email = user.Email!,
            DisplayName = user.DisplayName,
            Roles = roles.ToList(),
            CreatedAt = user.CreatedAt,
            LastLoginAt = user.LastLoginAt
        });
    }

    /// <summary>
    /// Update admin user.
    /// </summary>
    /// <param name="id">The admin user identifier.</param>
    /// <param name="request">Updated user details including roles.</param>
    /// <returns>The updated admin user.</returns>
    [HttpPut("{id:guid}")]
    [ProducesResponseType<AdminUserDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<AdminUserDto>> Update(Guid id, [FromBody] UpdateAdminUserRequest request)
    {
        var validationResult = await _updateValidator.ValidateAsync(request);
        if (!validationResult.IsValid)
        {
            return BadRequest(new { errors = validationResult.Errors.Select(e => e.ErrorMessage) });
        }

        var user = await _userManager.FindByIdAsync(id.ToString());
        if (user == null)
        {
            return NotFound();
        }

        user.DisplayName = request.DisplayName;
        var updateResult = await _userManager.UpdateAsync(user);
        if (!updateResult.Succeeded)
        {
            return BadRequest(new { errors = updateResult.Errors.Select(e => e.Description) });
        }

        // Update roles
        var currentRoles = await _userManager.GetRolesAsync(user);
        var rolesToRemove = currentRoles.Except(request.Roles).ToList();
        var rolesToAdd = request.Roles.Except(currentRoles).ToList();

        // Prevent removing SuperAdmin role if this would leave zero SuperAdmins
        if (currentRoles.Contains("SuperAdmin") && !request.Roles.Contains("SuperAdmin"))
        {
            var superAdminCount = (await _userManager.GetUsersInRoleAsync("SuperAdmin")).Count;
            if (superAdminCount <= 1)
            {
                return BadRequest(new { error = "Cannot remove the last SuperAdmin. At least one SuperAdmin must exist." });
            }
        }

        if (rolesToRemove.Count > 0)
        {
            await _userManager.RemoveFromRolesAsync(user, rolesToRemove);
        }
        if (rolesToAdd.Count > 0)
        {
            await _userManager.AddToRolesAsync(user, rolesToAdd);
        }

        _logger.LogInformation("Updated admin user {Email}", user.Email);

        var roles = await _userManager.GetRolesAsync(user);
        return Ok(new AdminUserDto
        {
            Id = user.Id,
            Email = user.Email!,
            DisplayName = user.DisplayName,
            Roles = roles.ToList(),
            CreatedAt = user.CreatedAt,
            LastLoginAt = user.LastLoginAt
        });
    }

    /// <summary>
    /// Delete admin user.
    /// </summary>
    /// <param name="id">The admin user identifier to delete.</param>
    /// <returns>No content on success.</returns>
    [HttpDelete("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Delete(Guid id)
    {
        var user = await _userManager.FindByIdAsync(id.ToString());
        if (user == null)
        {
            return NotFound();
        }

        // Prevent SuperAdmin from deleting themselves
        var currentUserId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        if (id.ToString() == currentUserId)
        {
            return BadRequest(new { error = "You cannot delete your own account." });
        }

        // Prevent deleting the last SuperAdmin
        var userRoles = await _userManager.GetRolesAsync(user);
        if (userRoles.Contains("SuperAdmin"))
        {
            var superAdminCount = (await _userManager.GetUsersInRoleAsync("SuperAdmin")).Count;
            if (superAdminCount <= 1)
            {
                return BadRequest(new { error = "Cannot delete the last SuperAdmin. At least one SuperAdmin must exist." });
            }
        }

        var result = await _userManager.DeleteAsync(user);
        if (!result.Succeeded)
        {
            return BadRequest(new { errors = result.Errors.Select(e => e.Description) });
        }

        _logger.LogInformation("Deleted admin user {Email}", user.Email);
        return NoContent();
    }
}

// DTOs

public record AdminUserDto
{
    public Guid Id { get; init; }
    public required string Email { get; init; }
    public string? DisplayName { get; init; }
    public required List<string> Roles { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset? LastLoginAt { get; init; }
}

public record CreateAdminUserRequest
{
    public required string Email { get; init; }
    public required string Password { get; init; }
    public string? DisplayName { get; init; }
    public List<string> Roles { get; init; } = [];
}

public record UpdateAdminUserRequest
{
    public string? DisplayName { get; init; }
    public List<string> Roles { get; init; } = [];
}
