-- Orleans CockroachDB Schema
-- Adapted from PostgreSQL schema for CockroachDB compatibility
-- Source: https://github.com/dotnet/orleans/tree/main/src/AdoNet

-- =============================================================================
-- DATABASE: Create and connect to titan database
-- =============================================================================
-- Note: CockroachDB requires database creation via separate connection
-- Run: CREATE DATABASE IF NOT EXISTS titan;
-- Then connect to titan database before running the rest of this script

-- =============================================================================
-- MAIN: OrleansQuery Table (Required)
-- =============================================================================
CREATE TABLE IF NOT EXISTS OrleansQuery
(
    QueryKey varchar(64) NOT NULL,
    QueryText varchar(8000) NOT NULL,

    CONSTRAINT OrleansQuery_Key PRIMARY KEY(QueryKey)
);

-- =============================================================================
-- CLUSTERING: Membership Tables
-- =============================================================================
CREATE TABLE IF NOT EXISTS OrleansMembershipVersionTable
(
    DeploymentId varchar(150) NOT NULL,
    Timestamp timestamptz NOT NULL DEFAULT now(),
    Version integer NOT NULL DEFAULT 0,

    CONSTRAINT PK_OrleansMembershipVersionTable_DeploymentId PRIMARY KEY(DeploymentId)
);

CREATE TABLE IF NOT EXISTS OrleansMembershipTable
(
    DeploymentId varchar(150) NOT NULL,
    Address varchar(45) NOT NULL,
    Port integer NOT NULL,
    Generation integer NOT NULL,
    SiloName varchar(150) NOT NULL,
    HostName varchar(150) NOT NULL,
    Status integer NOT NULL,
    ProxyPort integer NULL,
    SuspectTimes varchar(8000) NULL,
    StartTime timestamptz NOT NULL,
    IAmAliveTime timestamptz NOT NULL,

    CONSTRAINT PK_MembershipTable_DeploymentId PRIMARY KEY(DeploymentId, Address, Port, Generation),
    CONSTRAINT FK_MembershipTable_MembershipVersionTable_DeploymentId FOREIGN KEY (DeploymentId) REFERENCES OrleansMembershipVersionTable (DeploymentId)
);

-- =============================================================================
-- CLUSTERING: Functions (Rewritten without GET DIAGNOSTICS)
-- =============================================================================
CREATE OR REPLACE FUNCTION update_i_am_alive_time(
    deployment_id STRING,
    address_arg STRING,
    port_arg INT,
    generation_arg INT,
    i_am_alive_time TIMESTAMPTZ)
RETURNS INT AS
$$
    UPDATE OrleansMembershipTable
    SET IAmAliveTime = i_am_alive_time
    WHERE DeploymentId = deployment_id AND deployment_id IS NOT NULL
        AND Address = address_arg AND address_arg IS NOT NULL
        AND Port = port_arg AND port_arg IS NOT NULL
        AND Generation = generation_arg AND generation_arg IS NOT NULL;
    
    SELECT 0;
$$ LANGUAGE SQL;

CREATE OR REPLACE FUNCTION insert_membership_version(
    DeploymentIdArg STRING
)
RETURNS INT AS
$$
    INSERT INTO OrleansMembershipVersionTable (DeploymentId)
    VALUES (DeploymentIdArg)
    ON CONFLICT (DeploymentId) DO NOTHING;

    SELECT CASE WHEN EXISTS (
        SELECT 1 FROM OrleansMembershipVersionTable WHERE DeploymentId = DeploymentIdArg
    ) THEN 1 ELSE 0 END;
$$ LANGUAGE SQL;

CREATE OR REPLACE FUNCTION insert_membership(
    DeploymentIdArg STRING,
    AddressArg      STRING,
    PortArg         INT,
    GenerationArg   INT,
    SiloNameArg     STRING,
    HostNameArg     STRING,
    StatusArg       INT,
    ProxyPortArg    INT,
    StartTimeArg    TIMESTAMPTZ,
    IAmAliveTimeArg TIMESTAMPTZ,
    VersionArg      INT)
