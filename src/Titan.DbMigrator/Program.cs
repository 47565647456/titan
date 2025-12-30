using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Titan.API.Data;

Console.WriteLine("🔄 Titan Database Migrator starting...");

// Get connection strings from environment (Aspire sets these via WithReference)
// titan = Orleans storage database
// titan-admin = Admin Identity database
var titanConnectionString = Environment.GetEnvironmentVariable("ConnectionStrings__titan");
var adminConnectionString = Environment.GetEnvironmentVariable("ConnectionStrings__titan-admin");

if (string.IsNullOrEmpty(titanConnectionString))
{
    Console.WriteLine("❌ Error: ConnectionStrings__titan environment variable not set");
    Environment.Exit(1);
}

try
{
    // Wait for PostgreSQL to be ready
    Console.WriteLine("   Waiting for PostgreSQL to be ready...");
    await WaitForPostgresAsync(titanConnectionString);
    Console.WriteLine("✅ PostgreSQL is ready!");

    // Apply Orleans schema
    Console.WriteLine("📦 Applying Orleans schema...");
    await ApplyOrleansSchemaAsync(titanConnectionString);
    Console.WriteLine("✅ Orleans schema applied!");

    // Apply Admin Identity schema via EF migrations
    if (!string.IsNullOrEmpty(adminConnectionString))
    {
        Console.WriteLine("📦 Applying Admin Identity schema (EF migrations)...");
        await ApplyAdminMigrationsAsync(adminConnectionString);
        Console.WriteLine("✅ Admin Identity schema applied!");
    }
    else
    {
        Console.WriteLine("⚠️ Skipping Admin Identity migrations (connection string not set)");
    }

    Console.WriteLine("✅ Database migration complete!");
    Environment.Exit(0);
}
catch (Exception ex)
{
    Console.WriteLine($"❌ Migration failed: {ex.Message}");
    Console.WriteLine(ex.StackTrace);
    Environment.Exit(1);
}

async Task WaitForPostgresAsync(string connString, int maxRetries = 30)
{
    for (int i = 0; i < maxRetries; i++)
    {
        try
        {
            await using var conn = new NpgsqlConnection(connString);
            await conn.OpenAsync();
            return;
        }
        catch
        {
            Console.WriteLine($"   Attempt {i + 1}/{maxRetries} - PostgreSQL not ready, waiting...");
            await Task.Delay(2000);
        }
    }
    throw new TimeoutException("PostgreSQL did not become ready within the timeout period");
}

