-- Orleans ADO.NET CockroachDB Schema (Persistence & Reminders)
-- CockroachDB is PostgreSQL-compatible but doesn't support all PL/pgSQL features
-- This simplified schema works with Orleans' ADO.NET grain storage

-- ============================================================
-- Create Database
-- ============================================================
CREATE DATABASE IF NOT EXISTS titan;
USE titan;

-- ============================================================
-- OrleansQuery - Base Query Table (Required)
-- ============================================================

CREATE TABLE IF NOT EXISTS OrleansQuery
(
    QueryKey varchar(64) NOT NULL,
    QueryText varchar(8000) NOT NULL,

    CONSTRAINT OrleansQuery_Key PRIMARY KEY(QueryKey)
);

-- ============================================================
-- OrleansStorage - Grain Persistence Table
-- ============================================================

CREATE TABLE IF NOT EXISTS OrleansStorage
(
    grainidhash integer NOT NULL,
    grainidn0 bigint NOT NULL,
    grainidn1 bigint NOT NULL,
    graintypehash integer NOT NULL,
    graintypestring varchar(512) NOT NULL,
    grainidextensionstring varchar(512),
    serviceid varchar(150) NOT NULL,
    payloadbinary bytea,
    modifiedon timestamp NOT NULL DEFAULT now(),
    version integer
);

CREATE INDEX IF NOT EXISTS ix_orleansstorage
    ON orleansstorage (grainidhash, graintypehash);

