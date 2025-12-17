using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Titan.Abstractions.Grains;
using Titan.API.Services;

namespace Titan.API.Controllers;

/// <summary>
/// Player account management endpoints for admins.
/// </summary>
[ApiController]
[Route("api/admin/accounts")]
[Tags("Admin - Accounts")]
[Authorize(Policy = "AdminDashboard")]
public class AccountsController : ControllerBase
{
    private readonly IClusterClient _clusterClient;
    private readonly AccountQueryService _accountQuery;
    private readonly ILogger<AccountsController> _logger;

    public AccountsController(
        IClusterClient clusterClient,
        AccountQueryService accountQuery,
        ILogger<AccountsController> logger)
    {
        _clusterClient = clusterClient;
        _accountQuery = accountQuery;
        _logger = logger;
    }

    /// <summary>
    /// Get all accounts (max 1000).
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<List<AccountSummary>>> GetAll()
    {
        // GetAll still uses DB query as it needs to scan all accounts
        var accounts = await _accountQuery.GetAllAccountsAsync();
        return Ok(accounts);
    }

    /// <summary>
    /// Get account details by ID.
    /// </summary>
    [HttpGet("{id:guid}")]
    public async Task<ActionResult<AccountDetailDto>> GetById(Guid id)
    {
        var grain = _clusterClient.GetGrain<IAccountGrain>(id);
        
        // Use Orleans RecordExists via grain method - doesn't auto-create
        if (!await grain.ExistsAsync())
        {
            return NotFound();
        }

        var account = await grain.GetAccountAsync();

        return Ok(new AccountDetailDto
        {
            AccountId = account.AccountId,
            CreatedAt = account.CreatedAt,
            UnlockedCosmetics = account.UnlockedCosmetics.ToList(),
            UnlockedAchievements = account.UnlockedAchievements.ToList()
        });
    }

    /// <summary>
    /// Get characters for an account.
    /// </summary>
    [HttpGet("{id:guid}/characters")]
    public async Task<ActionResult<List<CharacterSummaryDto>>> GetCharacters(Guid id)
    {
        var grain = _clusterClient.GetGrain<IAccountGrain>(id);
        
        // Use Orleans RecordExists via grain method
        if (!await grain.ExistsAsync())
        {
            return NotFound();
        }

        var characters = await grain.GetCharactersAsync();
        
        return Ok(characters.Select(c => new CharacterSummaryDto
        {
            CharacterId = c.CharacterId,
            Name = c.Name,
            SeasonId = c.SeasonId,
            Level = c.Level,
            IsDead = c.IsDead,
            Restrictions = c.Restrictions.ToString(),
            CreatedAt = c.CreatedAt
        }).OrderByDescending(c => c.CreatedAt).ToList());
    }

    /// <summary>
    /// Create a new account.
    /// </summary>
    [HttpPost]
    public async Task<ActionResult<AccountDetailDto>> Create()
    {
        var accountId = Guid.NewGuid();
        var grain = _clusterClient.GetGrain<IAccountGrain>(accountId);
        var account = await grain.GetAccountAsync();

        _logger.LogInformation("Created account {AccountId}", accountId);

        return CreatedAtAction(nameof(GetById), new { id = accountId }, new AccountDetailDto
        {
            AccountId = account.AccountId,
            CreatedAt = account.CreatedAt,
            UnlockedCosmetics = account.UnlockedCosmetics.ToList(),
            UnlockedAchievements = account.UnlockedAchievements.ToList()
        });
    }

    /// <summary>
    /// Delete an account.
    /// </summary>
    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id)
    {
        var grain = _clusterClient.GetGrain<IAccountGrain>(id);
        
        // Check existence before deleting
        if (!await grain.ExistsAsync())
        {
            return NotFound();
        }

        // Clear grain state via Orleans
        await grain.DeleteAsync();
        
        // Also delete the row from DB directly to ensure GetAllAccountsAsync doesn't find it
        // ClearStateAsync may not delete the row in all storage providers
        await _accountQuery.DeleteAccountAsync(id);
        
        _logger.LogInformation("Deleted account {AccountId}", id);
        return NoContent();
    }
}


// DTOs

public record AccountDetailDto
{
    public Guid AccountId { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
    public List<string> UnlockedCosmetics { get; init; } = [];
    public List<string> UnlockedAchievements { get; init; } = [];
}

public record CharacterSummaryDto
{
    public Guid CharacterId { get; init; }
    public required string Name { get; init; }
    public required string SeasonId { get; init; }
    public int Level { get; init; }
    public bool IsDead { get; init; }
    public required string Restrictions { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
}
