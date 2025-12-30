using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Titan.Abstractions.Grains;
using Titan.API.Services;
using Titan.Abstractions.RateLimiting;

namespace Titan.API.Controllers;

/// <summary>
/// Player account management endpoints for admins.
/// </summary>
[ApiController]
[Route("api/admin/accounts")]
[Tags("Admin - Accounts")]
[Authorize(Policy = "AdminDashboard")]
[RateLimitPolicy("Admin")]
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
    /// <returns>List of account summaries.</returns>
    [HttpGet]
    [ProducesResponseType<List<AccountSummary>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<List<AccountSummary>>> GetAll()
    {
        // GetAll still uses DB query as it needs to scan all accounts
        var accounts = await _accountQuery.GetAllAccountsAsync();
        return Ok(accounts);
    }

    /// <summary>
    /// Get account details by ID.
    /// </summary>
    /// <param name="id">The account identifier.</param>
    /// <returns>Account details including cosmetics and achievements.</returns>
    [HttpGet("{id:guid}")]
    [ProducesResponseType<AccountDetailDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
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
    /// <param name="id">The account identifier.</param>
    /// <returns>List of character summaries for the account.</returns>
    [HttpGet("{id:guid}/characters")]
    [ProducesResponseType<List<CharacterSummaryDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
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
    /// <returns>The newly created account.</returns>
    [HttpPost]
    [ProducesResponseType<AccountDetailDto>(StatusCodes.Status201Created)]
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
    /// <param name="id">The account identifier to delete.</param>
    /// <returns>No content on success.</returns>
    [HttpDelete("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
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
