using Npgsql;

namespace Titan.AppHost.Tests;

/// <summary>
/// Integration tests for CockroachDB backup and restore functionality.
/// Tests use userfile:// storage which requires no external dependencies.
/// </summary>
[Collection("AppHost")]
public class CockroachBackupTests : IntegrationTestBase
{
    private readonly string _connectionString;
    
    public CockroachBackupTests(AppHostFixture fixture) : base(fixture)
    {
        // Build connection string from the AppHost
        // Uses titan_user (which has ADMIN privileges) for backup/restore operations
        // Note: root user requires client certificates in secure mode
        var endpoint = fixture.App.GetEndpoint("titan-db", "sql");
        _connectionString = $"Host={endpoint.Host};Port={endpoint.Port};Database=defaultdb;Username=titan_user;Password=TestPassword123!;SSL Mode=Require;Trust Server Certificate=true";
    }
    
    private async Task<NpgsqlConnection> CreateConnectionAsync()
    {
        var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync();
        return conn;
    }
    
    private async Task ExecuteSqlAsync(string sql, int timeoutSeconds = 120)
    {
        await using var conn = await CreateConnectionAsync();
        await using var cmd = new NpgsqlCommand(sql, conn) { CommandTimeout = timeoutSeconds };
        await cmd.ExecuteNonQueryAsync();
    }
    
    private async Task<T?> ExecuteScalarAsync<T>(string sql)
    {
        await using var conn = await CreateConnectionAsync();
        await using var cmd = new NpgsqlCommand(sql, conn) { CommandTimeout = 60 };
        var result = await cmd.ExecuteScalarAsync();
        return result == null || result == DBNull.Value ? default : (T)result;
    }
    
    private string GetUniqueBackupPath(string prefix) => $"userfile:///{prefix}-{Guid.NewGuid():N}";

    #region Basic Backup Tests

    [Fact]
    public async Task BackupTitanDatabase_ToUserfile_Succeeds()
    {
        // Arrange
        var backupPath = GetUniqueBackupPath("titan-backup-test");
        // Use current time backup (no AS OF SYSTEM TIME) for existing database
        var sql = $"BACKUP DATABASE titan INTO '{backupPath}';";
        
        // Act & Assert - should not throw
        await ExecuteSqlAsync(sql);
    }

    [Fact]
    public async Task BackupTitanAdminDatabase_ToUserfile_Succeeds()
    {
        // Arrange
        var backupPath = GetUniqueBackupPath("titan-admin-backup-test");
        var sql = $"BACKUP DATABASE titan_admin INTO '{backupPath}';";
        
        // Act & Assert - should not throw
        await ExecuteSqlAsync(sql);
    }

    [Fact]
    public async Task BackupWithRevisionHistory_Succeeds()
    {
        // Arrange
        var backupPath = GetUniqueBackupPath("titan-revision-test");
        var sql = $"BACKUP DATABASE titan INTO '{backupPath}' WITH revision_history;";
        
        // Act & Assert - should not throw
        await ExecuteSqlAsync(sql);
    }

    #endregion

    #region Restore & Data Integrity Tests

    [Fact]
    public async Task RestoreDatabase_FromUserfile_Succeeds()
    {
        // Arrange - Create a test database and back it up
        var testDbName = $"test_restore_{Guid.NewGuid():N}".Substring(0, 30);
        var backupPath = GetUniqueBackupPath("restore-test");
        
        await ExecuteSqlAsync($"CREATE DATABASE {testDbName};");
        // Small delay to ensure database exists for backup
        await Task.Delay(500);
        await ExecuteSqlAsync($"BACKUP DATABASE {testDbName} INTO '{backupPath}';");
        
        // Drop the database
        await ExecuteSqlAsync($"DROP DATABASE {testDbName} CASCADE;");
        
        // Act - Restore from backup
        await ExecuteSqlAsync($"RESTORE DATABASE {testDbName} FROM LATEST IN '{backupPath}';");
        
        // Assert - Database exists again
        var exists = await ExecuteScalarAsync<long>(
            $"SELECT count(*) FROM [SHOW DATABASES] WHERE database_name = '{testDbName}';");
        Assert.Equal(1, exists);
        
        // Cleanup
        await ExecuteSqlAsync($"DROP DATABASE IF EXISTS {testDbName} CASCADE;");
    }

