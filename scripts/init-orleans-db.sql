-- Orleans PostgreSQL Schema
-- Combines official Orleans scripts: Main + Clustering + Persistence
-- Source: https://github.com/dotnet/orleans/tree/main/src/AdoNet

-- =============================================================================
-- DATABASE: Create and connect to titan database
-- =============================================================================
SELECT 'CREATE DATABASE titan' WHERE NOT EXISTS (SELECT FROM pg_database WHERE datname = 'titan')
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
    Timestamp timestamptz(3) NOT NULL DEFAULT now(),
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
    StartTime timestamptz(3) NOT NULL,
    IAmAliveTime timestamptz(3) NOT NULL,

    CONSTRAINT PK_MembershipTable_DeploymentId PRIMARY KEY(DeploymentId, Address, Port, Generation),
    CONSTRAINT FK_MembershipTable_MembershipVersionTable_DeploymentId FOREIGN KEY (DeploymentId) REFERENCES OrleansMembershipVersionTable (DeploymentId)
);

-- =============================================================================
-- CLUSTERING: Functions
-- =============================================================================
CREATE OR REPLACE FUNCTION update_i_am_alive_time(
    deployment_id OrleansMembershipTable.DeploymentId%TYPE,
    address_arg OrleansMembershipTable.Address%TYPE,
    port_arg OrleansMembershipTable.Port%TYPE,
    generation_arg OrleansMembershipTable.Generation%TYPE,
    i_am_alive_time OrleansMembershipTable.IAmAliveTime%TYPE)
  RETURNS void AS
$func$
BEGIN
    UPDATE OrleansMembershipTable as d
    SET
        IAmAliveTime = i_am_alive_time
    WHERE
        d.DeploymentId = deployment_id AND deployment_id IS NOT NULL
        AND d.Address = address_arg AND address_arg IS NOT NULL
        AND d.Port = port_arg AND port_arg IS NOT NULL
        AND d.Generation = generation_arg AND generation_arg IS NOT NULL;
END
$func$ LANGUAGE plpgsql;

CREATE OR REPLACE FUNCTION insert_membership_version(
    DeploymentIdArg OrleansMembershipTable.DeploymentId%TYPE
)
  RETURNS TABLE(row_count integer) AS
$func$
DECLARE
    RowCountVar int := 0;
BEGIN
    BEGIN
        INSERT INTO OrleansMembershipVersionTable (DeploymentId)
        SELECT DeploymentIdArg
        ON CONFLICT (DeploymentId) DO NOTHING;

        GET DIAGNOSTICS RowCountVar = ROW_COUNT;
        ASSERT RowCountVar <> 0, 'no rows affected, rollback';
        RETURN QUERY SELECT RowCountVar;
    EXCEPTION
    WHEN assert_failure THEN
        RETURN QUERY SELECT RowCountVar;
    END;
END
$func$ LANGUAGE plpgsql;

CREATE OR REPLACE FUNCTION insert_membership(
    DeploymentIdArg OrleansMembershipTable.DeploymentId%TYPE,
    AddressArg      OrleansMembershipTable.Address%TYPE,
    PortArg         OrleansMembershipTable.Port%TYPE,
    GenerationArg   OrleansMembershipTable.Generation%TYPE,
    SiloNameArg     OrleansMembershipTable.SiloName%TYPE,
    HostNameArg     OrleansMembershipTable.HostName%TYPE,
    StatusArg       OrleansMembershipTable.Status%TYPE,
    ProxyPortArg    OrleansMembershipTable.ProxyPort%TYPE,
    StartTimeArg    OrleansMembershipTable.StartTime%TYPE,
    IAmAliveTimeArg OrleansMembershipTable.IAmAliveTime%TYPE,
    VersionArg      OrleansMembershipVersionTable.Version%TYPE)
  RETURNS TABLE(row_count integer) AS
$func$
DECLARE
    RowCountVar int := 0;