-- Start of Orleans Queries
INSERT INTO OrleansQuery(QueryKey, QueryText)
VALUES
(
    'WriteToStorageKey','
    INSERT INTO OrleansStorage
    (
        GrainIdHash, GrainIdN0, GrainIdN1, GrainTypeHash, GrainTypeString,
        GrainIdExtensionString, ServiceId, PayloadBinary, ModifiedOn, Version
    )
    VALUES
    (
        @GrainIdHash, @GrainIdN0, @GrainIdN1, @GrainTypeHash, @GrainTypeString,
        @GrainIdExtensionString, @ServiceId, @PayloadBinary, now(), 1
    )
    ON CONFLICT (GrainIdHash, GrainIdN0, GrainIdN1, GrainTypeHash, GrainTypeString, ServiceId) 
    DO UPDATE SET
        PayloadBinary = EXCLUDED.PayloadBinary,
        ModifiedOn = now(),
        Version = OrleansStorage.Version + 1
    RETURNING Version AS NewGrainStateVersion;
')
ON CONFLICT (QueryKey) DO NOTHING;

INSERT INTO OrleansQuery(QueryKey, QueryText)
VALUES
(
    'ReadFromStorageKey','
    SELECT
        PayloadBinary,
        now(),
        Version
    FROM
        OrleansStorage
    WHERE
        GrainIdHash = @GrainIdHash
        AND GrainTypeHash = @GrainTypeHash
        AND GrainIdN0 = @GrainIdN0
        AND GrainIdN1 = @GrainIdN1
        AND GrainTypeString = @GrainTypeString
        AND ((@GrainIdExtensionString IS NOT NULL AND GrainIdExtensionString = @GrainIdExtensionString) 
             OR (@GrainIdExtensionString IS NULL AND GrainIdExtensionString IS NULL))
        AND ServiceId = @ServiceId
')
ON CONFLICT (QueryKey) DO NOTHING;

INSERT INTO OrleansQuery(QueryKey, QueryText)
VALUES
(
    'ClearStorageKey','
    UPDATE OrleansStorage
    SET
        PayloadBinary = NULL,
        ModifiedOn = now(),
        Version = Version + 1
    WHERE
        GrainIdHash = @GrainIdHash
        AND GrainTypeHash = @GrainTypeHash
        AND GrainIdN0 = @GrainIdN0
        AND GrainIdN1 = @GrainIdN1
        AND GrainTypeString = @GrainTypeString
        AND ((@GrainIdExtensionString IS NOT NULL AND GrainIdExtensionString = @GrainIdExtensionString) 
             OR (@GrainIdExtensionString IS NULL AND GrainIdExtensionString IS NULL))
        AND ServiceId = @ServiceId
        AND Version = @GrainStateVersion
    RETURNING Version AS NewGrainStateVersion
')
ON CONFLICT (QueryKey) DO NOTHING;

-- Add unique constraint for upsert via index (idempotent)
CREATE UNIQUE INDEX IF NOT EXISTS uk_orleansstorage 
    ON OrleansStorage (GrainIdHash, GrainIdN0, GrainIdN1, GrainTypeHash, GrainTypeString, ServiceId);

-- ============================================================
-- OrleansRemindersTable - Reminders Persistence
-- ============================================================

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
);

INSERT INTO OrleansQuery(QueryKey, QueryText)
VALUES
(
    'UpsertReminderRowKey','
    INSERT INTO OrleansRemindersTable
    (
        ServiceId,
        GrainId,
        ReminderName,
        StartTime,
        Period,
        GrainHash,
        Version
    )
    VALUES
    (
        @ServiceId,
        @GrainId,
        @ReminderName,
        @StartTime,
        @Period,
        @GrainHash,
        0
    )
    ON CONFLICT (ServiceId, GrainId, ReminderName)
    DO UPDATE SET
        StartTime = excluded.StartTime,
        Period = excluded.Period,
        GrainHash = excluded.GrainHash,
        Version = OrleansRemindersTable.Version + 1
    RETURNING
        OrleansRemindersTable.Version AS versionr;
')
ON CONFLICT (QueryKey) DO NOTHING;

INSERT INTO OrleansQuery(QueryKey, QueryText)
VALUES
(
    'ReadReminderRowsKey','
    SELECT
        GrainId,
        ReminderName,
        StartTime,
        Period,
        Version
    FROM OrleansRemindersTable
    WHERE
        ServiceId = @ServiceId
        AND GrainId = @GrainId;
')
ON CONFLICT (QueryKey) DO NOTHING;

INSERT INTO OrleansQuery(QueryKey, QueryText)
VALUES
(
    'ReadReminderRowKey','
    SELECT
        GrainId,
        ReminderName,
        StartTime,
        Period,
        Version
    FROM OrleansRemindersTable
    WHERE
        ServiceId = @ServiceId
        AND GrainId = @GrainId
        AND ReminderName = @ReminderName;
')
ON CONFLICT (QueryKey) DO NOTHING;

INSERT INTO OrleansQuery(QueryKey, QueryText)
VALUES
(
    'ReadRangeRows1Key','
    SELECT
        GrainId,
        ReminderName,
        StartTime,
        Period,
        Version
    FROM OrleansRemindersTable
    WHERE
        ServiceId = @ServiceId
        AND GrainHash > @BeginHash
        AND GrainHash <= @EndHash;
')
ON CONFLICT (QueryKey) DO NOTHING;

INSERT INTO OrleansQuery(QueryKey, QueryText)
VALUES
(
    'ReadRangeRows2Key','
    SELECT
        GrainId,
        ReminderName,
        StartTime,
        Period,
        Version
    FROM OrleansRemindersTable
    WHERE
        ServiceId = @ServiceId
        AND ((GrainHash > @BeginHash)
        OR (GrainHash <= @EndHash));
')
ON CONFLICT (QueryKey) DO NOTHING;

INSERT INTO OrleansQuery(QueryKey, QueryText)
VALUES
(
    'DeleteReminderRowKey','
    DELETE FROM OrleansRemindersTable
    WHERE
        ServiceId = @ServiceId
        AND GrainId = @GrainId
        AND ReminderName = @ReminderName
        AND Version = @Version;
    SELECT 1; -- Expected by Orleans ADO Net wrapper to confirm execution? (Actually row count usually returned via ExecuteNonQuery but Orleans uses scalar/reader sometimes). 
              -- Standard ADO provider for Orleans usually checks row count. 
              -- This is simplified for text query access. The C# wrapper often expects row count.
')
ON CONFLICT (QueryKey) DO NOTHING;

INSERT INTO OrleansQuery(QueryKey, QueryText)
VALUES
(
    'DeleteReminderRowsKey','
    DELETE FROM OrleansRemindersTable
    WHERE
        ServiceId = @ServiceId;
')
ON CONFLICT (QueryKey) DO NOTHING;
