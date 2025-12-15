using System.Text.Json;
using Npgsql;
using Titan.Abstractions.Models;

namespace Titan.Dashboard.Services;

/// <summary>
/// Service to query accounts directly from the Orleans grain storage database.
/// This is a simple approach for admin tools - bypasses Orleans for listing all accounts.
/// </summary>
public class AccountQueryService
{
    private readonly string _connectionString;
    private readonly ILogger<AccountQueryService> _logger;

    public AccountQueryService(IConfiguration configuration, ILogger<AccountQueryService> logger)
    {
        // Use the main titan database connection string
        _connectionString = configuration.GetConnectionString("titan") 
            ?? throw new InvalidOperationException("Connection string 'titan' not found");
        _logger = logger;
    }

    /// <summary>
    /// Gets all accounts from the grain storage.
    /// Queries OrleansStorage for grains with type containing "AccountGrain".
    /// </summary>
    public async Task<List<AccountSummary>> GetAllAccountsAsync()
    {
        var accounts = new List<AccountSummary>();

        try
        {
            await using var conn = new NpgsqlConnection(_connectionString);
            await conn.OpenAsync();

            // Check total rows in OrleansStorage table
            await using var countCmd = new NpgsqlCommand("SELECT COUNT(*) FROM orleansstorage", conn);
            var totalRows = await countCmd.ExecuteScalarAsync();
            _logger.LogInformation("Total rows in OrleansStorage: {TotalRows}", totalRows);

            // Log what grain types exist for debugging
            await using var debugCmd = new NpgsqlCommand("SELECT DISTINCT graintypestring FROM orleansstorage LIMIT 20", conn);
            await using var debugReader = await debugCmd.ExecuteReaderAsync();
            while (await debugReader.ReadAsync())
            {
                _logger.LogInformation("Found grain type in DB: {GrainType}", debugReader.GetString(0));
            }
            await debugReader.CloseAsync();

            // Query for all account entries
            await using var cmd = new NpgsqlCommand(@"
                SELECT grainidn0, grainidn1, payloadbinary, modifiedon, graintypestring
                FROM orleansstorage 
                WHERE graintypestring LIKE '%account%' 
                   OR graintypestring LIKE '%Account%'
                   OR graintypestring LIKE '%IAccountGrain%'
                ORDER BY modifiedon DESC
                LIMIT 1000
            ", conn);

            await using var reader = await cmd.ExecuteReaderAsync();
            
            while (await reader.ReadAsync())
            {
                try
                {
                    // Reconstruct Guid from grainidn0 and grainidn1
                    var n0 = reader.GetInt64(0);
                    var n1 = reader.GetInt64(1);
                    var accountId = ReconstructGuid(n0, n1);
                    
                    var modifiedOn = reader.GetDateTime(3);
                    var grainType = reader.GetString(4);
                    
                    _logger.LogInformation("Found grain: {GrainType} with ID {AccountId}", grainType, accountId);

                    // Try to parse the payload for additional details
                    DateTimeOffset createdAt = modifiedOn;
                    int characterCount = 0;
                    
                    if (!reader.IsDBNull(2))
                    {
                        var payload = (byte[])reader[2];
                        // Payload is MemoryPack serialized, we can't easily deserialize here
                        // Just use modified date as created date approximation
                    }

                    accounts.Add(new AccountSummary
                    {
                        AccountId = accountId,
                        CreatedAt = createdAt,
                        LastModified = modifiedOn,
                        CharacterCount = characterCount
                    });
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to parse account row");
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to query accounts from database");
            throw;
        }

        return accounts;
    }

    /// <summary>
    /// Deletes an account by clearing its grain state.
    /// </summary>
    public async Task<bool> DeleteAccountAsync(Guid accountId)
    {
        try
        {
            await using var conn = new NpgsqlConnection(_connectionString);
            await conn.OpenAsync();

            var (n0, n1) = DeconstructGuid(accountId);

            await using var cmd = new NpgsqlCommand(@"
                DELETE FROM orleansstorage 
                WHERE grainidn0 = @n0 AND grainidn1 = @n1 
                AND graintypestring LIKE '%AccountGrain%'
            ", conn);
            
            cmd.Parameters.AddWithValue("n0", n0);
            cmd.Parameters.AddWithValue("n1", n1);

            var rowsAffected = await cmd.ExecuteNonQueryAsync();
            
            _logger.LogInformation("Deleted account {AccountId}, rows affected: {RowsAffected}", accountId, rowsAffected);
            return rowsAffected > 0;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to delete account {AccountId}", accountId);
            throw;
        }
    }

    /// <summary>
    /// Reconstructs a Guid from Orleans grain key components.
    /// Orleans stores Guids as two 64-bit integers.
    /// </summary>
    private static Guid ReconstructGuid(long n0, long n1)
    {
        var bytes = new byte[16];
        BitConverter.GetBytes(n0).CopyTo(bytes, 0);
        BitConverter.GetBytes(n1).CopyTo(bytes, 8);
        return new Guid(bytes);
    }

    /// <summary>
    /// Deconstructs a Guid into Orleans grain key components.
    /// </summary>
    private static (long n0, long n1) DeconstructGuid(Guid guid)
    {
        var bytes = guid.ToByteArray();
        var n0 = BitConverter.ToInt64(bytes, 0);
        var n1 = BitConverter.ToInt64(bytes, 8);
        return (n0, n1);
    }
}

/// <summary>
/// Summary of an account for listing purposes.
/// </summary>
public record AccountSummary
{
    public Guid AccountId { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset LastModified { get; init; }
    public int CharacterCount { get; init; }
}