BEGIN
    BEGIN
        INSERT INTO OrleansMembershipTable
        (
            DeploymentId, Address, Port, Generation, SiloName,
            HostName, Status, ProxyPort, StartTime, IAmAliveTime
        )
        SELECT
            DeploymentIdArg, AddressArg, PortArg, GenerationArg, SiloNameArg,
            HostNameArg, StatusArg, ProxyPortArg, StartTimeArg, IAmAliveTimeArg
        ON CONFLICT (DeploymentId, Address, Port, Generation) DO NOTHING;

        GET DIAGNOSTICS RowCountVar = ROW_COUNT;

        UPDATE OrleansMembershipVersionTable
        SET Timestamp = now(), Version = Version + 1
        WHERE DeploymentId = DeploymentIdArg AND DeploymentIdArg IS NOT NULL
            AND Version = VersionArg AND VersionArg IS NOT NULL
            AND RowCountVar > 0;

        GET DIAGNOSTICS RowCountVar = ROW_COUNT;
        ASSERT RowCountVar <> 0, 'no rows affected, rollback';
        RETURN QUERY SELECT RowCountVar;
    EXCEPTION
    WHEN assert_failure THEN
        RETURN QUERY SELECT RowCountVar;
    END;
END
$func$ LANGUAGE plpgsql;

CREATE OR REPLACE FUNCTION update_membership(
    DeploymentIdArg OrleansMembershipTable.DeploymentId%TYPE,
    AddressArg      OrleansMembershipTable.Address%TYPE,
    PortArg         OrleansMembershipTable.Port%TYPE,
    GenerationArg   OrleansMembershipTable.Generation%TYPE,
    StatusArg       OrleansMembershipTable.Status%TYPE,
    SuspectTimesArg OrleansMembershipTable.SuspectTimes%TYPE,
    IAmAliveTimeArg OrleansMembershipTable.IAmAliveTime%TYPE,
    VersionArg      OrleansMembershipVersionTable.Version%TYPE
  )
  RETURNS TABLE(row_count integer) AS
$func$
DECLARE
    RowCountVar int := 0;
BEGIN
    BEGIN
        UPDATE OrleansMembershipVersionTable
        SET Timestamp = now(), Version = Version + 1
        WHERE DeploymentId = DeploymentIdArg AND DeploymentIdArg IS NOT NULL
            AND Version = VersionArg AND VersionArg IS NOT NULL;

        GET DIAGNOSTICS RowCountVar = ROW_COUNT;

        UPDATE OrleansMembershipTable
        SET Status = StatusArg, SuspectTimes = SuspectTimesArg, IAmAliveTime = IAmAliveTimeArg
        WHERE DeploymentId = DeploymentIdArg AND DeploymentIdArg IS NOT NULL
            AND Address = AddressArg AND AddressArg IS NOT NULL
            AND Port = PortArg AND PortArg IS NOT NULL
            AND Generation = GenerationArg AND GenerationArg IS NOT NULL
            AND RowCountVar > 0;

        GET DIAGNOSTICS RowCountVar = ROW_COUNT;
        ASSERT RowCountVar <> 0, 'no rows affected, rollback';
        RETURN QUERY SELECT RowCountVar;
    EXCEPTION
    WHEN assert_failure THEN
        RETURN QUERY SELECT RowCountVar;
    END;
END
$func$ LANGUAGE plpgsql;

-- =============================================================================
-- PERSISTENCE: OrleansStorage Table
-- =============================================================================
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
);

CREATE INDEX IF NOT EXISTS ix_orleansstorage
    ON orleansstorage USING btree (grainidhash, graintypehash);

-- =============================================================================
-- PERSISTENCE: WriteToStorage Function
-- =============================================================================
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
    IF _GrainStateVersion IS NOT NULL THEN
        UPDATE OrleansStorage
        SET PayloadBinary = _PayloadBinary,
            ModifiedOn = (now() at time zone 'utc'),
            Version = Version + 1
        WHERE GrainIdHash = _GrainIdHash AND _GrainIdHash IS NOT NULL
            AND GrainTypeHash = _GrainTypeHash AND _GrainTypeHash IS NOT NULL
            AND GrainIdN0 = _GrainIdN0 AND _GrainIdN0 IS NOT NULL
            AND GrainIdN1 = _GrainIdN1 AND _GrainIdN1 IS NOT NULL
            AND GrainTypeString = _GrainTypeString AND _GrainTypeString IS NOT NULL
            AND ((_GrainIdExtensionString IS NOT NULL AND GrainIdExtensionString IS NOT NULL AND GrainIdExtensionString = _GrainIdExtensionString) OR _GrainIdExtensionString IS NULL AND GrainIdExtensionString IS NULL)
            AND ServiceId = _ServiceId AND _ServiceId IS NOT NULL
            AND Version IS NOT NULL AND Version = _GrainStateVersion AND _GrainStateVersion IS NOT NULL;

        GET DIAGNOSTICS RowCountVar = ROW_COUNT;
        IF RowCountVar > 0 THEN
            _newGrainStateVersion := _GrainStateVersion + 1;
        END IF;
    END IF;

    IF _GrainStateVersion IS NULL THEN
        INSERT INTO OrleansStorage
        (GrainIdHash, GrainIdN0, GrainIdN1, GrainTypeHash, GrainTypeString,
         GrainIdExtensionString, ServiceId, PayloadBinary, ModifiedOn, Version)
        SELECT _GrainIdHash, _GrainIdN0, _GrainIdN1, _GrainTypeHash, _GrainTypeString,
               _GrainIdExtensionString, _ServiceId, _PayloadBinary, (now() at time zone 'utc'), 1
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
        IF RowCountVar > 0 THEN
            _newGrainStateVersion := 1;
        END IF;
    END IF;

    RETURN QUERY SELECT _newGrainStateVersion AS NewGrainStateVersion;