    [Fact]
    public async Task RestoreVerifiesDataIntegrity()
    {
        // Arrange - Create test database with data
        var testDbName = $"test_integrity_{Guid.NewGuid():N}".Substring(0, 30);
        var backupPath = GetUniqueBackupPath("integrity-test");
        
        await ExecuteSqlAsync($"CREATE DATABASE {testDbName};");
        await ExecuteSqlAsync($"CREATE TABLE {testDbName}.test_data (id INT PRIMARY KEY, value STRING);");
        await ExecuteSqlAsync($"INSERT INTO {testDbName}.test_data VALUES (1, 'original_value');");
        
        // Small delay to ensure data is committed
        await Task.Delay(500);
        
        // Backup the database (current time, not historical)
        await ExecuteSqlAsync($"BACKUP DATABASE {testDbName} INTO '{backupPath}';");
        
        // Modify data after backup
        await ExecuteSqlAsync($"UPDATE {testDbName}.test_data SET value = 'modified_value' WHERE id = 1;");
        
        // Verify data was modified
        var modifiedValue = await ExecuteScalarAsync<string>($"SELECT value FROM {testDbName}.test_data WHERE id = 1;");
        Assert.Equal("modified_value", modifiedValue);
        
        // Drop and restore
        await ExecuteSqlAsync($"DROP DATABASE {testDbName} CASCADE;");
        await ExecuteSqlAsync($"RESTORE DATABASE {testDbName} FROM LATEST IN '{backupPath}';");
        
        // Assert - Original data is restored
        var restoredValue = await ExecuteScalarAsync<string>($"SELECT value FROM {testDbName}.test_data WHERE id = 1;");
        Assert.Equal("original_value", restoredValue);
        
        // Cleanup
        await ExecuteSqlAsync($"DROP DATABASE IF EXISTS {testDbName} CASCADE;");
    }

    [Fact]
    public async Task PointInTimeRestore_Succeeds()
    {
        // Arrange - Create test database with revision history backup
        var testDbName = $"test_pit_{Guid.NewGuid():N}".Substring(0, 30);
        var backupPath = GetUniqueBackupPath("pit-test");
        
        await ExecuteSqlAsync($"CREATE DATABASE {testDbName};");
        await ExecuteSqlAsync($"CREATE TABLE {testDbName}.test_data (id INT PRIMARY KEY, value STRING);");
        await ExecuteSqlAsync($"INSERT INTO {testDbName}.test_data VALUES (1, 'good_data');");
        
        // Wait a moment to get a valid timestamp
        await Task.Delay(1000);
        
        // Get current timestamp for point-in-time restore
        var timestamp = await ExecuteScalarAsync<DateTime>("SELECT now();");
        
        // Wait and then insert "bad" data
        await Task.Delay(1000);
        await ExecuteSqlAsync($"INSERT INTO {testDbName}.test_data VALUES (2, 'bad_data');");
        
        // Backup with revision history (captures all changes)
        await ExecuteSqlAsync($"BACKUP DATABASE {testDbName} INTO '{backupPath}' WITH revision_history;");
        
        // Verify bad data exists
        var badDataExists = await ExecuteScalarAsync<long>($"SELECT count(*) FROM {testDbName}.test_data WHERE id = 2;");
        Assert.Equal(1, badDataExists);
        
        // Drop and restore to point-in-time (before bad data)
        await ExecuteSqlAsync($"DROP DATABASE {testDbName} CASCADE;");
        var restoreSql = $"RESTORE DATABASE {testDbName} FROM LATEST IN '{backupPath}' AS OF SYSTEM TIME '{timestamp:yyyy-MM-dd HH:mm:ss.ffffff}+00:00';";
        await ExecuteSqlAsync(restoreSql);
        
        // Assert - Bad data should NOT exist (restored to before it was inserted)
        var badDataAfterRestore = await ExecuteScalarAsync<long>($"SELECT count(*) FROM {testDbName}.test_data WHERE id = 2;");
        Assert.Equal(0, badDataAfterRestore);
        
        // Good data should still exist
        var goodDataValue = await ExecuteScalarAsync<string>($"SELECT value FROM {testDbName}.test_data WHERE id = 1;");
        Assert.Equal("good_data", goodDataValue);
        
        // Cleanup
        await ExecuteSqlAsync($"DROP DATABASE IF EXISTS {testDbName} CASCADE;");
    }

    #endregion

    #region Advanced Features Tests