async Task ApplyOrleansSchemaAsync(string connString)
{
    await using var conn = new NpgsqlConnection(connString);
    await conn.OpenAsync();

    // Create Orleans tables directly with idempotent DDL
    // Skipping the OrleansQuery INSERT statements - Orleans will handle these
    
    var ddlStatements = new[]
    {
        // OrleansQuery table
        """
        CREATE TABLE IF NOT EXISTS OrleansQuery
        (
            QueryKey varchar(64) NOT NULL,
            QueryText varchar(8000) NOT NULL,
            CONSTRAINT OrleansQuery_Key PRIMARY KEY(QueryKey)
        )
        """,
        
        // OrleansStorage table
        """
        CREATE TABLE IF NOT EXISTS OrleansStorage
        (
            grainidhash integer NOT NULL,
            grainidn0 bigint NOT NULL,
            grainidn1 bigint NOT NULL,
            graintypehash integer NOT NULL,
            graintypestring character varying(512) NOT NULL,
            grainidextensionstring character varying(512),
            serviceid character varying(150) NOT NULL,
            payloadbinary bytea,
            modifiedon timestamp without time zone NOT NULL,
            version integer
        )
        """,
        
        // Index for OrleansStorage
        """
        CREATE INDEX IF NOT EXISTS ix_orleansstorage
            ON orleansstorage USING btree (grainidhash, graintypehash)
        """,
        
        // WriteToStorage function
        """
        CREATE OR REPLACE FUNCTION writetostorage(
            _grainidhash integer,
            _grainidn0 bigint,
            _grainidn1 bigint,
            _graintypehash integer,
            _graintypestring character varying,
            _grainidextensionstring character varying,
            _serviceid character varying,
            _grainstateversion integer,
            _payloadbinary bytea)
            RETURNS TABLE(newgrainstateversion integer)
            LANGUAGE 'plpgsql'
        AS $function$
            DECLARE
             _newGrainStateVersion integer := _GrainStateVersion;
             RowCountVar integer := 0;

            BEGIN
            IF _GrainStateVersion IS NOT NULL
            THEN
                UPDATE OrleansStorage
                SET PayloadBinary = _PayloadBinary, ModifiedOn = (now() at time zone 'utc'), Version = Version + 1
                WHERE GrainIdHash = _GrainIdHash AND _GrainIdHash IS NOT NULL
                    AND GrainTypeHash = _GrainTypeHash AND _GrainTypeHash IS NOT NULL
                    AND GrainIdN0 = _GrainIdN0 AND _GrainIdN0 IS NOT NULL
                    AND GrainIdN1 = _GrainIdN1 AND _GrainIdN1 IS NOT NULL
                    AND GrainTypeString = _GrainTypeString AND _GrainTypeString IS NOT NULL
                    AND ((_GrainIdExtensionString IS NOT NULL AND GrainIdExtensionString IS NOT NULL AND GrainIdExtensionString = _GrainIdExtensionString) OR _GrainIdExtensionString IS NULL AND GrainIdExtensionString IS NULL)
                    AND ServiceId = _ServiceId AND _ServiceId IS NOT NULL
                    AND Version IS NOT NULL AND Version = _GrainStateVersion AND _GrainStateVersion IS NOT NULL;

                GET DIAGNOSTICS RowCountVar = ROW_COUNT;
                IF RowCountVar > 0 THEN _newGrainStateVersion := _GrainStateVersion + 1; END IF;
            END IF;

            IF _GrainStateVersion IS NULL
            THEN
                INSERT INTO OrleansStorage (GrainIdHash, GrainIdN0, GrainIdN1, GrainTypeHash, GrainTypeString, GrainIdExtensionString, ServiceId, PayloadBinary, ModifiedOn, Version)
                SELECT _GrainIdHash, _GrainIdN0, _GrainIdN1, _GrainTypeHash, _GrainTypeString, _GrainIdExtensionString, _ServiceId, _PayloadBinary, (now() at time zone 'utc'), 1
                WHERE NOT EXISTS (
                    SELECT 1 FROM OrleansStorage
                    WHERE GrainIdHash = _GrainIdHash AND _GrainIdHash IS NOT NULL
                        AND GrainTypeHash = _GrainTypeHash AND _GrainTypeHash IS NOT NULL
                        AND GrainIdN0 = _GrainIdN0 AND _GrainIdN0 IS NOT NULL
                        AND GrainIdN1 = _GrainIdN1 AND _GrainIdN1 IS NOT NULL
                        AND GrainTypeString = _GrainTypeString AND _GrainTypeString IS NOT NULL
                        AND ((_GrainIdExtensionString IS NOT NULL AND GrainIdExtensionString IS NOT NULL AND GrainIdExtensionString = _GrainIdExtensionString) OR _GrainIdExtensionString IS NULL AND GrainIdExtensionString IS NULL)
                        AND ServiceId = _ServiceId AND _ServiceId IS NOT NULL
                );

                GET DIAGNOSTICS RowCountVar = ROW_COUNT;
                IF RowCountVar > 0 THEN _newGrainStateVersion := 1; END IF;
            END IF;

            RETURN QUERY SELECT _newGrainStateVersion AS NewGrainStateVersion;
        END
        $function$
        """,
        
        // OrleansRemindersTable
        """
        CREATE TABLE IF NOT EXISTS OrleansRemindersTable
        (
            ServiceId varchar(150) NOT NULL,
            GrainId varchar(150) NOT NULL,
            ReminderName varchar(150) NOT NULL,
            StartTime timestamptz(3) NOT NULL,
            Period bigint NOT NULL,
            GrainHash integer NOT NULL,
            Version integer NOT NULL,
            CONSTRAINT PK_RemindersTable_ServiceId_GrainId_ReminderName PRIMARY KEY(ServiceId, GrainId, ReminderName)
        )
        """
    };

    foreach (var ddl in ddlStatements)
    {
        try
        {
            await using var cmd = new NpgsqlCommand(ddl, conn);
            await cmd.ExecuteNonQueryAsync();
        }
        catch (PostgresException ex) when (ex.SqlState == "42P07") // duplicate_table
        {
            // Table already exists - ignore
        }
        catch (PostgresException ex) when (ex.SqlState == "42710") // duplicate_object (for functions)
        {
            // Function already exists - ignore
        }
    }

    // Insert Orleans query registrations (these are required for Orleans to work)
    await InsertOrleansQueriesAsync(conn);
}