END
$function$;

-- =============================================================================
-- QUERIES: Clustering Queries
-- =============================================================================
INSERT INTO OrleansQuery(QueryKey, QueryText) VALUES
('UpdateIAmAlivetimeKey', 'SELECT * from update_i_am_alive_time(@DeploymentId, @Address, @Port, @Generation, @IAmAliveTime);')
ON CONFLICT (QueryKey) DO UPDATE SET QueryText = EXCLUDED.QueryText;

INSERT INTO OrleansQuery(QueryKey, QueryText) VALUES
('InsertMembershipVersionKey', 'SELECT * FROM insert_membership_version(@DeploymentId);')
ON CONFLICT (QueryKey) DO UPDATE SET QueryText = EXCLUDED.QueryText;

INSERT INTO OrleansQuery(QueryKey, QueryText) VALUES
('InsertMembershipKey', 'SELECT * FROM insert_membership(@DeploymentId, @Address, @Port, @Generation, @SiloName, @HostName, @Status, @ProxyPort, @StartTime, @IAmAliveTime, @Version);')
ON CONFLICT (QueryKey) DO UPDATE SET QueryText = EXCLUDED.QueryText;

INSERT INTO OrleansQuery(QueryKey, QueryText) VALUES
('UpdateMembershipKey', 'SELECT * FROM update_membership(@DeploymentId, @Address, @Port, @Generation, @Status, @SuspectTimes, @IAmAliveTime, @Version);')
ON CONFLICT (QueryKey) DO UPDATE SET QueryText = EXCLUDED.QueryText;