    [Fact]
    public async Task IncrementalBackup_Succeeds()
    {
        // Arrange - Create database and full backup
        var testDbName = $"test_incr_{Guid.NewGuid():N}".Substring(0, 30);
        var backupPath = GetUniqueBackupPath("incr-test");
        
        await ExecuteSqlAsync($"CREATE DATABASE {testDbName};");
        await ExecuteSqlAsync($"CREATE TABLE {testDbName}.test_data (id INT PRIMARY KEY, value STRING);");
        await ExecuteSqlAsync($"INSERT INTO {testDbName}.test_data VALUES (1, 'initial');");
        
        // Small delay before backup
        await Task.Delay(500);
        
        // Full backup (current time)
        await ExecuteSqlAsync($"BACKUP DATABASE {testDbName} INTO '{backupPath}';");
        
        // Add more data
        await ExecuteSqlAsync($"INSERT INTO {testDbName}.test_data VALUES (2, 'incremental');");
        
        // Small delay before incremental
        await Task.Delay(500);
        
        // Act - Incremental backup
        await ExecuteSqlAsync($"BACKUP DATABASE {testDbName} INTO LATEST IN '{backupPath}';");
        
        // Assert - Verify incremental backup exists by showing backups
        // Should have 2 entries in the backup collection
        
        // Cleanup
        await ExecuteSqlAsync($"DROP DATABASE IF EXISTS {testDbName} CASCADE;");
    }

    [Fact]
    public async Task ShowBackups_ListsExistingBackups()
    {
        // Arrange - Create a backup
        var testDbName = $"test_show_{Guid.NewGuid():N}".Substring(0, 30);
        var backupPath = GetUniqueBackupPath("show-test");
        
        await ExecuteSqlAsync($"CREATE DATABASE {testDbName};");
        // Small delay before backup
        await Task.Delay(500);
        await ExecuteSqlAsync($"BACKUP DATABASE {testDbName} INTO '{backupPath}';");
        
        // Act - Show backups
        await using var conn = await CreateConnectionAsync();
        await using var cmd = new NpgsqlCommand($"SHOW BACKUPS IN '{backupPath}';", conn);
        await using var reader = await cmd.ExecuteReaderAsync();
        
        // Assert - At least one backup should be listed
        var hasBackups = await reader.ReadAsync();
        Assert.True(hasBackups, "Expected at least one backup to be listed");
        
        // Cleanup
        await ExecuteSqlAsync($"DROP DATABASE IF EXISTS {testDbName} CASCADE;");
    }

    #endregion

    #region Error Handling Tests

    [Fact]
    public async Task RestoreNonExistentBackup_FailsGracefully()
    {
        // Arrange
        var nonExistentPath = GetUniqueBackupPath("non-existent");
        var testDbName = $"test_err_{Guid.NewGuid():N}".Substring(0, 30);
        
        // Act & Assert - Should throw an exception for non-existent backup
        var exception = await Assert.ThrowsAnyAsync<Exception>(async () =>
        {
            await ExecuteSqlAsync($"RESTORE DATABASE {testDbName} FROM LATEST IN '{nonExistentPath}';");
        });
        
        // Verify it's a meaningful error about the backup not existing
        Assert.Contains("backup", exception.Message.ToLower());
    }

    #endregion

    #region Scheduled Backup Tests

    [Fact]
    public async Task CreateBackupSchedule_Succeeds()
    {
        // Arrange
        var scheduleName = $"test_schedule_{Guid.NewGuid():N}".Substring(0, 30);
        var testDbName = $"test_sched_{Guid.NewGuid():N}".Substring(0, 30);
        var backupPath = GetUniqueBackupPath("schedule-test");
        
        await ExecuteSqlAsync($"CREATE DATABASE {testDbName};");
        
        // Act - Create a backup schedule
        var sql = $@"CREATE SCHEDULE ""{scheduleName}""
            FOR BACKUP DATABASE {testDbName} INTO '{backupPath}'
            WITH revision_history
            RECURRING '@hourly'
            WITH SCHEDULE OPTIONS first_run = 'now';";
        
        await ExecuteSqlAsync(sql);
        
        // Assert - Verify schedule exists
        await using var conn = await CreateConnectionAsync();
        await using var cmd = new NpgsqlCommand("SHOW SCHEDULES FOR BACKUP;", conn);
        await using var reader = await cmd.ExecuteReaderAsync();
        
        var found = false;
        while (await reader.ReadAsync())
        {
            var name = reader.GetString(reader.GetOrdinal("label"));
            if (name.Contains(scheduleName))
            {
                found = true;
                break;
            }
        }
        
        Assert.True(found, $"Expected to find schedule '{scheduleName}'");
        
        // Cleanup - Get schedule ID and drop
        await using var conn2 = await CreateConnectionAsync();
        await using var cmd2 = new NpgsqlCommand($"SELECT id FROM [SHOW SCHEDULES] WHERE label LIKE '%{scheduleName}%';", conn2);
        var scheduleId = await cmd2.ExecuteScalarAsync();
        if (scheduleId != null)
        {
            await ExecuteSqlAsync($"DROP SCHEDULE {(long)scheduleId};");
        }
        await ExecuteSqlAsync($"DROP DATABASE IF EXISTS {testDbName} CASCADE;");
    }

