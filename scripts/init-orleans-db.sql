-- Orleans Schema for CockroachDB (PostgreSQL-compatible)

-- =========================================================================================
-- OrleansQuery Table (Required for ADO.NET Providers)
-- =========================================================================================
CREATE TABLE IF NOT EXISTS OrleansQuery
(
    QueryKey VARCHAR(64) NOT NULL,
    QueryText VARCHAR(8000) NOT NULL,

    CONSTRAINT PK_OrleansQuery PRIMARY KEY(QueryKey)
);

-- =========================================================================================
-- OrleansStorage Table (Persistence)
-- =========================================================================================
DROP TABLE IF EXISTS OrleansStorage;

CREATE TABLE OrleansStorage
(
    GrainIdHash INTEGER NOT NULL,
    GrainIdN0 BIGINT NOT NULL,
    GrainIdN1 BIGINT NOT NULL,
    GrainTypeHash INTEGER NOT NULL,
    GrainTypeString VARCHAR(512) NOT NULL,
    GrainIdExtensionString VARCHAR(512) NOT NULL DEFAULT '',
    ServiceId VARCHAR(150) NOT NULL,
    PayloadBinary BYTEA,
    ModifiedOn TIMESTAMP WITHOUT TIME ZONE NOT NULL,
    Version INTEGER,

    CONSTRAINT PK_OrleansStorage PRIMARY KEY(GrainIdHash, GrainTypeHash, GrainIdN0, GrainIdN1, GrainIdExtensionString, ServiceId)
);

CREATE INDEX IF NOT EXISTS IX_OrleansStorage
    ON OrleansStorage(GrainIdHash, GrainTypeHash);

-- =========================================================================================
-- OrleansMembership Table (Clustering - if needed)
-- =========================================================================================
CREATE TABLE IF NOT EXISTS OrleansMembershipTable
(
    DeploymentId VARCHAR(150) NOT NULL,
    Address VARCHAR(45) NOT NULL,
    Port INT NOT NULL,
    Generation INT NOT NULL,
    SiloName VARCHAR(150) NOT NULL,
    HostName VARCHAR(150) NOT NULL,
    Status INT NOT NULL,
    ProxyPort INT,
    SuspectTimes VARCHAR(8000),
    StartTime TIMESTAMP WITHOUT TIME ZONE NOT NULL,
    IAmAliveTime TIMESTAMP WITHOUT TIME ZONE NOT NULL,

    CONSTRAINT PK_OrleansMembershipTable PRIMARY KEY (DeploymentId, Address, Port, Generation)
);

CREATE TABLE IF NOT EXISTS OrleansMembershipVersionTable
(
    DeploymentId VARCHAR(150) NOT NULL,
    Timestamp TIMESTAMP WITHOUT TIME ZONE NOT NULL DEFAULT now(),
    Version INT NOT NULL DEFAULT 0,

    CONSTRAINT PK_OrleansMembershipVersionTable PRIMARY KEY (DeploymentId)
);

-- =========================================================================================
-- INSERT Default Queries (Optimized for CockroachDB & Strict Schema)
-- =========================================================================================

INSERT INTO OrleansQuery(QueryKey, QueryText)
VALUES
(
    'WriteToStorageKey',
    'INSERT INTO OrleansStorage
    (
        GrainIdHash,
        GrainIdN0,
        GrainIdN1,
        GrainTypeHash,
        GrainTypeString,
        GrainIdExtensionString,
        ServiceId,
        PayloadBinary,
        ModifiedOn,
        Version
    )
    VALUES
    (
        @GrainIdHash,
        @GrainIdN0,
        @GrainIdN1,
        @GrainTypeHash,
        @GrainTypeString,
        COALESCE(@GrainIdExtensionString, ''''),
        @ServiceId,
        @PayloadBinary,
        now(),
        CASE WHEN @GrainStateVersion IS NOT NULL THEN @GrainStateVersion + 1 ELSE 1 END
    )
    ON CONFLICT (GrainIdHash, GrainTypeHash, GrainIdN0, GrainIdN1, GrainIdExtensionString, ServiceId)
    DO UPDATE SET
        PayloadBinary = EXCLUDED.PayloadBinary,
        ModifiedOn = EXCLUDED.ModifiedOn,
        Version = EXCLUDED.Version
    WHERE
        OrleansStorage.Version = @GrainStateVersion OR (@GrainStateVersion IS NULL AND OrleansStorage.Version IS NULL)
    RETURNING Version as newGrainStateVersion;'
),
(
    'ReadFromStorageKey',
    'SELECT PayloadBinary, ModifiedOn, Version 
     FROM OrleansStorage 
     WHERE GrainIdHash = @GrainIdHash AND GrainTypeHash = @GrainTypeHash AND GrainIdN0 = @GrainIdN0 AND GrainIdN1 = @GrainIdN1 AND GrainTypeString = @GrainTypeString AND GrainIdExtensionString = COALESCE(@GrainIdExtensionString, '''') AND ServiceId = @ServiceId'
),
(
    'ClearStorageKey',
    'DELETE FROM OrleansStorage 
     WHERE GrainIdHash = @GrainIdHash AND GrainTypeHash = @GrainTypeHash AND GrainIdN0 = @GrainIdN0 AND GrainIdN1 = @GrainIdN1 AND GrainTypeString = @GrainTypeString AND GrainIdExtensionString = COALESCE(@GrainIdExtensionString, '''') AND ServiceId = @ServiceId AND Version IS NOT NULL AND Version = @GrainStateVersion'
)
ON CONFLICT (QueryKey) 
DO UPDATE SET QueryText = EXCLUDED.QueryText;

SELECT 'Orleans schema initialized successfully' AS message;