INSERT INTO OrleansQuery(QueryKey, QueryText) VALUES
('MembershipReadRowKey', '
    SELECT v.DeploymentId, m.Address, m.Port, m.Generation, m.SiloName, m.HostName,
           m.Status, m.ProxyPort, m.SuspectTimes, m.StartTime, m.IAmAliveTime, v.Version
    FROM OrleansMembershipVersionTable v
    LEFT OUTER JOIN OrleansMembershipTable m ON v.DeploymentId = m.DeploymentId
        AND Address = @Address AND @Address IS NOT NULL
        AND Port = @Port AND @Port IS NOT NULL
        AND Generation = @Generation AND @Generation IS NOT NULL
    WHERE v.DeploymentId = @DeploymentId AND @DeploymentId IS NOT NULL;')
ON CONFLICT (QueryKey) DO UPDATE SET QueryText = EXCLUDED.QueryText;

INSERT INTO OrleansQuery(QueryKey, QueryText) VALUES
('MembershipReadAllKey', '
    SELECT v.DeploymentId, m.Address, m.Port, m.Generation, m.SiloName, m.HostName,
           m.Status, m.ProxyPort, m.SuspectTimes, m.StartTime, m.IAmAliveTime, v.Version
    FROM OrleansMembershipVersionTable v
    LEFT OUTER JOIN OrleansMembershipTable m ON v.DeploymentId = m.DeploymentId
    WHERE v.DeploymentId = @DeploymentId AND @DeploymentId IS NOT NULL;')
ON CONFLICT (QueryKey) DO UPDATE SET QueryText = EXCLUDED.QueryText;

INSERT INTO OrleansQuery(QueryKey, QueryText) VALUES
('DeleteMembershipTableEntriesKey', '
    DELETE FROM OrleansMembershipTable WHERE DeploymentId = @DeploymentId AND @DeploymentId IS NOT NULL;
    DELETE FROM OrleansMembershipVersionTable WHERE DeploymentId = @DeploymentId AND @DeploymentId IS NOT NULL;')
ON CONFLICT (QueryKey) DO UPDATE SET QueryText = EXCLUDED.QueryText;

INSERT INTO OrleansQuery(QueryKey, QueryText) VALUES
('GatewaysQueryKey', '
    SELECT Address, ProxyPort, Generation FROM OrleansMembershipTable
    WHERE DeploymentId = @DeploymentId AND @DeploymentId IS NOT NULL
        AND Status = @Status AND @Status IS NOT NULL AND ProxyPort > 0;')
ON CONFLICT (QueryKey) DO UPDATE SET QueryText = EXCLUDED.QueryText;

INSERT INTO OrleansQuery(QueryKey, QueryText) VALUES
-- Query required for Orleans 9.x+ (see https://github.com/dotnet/orleans/issues/8676)
-- Source: https://raw.githubusercontent.com/dotnet/orleans/refs/heads/main/src/AdoNet/Orleans.Clustering.AdoNet/Migrations/PostgreSQL-Clustering-3.7.0.sql
('CleanupDefunctSiloEntriesKey', '
    DELETE FROM OrleansMembershipTable
    WHERE DeploymentId = @DeploymentId
        AND @DeploymentId IS NOT NULL
        AND IAmAliveTime < @IAmAliveTime
        AND Status != 3;')
ON CONFLICT (QueryKey) DO UPDATE SET QueryText = EXCLUDED.QueryText;

-- =============================================================================
-- QUERIES: Persistence Queries
-- =============================================================================
INSERT INTO OrleansQuery(QueryKey, QueryText) VALUES
('WriteToStorageKey', 'select * from WriteToStorage(@GrainIdHash, @GrainIdN0, @GrainIdN1, @GrainTypeHash, @GrainTypeString, @GrainIdExtensionString, @ServiceId, @GrainStateVersion, @PayloadBinary);')
ON CONFLICT (QueryKey) DO UPDATE SET QueryText = EXCLUDED.QueryText;

INSERT INTO OrleansQuery(QueryKey, QueryText) VALUES
('ReadFromStorageKey', '
    SELECT PayloadBinary, (now() at time zone ''utc''), Version
    FROM OrleansStorage
    WHERE GrainIdHash = @GrainIdHash
        AND GrainTypeHash = @GrainTypeHash AND @GrainTypeHash IS NOT NULL
        AND GrainIdN0 = @GrainIdN0 AND @GrainIdN0 IS NOT NULL
        AND GrainIdN1 = @GrainIdN1 AND @GrainIdN1 IS NOT NULL
        AND GrainTypeString = @GrainTypeString AND GrainTypeString IS NOT NULL
        AND ((@GrainIdExtensionString IS NOT NULL AND GrainIdExtensionString IS NOT NULL AND GrainIdExtensionString = @GrainIdExtensionString) OR @GrainIdExtensionString IS NULL AND GrainIdExtensionString IS NULL)
        AND ServiceId = @ServiceId AND @ServiceId IS NOT NULL;')
ON CONFLICT (QueryKey) DO UPDATE SET QueryText = EXCLUDED.QueryText;

INSERT INTO OrleansQuery(QueryKey, QueryText) VALUES
('ClearStorageKey', '
    UPDATE OrleansStorage
    SET PayloadBinary = NULL, Version = Version + 1
    WHERE GrainIdHash = @GrainIdHash AND @GrainIdHash IS NOT NULL
        AND GrainTypeHash = @GrainTypeHash AND @GrainTypeHash IS NOT NULL
        AND GrainIdN0 = @GrainIdN0 AND @GrainIdN0 IS NOT NULL
        AND GrainIdN1 = @GrainIdN1 AND @GrainIdN1 IS NOT NULL
        AND GrainTypeString = @GrainTypeString AND @GrainTypeString IS NOT NULL
        AND ((@GrainIdExtensionString IS NOT NULL AND GrainIdExtensionString IS NOT NULL AND GrainIdExtensionString = @GrainIdExtensionString) OR @GrainIdExtensionString IS NULL AND GrainIdExtensionString IS NULL)
        AND ServiceId = @ServiceId AND @ServiceId IS NOT NULL
        AND Version IS NOT NULL AND Version = @GrainStateVersion AND @GrainStateVersion IS NOT NULL
    Returning Version as NewGrainStateVersion;')
ON CONFLICT (QueryKey) DO UPDATE SET QueryText = EXCLUDED.QueryText;

SELECT 'Orleans PostgreSQL schema initialized successfully' AS message;