async Task InsertOrleansQueriesAsync(NpgsqlConnection conn)
{
    var queries = new Dictionary<string, string>
    {
        ["WriteToStorageKey"] = "select * from WriteToStorage(@GrainIdHash, @GrainIdN0, @GrainIdN1, @GrainTypeHash, @GrainTypeString, @GrainIdExtensionString, @ServiceId, @GrainStateVersion, @PayloadBinary);",
        ["ReadFromStorageKey"] = """
            SELECT PayloadBinary, (now() at time zone 'utc'), Version
            FROM OrleansStorage
            WHERE GrainIdHash = @GrainIdHash AND GrainTypeHash = @GrainTypeHash AND @GrainTypeHash IS NOT NULL
                AND GrainIdN0 = @GrainIdN0 AND @GrainIdN0 IS NOT NULL
                AND GrainIdN1 = @GrainIdN1 AND @GrainIdN1 IS NOT NULL
                AND GrainTypeString = @GrainTypeString AND GrainTypeString IS NOT NULL
                AND ((@GrainIdExtensionString IS NOT NULL AND GrainIdExtensionString IS NOT NULL AND GrainIdExtensionString = @GrainIdExtensionString) OR @GrainIdExtensionString IS NULL AND GrainIdExtensionString IS NULL)
                AND ServiceId = @ServiceId AND @ServiceId IS NOT NULL
            """,
        ["ClearStorageKey"] = """
            UPDATE OrleansStorage SET PayloadBinary = NULL, Version = Version + 1
            WHERE GrainIdHash = @GrainIdHash AND @GrainIdHash IS NOT NULL
                AND GrainTypeHash = @GrainTypeHash AND @GrainTypeHash IS NOT NULL
                AND GrainIdN0 = @GrainIdN0 AND @GrainIdN0 IS NOT NULL
                AND GrainIdN1 = @GrainIdN1 AND @GrainIdN1 IS NOT NULL
                AND GrainTypeString = @GrainTypeString AND @GrainTypeString IS NOT NULL
                AND ((@GrainIdExtensionString IS NOT NULL AND GrainIdExtensionString IS NOT NULL AND GrainIdExtensionString = @GrainIdExtensionString) OR @GrainIdExtensionString IS NULL AND GrainIdExtensionString IS NULL)
                AND ServiceId = @ServiceId AND @ServiceId IS NOT NULL
                AND Version IS NOT NULL AND Version = @GrainStateVersion AND @GrainStateVersion IS NOT NULL
            Returning Version as NewGrainStateVersion
            """,
        ["UpsertReminderRowKey"] = """
            INSERT INTO OrleansRemindersTable (ServiceId, GrainId, ReminderName, StartTime, Period, GrainHash, Version)
            VALUES (@ServiceId, @GrainId, @ReminderName, @StartTime, @Period, @GrainHash, 0)
            ON CONFLICT (ServiceId, GrainId, ReminderName)
            DO UPDATE SET StartTime = excluded.StartTime, Period = excluded.Period, GrainHash = excluded.GrainHash, Version = OrleansRemindersTable.Version + 1
            RETURNING OrleansRemindersTable.Version AS version
            """,
        ["ReadReminderRowsKey"] = "SELECT GrainId, ReminderName, StartTime, Period, Version FROM OrleansRemindersTable WHERE ServiceId = @ServiceId AND GrainId = @GrainId;",
        ["ReadReminderRowKey"] = "SELECT GrainId, ReminderName, StartTime, Period, Version FROM OrleansRemindersTable WHERE ServiceId = @ServiceId AND GrainId = @GrainId AND ReminderName = @ReminderName;",
        ["ReadRangeRows1Key"] = "SELECT GrainId, ReminderName, StartTime, Period, Version FROM OrleansRemindersTable WHERE ServiceId = @ServiceId AND GrainHash > @BeginHash AND GrainHash <= @EndHash;",
        ["ReadRangeRows2Key"] = "SELECT GrainId, ReminderName, StartTime, Period, Version FROM OrleansRemindersTable WHERE ServiceId = @ServiceId AND ((GrainHash > @BeginHash) OR (GrainHash <= @EndHash));",
        ["DeleteReminderRowKey"] = "DELETE FROM OrleansRemindersTable WHERE ServiceId = @ServiceId AND GrainId = @GrainId AND ReminderName = @ReminderName AND Version = @Version; SELECT 1;",
        ["DeleteReminderRowsKey"] = "DELETE FROM OrleansRemindersTable WHERE ServiceId = @ServiceId;"
    };

    foreach (var (key, queryText) in queries)
    {
        try
        {
            await using var cmd = new NpgsqlCommand(
                "INSERT INTO OrleansQuery(QueryKey, QueryText) VALUES (@key, @text) ON CONFLICT (QueryKey) DO NOTHING",
                conn);
            cmd.Parameters.AddWithValue("key", key);
            cmd.Parameters.AddWithValue("text", queryText);
            await cmd.ExecuteNonQueryAsync();
        }
        catch (PostgresException ex) when (ex.SqlState == "23505") // unique_violation
        {
            // Already exists - ignore
        }
    }
}

async Task ApplyAdminMigrationsAsync(string connString)
{
    var optionsBuilder = new DbContextOptionsBuilder<AdminDbContext>();
    optionsBuilder.UseNpgsql(connString);
    
    await using var context = new AdminDbContext(optionsBuilder.Options);
    await context.Database.MigrateAsync();
}