RETURNS INT AS
$$
    WITH inserted AS (
        INSERT INTO OrleansMembershipTable
        (DeploymentId, Address, Port, Generation, SiloName, HostName, Status, ProxyPort, StartTime, IAmAliveTime)
        VALUES (DeploymentIdArg, AddressArg, PortArg, GenerationArg, SiloNameArg, HostNameArg, StatusArg, ProxyPortArg, StartTimeArg, IAmAliveTimeArg)
        ON CONFLICT (DeploymentId, Address, Port, Generation) DO NOTHING
        RETURNING 1
    ),
    updated AS (
        UPDATE OrleansMembershipVersionTable
        SET Timestamp = now(), Version = Version + 1
        WHERE DeploymentId = DeploymentIdArg AND DeploymentIdArg IS NOT NULL
            AND Version = VersionArg AND VersionArg IS NOT NULL
            AND EXISTS (SELECT 1 FROM inserted)
        RETURNING 1
    )
    SELECT COALESCE((SELECT COUNT(*) FROM updated), 0)::INT;
$$ LANGUAGE SQL;

CREATE OR REPLACE FUNCTION update_membership(
    DeploymentIdArg STRING,
    AddressArg      STRING,
    PortArg         INT,
    GenerationArg   INT,
    StatusArg       INT,
    SuspectTimesArg STRING,
    IAmAliveTimeArg TIMESTAMPTZ,
    VersionArg      INT
)
RETURNS INT AS
$$
    WITH version_updated AS (
        UPDATE OrleansMembershipVersionTable
        SET Timestamp = now(), Version = Version + 1
        WHERE DeploymentId = DeploymentIdArg AND DeploymentIdArg IS NOT NULL
            AND Version = VersionArg AND VersionArg IS NOT NULL
        RETURNING 1
    ),
    membership_updated AS (
        UPDATE OrleansMembershipTable
        SET Status = StatusArg, SuspectTimes = SuspectTimesArg, IAmAliveTime = IAmAliveTimeArg
        WHERE DeploymentId = DeploymentIdArg AND DeploymentIdArg IS NOT NULL
            AND Address = AddressArg AND AddressArg IS NOT NULL
            AND Port = PortArg AND PortArg IS NOT NULL
            AND Generation = GenerationArg AND GenerationArg IS NOT NULL
            AND EXISTS (SELECT 1 FROM version_updated)
        RETURNING 1
    )
    SELECT COALESCE((SELECT COUNT(*) FROM membership_updated), 0)::INT;
$$ LANGUAGE SQL;

-- =============================================================================
-- PERSISTENCE: OrleansStorage Table
-- =============================================================================
CREATE TABLE IF NOT EXISTS OrleansStorage
(
    grainidhash integer NOT NULL,
    grainidn0 bigint NOT NULL,
    grainidn1 bigint NOT NULL,
    graintypehash integer NOT NULL,
    graintypestring varchar(512) NOT NULL,
    grainidextensionstring varchar(512) NOT NULL DEFAULT '',
    serviceid varchar(150) NOT NULL,
    payloadbinary bytes,
    modifiedon timestamp NOT NULL,
    version integer,
    
    PRIMARY KEY (grainidhash, graintypehash, grainidn0, grainidn1, graintypestring, serviceid, grainidextensionstring)
);

CREATE INDEX IF NOT EXISTS ix_orleansstorage
    ON orleansstorage (grainidhash, graintypehash);

-- =============================================================================
-- PERSISTENCE: WriteToStorage Function (Rewritten without GET DIAGNOSTICS)
-- =============================================================================
CREATE OR REPLACE FUNCTION writetostorage(
    _grainidhash INT,
    _grainidn0 BIGINT,
    _grainidn1 BIGINT,
    _graintypehash INT,
    _graintypestring STRING,
    _grainidextensionstring STRING,
    _serviceid STRING,
    _grainstateversion INT,
    _payloadbinary BYTES)