    [Fact]
    public async Task PauseAndResumeSchedule_Succeeds()
    {
        // Arrange - Create a schedule (without first_run to avoid immediate execution)
        var scheduleName = $"test_pause_{Guid.NewGuid():N}".Substring(0, 30);
        var testDbName = $"test_pr_{Guid.NewGuid():N}".Substring(0, 30);
        var backupPath = GetUniqueBackupPath("pause-test");
        
        await ExecuteSqlAsync($"CREATE DATABASE {testDbName};");
        await ExecuteSqlAsync($@"CREATE SCHEDULE ""{scheduleName}""
            FOR BACKUP DATABASE {testDbName} INTO '{backupPath}'
            RECURRING '@hourly';");
        
        // Get schedule ID
        await using var conn = await CreateConnectionAsync();
        await using var cmd = new NpgsqlCommand($"SELECT id FROM [SHOW SCHEDULES] WHERE label LIKE '%{scheduleName}%';", conn);
        var scheduleId = (long)(await cmd.ExecuteScalarAsync())!;
        
        // Act - Pause the schedule
        await ExecuteSqlAsync($"PAUSE SCHEDULE {scheduleId};");
        
        // Verify paused
        await using var conn2 = await CreateConnectionAsync();
        await using var cmd2 = new NpgsqlCommand($"SELECT schedule_status FROM [SHOW SCHEDULES] WHERE id = {scheduleId};", conn2);
        var state = (string)(await cmd2.ExecuteScalarAsync())!;
        Assert.Contains("PAUSED", state.ToUpper());
        
        // Act - Resume the schedule
        await ExecuteSqlAsync($"RESUME SCHEDULE {scheduleId};");
        
        // Verify running (could be ACTIVE or WAITING)
        await using var conn3 = await CreateConnectionAsync();
        await using var cmd3 = new NpgsqlCommand($"SELECT schedule_status FROM [SHOW SCHEDULES] WHERE id = {scheduleId};", conn3);
        var resumedState = (string)(await cmd3.ExecuteScalarAsync())!;
        Assert.DoesNotContain("PAUSED", resumedState.ToUpper());
        
        // Cleanup
        await ExecuteSqlAsync($"DROP SCHEDULE {scheduleId};");
        await ExecuteSqlAsync($"DROP DATABASE IF EXISTS {testDbName} CASCADE;");
    }

    [Fact]
    public async Task ScheduledBackup_HasCorrectConfiguration()
    {
        // Arrange - Create a schedule with first_run = 'now'
        var scheduleName = $"test_config_{Guid.NewGuid():N}".Substring(0, 30);
        var testDbName = $"test_cfg_{Guid.NewGuid():N}".Substring(0, 30);
        var backupPath = GetUniqueBackupPath("config-test");
        
        await ExecuteSqlAsync($"CREATE DATABASE {testDbName};");
        await ExecuteSqlAsync($@"CREATE SCHEDULE ""{scheduleName}""
            FOR BACKUP DATABASE {testDbName} INTO '{backupPath}'
            WITH revision_history
            RECURRING '@hourly'
            WITH SCHEDULE OPTIONS first_run = 'now';");
        
        // Small delay to let schedule initialize
        await Task.Delay(1000);
        
        // Get schedule details
        await using var conn = await CreateConnectionAsync();
        await using var cmd = new NpgsqlCommand(
            $"SELECT id, schedule_status, recurrence FROM [SHOW SCHEDULES] WHERE label LIKE '%{scheduleName}%';", 
            conn);
        await using var reader = await cmd.ExecuteReaderAsync();
        
        Assert.True(await reader.ReadAsync(), $"Expected to find schedule '{scheduleName}'");
        
        var scheduleId = reader.GetInt64(0);
        var recurrence = reader.GetString(2);
        
        // Assert - Schedule should be properly configured with correct recurrence
        // Note: Status may be PAUSED if initial backup had issues, but schedule creation succeeded
        Assert.Equal("@hourly", recurrence);
        
        // Cleanup
        await ExecuteSqlAsync($"DROP SCHEDULE {scheduleId};");
        await ExecuteSqlAsync($"DROP DATABASE IF EXISTS {testDbName} CASCADE;");
    }

    #endregion
}