RETURNS INT AS
$
    WITH normalized_ext AS (
        SELECT COALESCE(_GrainIdExtensionString, '') AS ext_value
    ),
    existing AS (
        SELECT Version
        FROM OrleansStorage, normalized_ext
        WHERE GrainIdHash = _GrainIdHash AND _GrainIdHash IS NOT NULL
            AND GrainTypeHash = _GrainTypeHash AND _GrainTypeHash IS NOT NULL
            AND GrainIdN0 = _GrainIdN0 AND _GrainIdN0 IS NOT NULL
            AND GrainIdN1 = _GrainIdN1 AND _GrainIdN1 IS NOT NULL
            AND GrainTypeString = _GrainTypeString AND _GrainTypeString IS NOT NULL
            AND GrainIdExtensionString = ext_value
            AND ServiceId = _ServiceId AND _ServiceId IS NOT NULL
    ),
    updated AS (
        UPDATE OrleansStorage
        SET PayloadBinary = _PayloadBinary,
            ModifiedOn = now(),
            Version = Version + 1
        FROM normalized_ext
        WHERE GrainIdHash = _GrainIdHash AND _GrainIdHash IS NOT NULL
            AND GrainTypeHash = _GrainTypeHash AND _GrainTypeHash IS NOT NULL
            AND GrainIdN0 = _GrainIdN0 AND _GrainIdN0 IS NOT NULL
            AND GrainIdN1 = _GrainIdN1 AND _GrainIdN1 IS NOT NULL
            AND GrainTypeString = _GrainTypeString AND _GrainTypeString IS NOT NULL
            AND GrainIdExtensionString = normalized_ext.ext_value
            AND ServiceId = _ServiceId AND _ServiceId IS NOT NULL
            AND Version IS NOT NULL AND Version = _GrainStateVersion AND _GrainStateVersion IS NOT NULL
        RETURNING OrleansStorage.Version
    ),
    inserted AS (
        INSERT INTO OrleansStorage
        (GrainIdHash, GrainIdN0, GrainIdN1, GrainTypeHash, GrainTypeString,
         GrainIdExtensionString, ServiceId, PayloadBinary, ModifiedOn, Version)
        SELECT _GrainIdHash, _GrainIdN0, _GrainIdN1, _GrainTypeHash, _GrainTypeString,
               COALESCE(_GrainIdExtensionString, ''), _ServiceId, _PayloadBinary, now(), 1
        WHERE _GrainStateVersion IS NULL
            AND NOT EXISTS (SELECT 1 FROM existing)
        ON CONFLICT (GrainIdHash, GrainTypeHash, GrainIdN0, GrainIdN1, GrainTypeString, ServiceId, GrainIdExtensionString) 
        DO NOTHING
        RETURNING Version
    )
    SELECT COALESCE(
        (SELECT Version FROM updated),
        (SELECT Version FROM inserted),
        (SELECT Version FROM existing)
    )::INT;
$ LANGUAGE SQL;

-- =============================================================================
-- QUERIES: Clustering Queries
-- =============================================================================
INSERT INTO OrleansQuery(QueryKey, QueryText) VALUES
('UpdateIAmAlivetimeKey', 'SELECT update_i_am_alive_time($1, $2, $3, $4, $5);')
ON CONFLICT (QueryKey) DO UPDATE SET QueryText = EXCLUDED.QueryText;

INSERT INTO OrleansQuery(QueryKey, QueryText) VALUES
('InsertMembershipVersionKey', 'SELECT insert_membership_version($1);')
ON CONFLICT (QueryKey) DO UPDATE SET QueryText = EXCLUDED.QueryText;

INSERT INTO OrleansQuery(QueryKey, QueryText) VALUES
('InsertMembershipKey', 'SELECT insert_membership($1, $2, $3, $4, $5, $6, $7, $8, $9, $10, $11);')
ON CONFLICT (QueryKey) DO UPDATE SET QueryText = EXCLUDED.QueryText;

INSERT INTO OrleansQuery(QueryKey, QueryText) VALUES
('UpdateMembershipKey', 'SELECT update_membership($1, $2, $3, $4, $5, $6, $7, $8);')
ON CONFLICT (QueryKey) DO UPDATE SET QueryText = EXCLUDED.QueryText;

INSERT INTO OrleansQuery(QueryKey, QueryText) VALUES
('MembershipReadRowKey', '
    SELECT v.DeploymentId, m.Address, m.Port, m.Generation, m.SiloName, m.HostName,
           m.Status, m.ProxyPort, m.SuspectTimes, m.StartTime, m.IAmAliveTime, v.Version
    FROM OrleansMembershipVersionTable v
    LEFT OUTER JOIN OrleansMembershipTable m ON v.DeploymentId = m.DeploymentId
        AND Address = $2 AND $2 IS NOT NULL
        AND Port = $3 AND $3 IS NOT NULL
        AND Generation = $4 AND $4 IS NOT NULL
    WHERE v.DeploymentId = $1 AND $1 IS NOT NULL;')
ON CONFLICT (QueryKey) DO UPDATE SET QueryText = EXCLUDED.QueryText;

INSERT INTO OrleansQuery(QueryKey, QueryText) VALUES
('MembershipReadAllKey', '
    SELECT v.DeploymentId, m.Address, m.Port, m.Generation, m.SiloName, m.HostName,
           m.Status, m.ProxyPort, m.SuspectTimes, m.StartTime, m.IAmAliveTime, v.Version
    FROM OrleansMembershipVersionTable v
    LEFT OUTER JOIN OrleansMembershipTable m ON v.DeploymentId = m.DeploymentId
    WHERE v.DeploymentId = $1 AND $1 IS NOT NULL;')
ON CONFLICT (QueryKey) DO UPDATE SET QueryText = EXCLUDED.QueryText;

INSERT INTO OrleansQuery(QueryKey, QueryText) VALUES
('DeleteMembershipTableEntriesKey', '
    DELETE FROM OrleansMembershipTable WHERE DeploymentId = $1 AND $1 IS NOT NULL;
    DELETE FROM OrleansMembershipVersionTable WHERE DeploymentId = $1 AND $1 IS NOT NULL;')
ON CONFLICT (QueryKey) DO UPDATE SET QueryText = EXCLUDED.QueryText;

INSERT INTO OrleansQuery(QueryKey, QueryText) VALUES
('GatewaysQueryKey', '
    SELECT Address, ProxyPort, Generation FROM OrleansMembershipTable
    WHERE DeploymentId = $1 AND $1 IS NOT NULL
        AND Status = $2 AND $2 IS NOT NULL AND ProxyPort > 0;')
ON CONFLICT (QueryKey) DO UPDATE SET QueryText = EXCLUDED.QueryText;

INSERT INTO OrleansQuery(QueryKey, QueryText) VALUES
('CleanupDefunctSiloEntriesKey', '
    DELETE FROM OrleansMembershipTable
    WHERE DeploymentId = $1
        AND $1 IS NOT NULL
        AND IAmAliveTime < $2
        AND Status != 3;')
ON CONFLICT (QueryKey) DO UPDATE SET QueryText = EXCLUDED.QueryText;

-- =============================================================================
-- QUERIES: Persistence Queries
-- =============================================================================
INSERT INTO OrleansQuery(QueryKey, QueryText) VALUES
('WriteToStorageKey', 'SELECT writetostorage($1, $2, $3, $4, $5, $6, $7, $8, $9);')
ON CONFLICT (QueryKey) DO UPDATE SET QueryText = EXCLUDED.QueryText;

INSERT INTO OrleansQuery(QueryKey, QueryText) VALUES
('ReadFromStorageKey', '
    SELECT PayloadBinary, now(), Version
    FROM OrleansStorage
    WHERE GrainIdHash = $1
        AND GrainTypeHash = $2 AND $2 IS NOT NULL
        AND GrainIdN0 = $3 AND $3 IS NOT NULL
        AND GrainIdN1 = $4 AND $4 IS NOT NULL
        AND GrainTypeString = $5 AND GrainTypeString IS NOT NULL
        AND GrainIdExtensionString = COALESCE($6, '''')
        AND ServiceId = $7 AND $7 IS NOT NULL;')
ON CONFLICT (QueryKey) DO UPDATE SET QueryText = EXCLUDED.QueryText;

INSERT INTO OrleansQuery(QueryKey, QueryText) VALUES
('ClearStorageKey', '
    UPDATE OrleansStorage
    SET PayloadBinary = NULL, Version = Version + 1
    WHERE GrainIdHash = $1 AND $1 IS NOT NULL
        AND GrainTypeHash = $2 AND $2 IS NOT NULL
        AND GrainIdN0 = $3 AND $3 IS NOT NULL
        AND GrainIdN1 = $4 AND $4 IS NOT NULL
        AND GrainTypeString = $5 AND $5 IS NOT NULL
        AND GrainIdExtensionString = COALESCE($6, '''')
        AND ServiceId = $7 AND $7 IS NOT NULL
        AND Version IS NOT NULL AND Version = $8 AND $8 IS NOT NULL
    RETURNING Version as NewGrainStateVersion;')
ON CONFLICT (QueryKey) DO UPDATE SET QueryText = EXCLUDED.QueryText;

SELECT 'Orleans CockroachDB schema initialized successfully' AS message;