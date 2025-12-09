-- Orleans Grain Storage Schema for CockroachDB (PostgreSQL-compatible)
-- Based on Orleans PostgreSQL persistence scripts

-- Create the OrleansStorage table for grain state persistence
CREATE TABLE IF NOT EXISTS OrleansStorage (
    GrainIdHash INT NOT NULL,
    GrainIdN0 BIGINT NOT NULL,
    GrainIdN1 BIGINT NOT NULL,
    GrainTypeHash INT NOT NULL,
    GrainTypeString VARCHAR(512) NOT NULL,
    GrainIdExtensionString VARCHAR(512),
    ServiceId VARCHAR(150) NOT NULL,
    PayloadBinary BYTEA,
    ModifiedOn TIMESTAMP NOT NULL DEFAULT NOW(),
    Version INT NOT NULL DEFAULT 0,

    CONSTRAINT PK_OrleansStorage PRIMARY KEY (GrainIdHash, GrainTypeHash, GrainIdN0, GrainIdN1, GrainIdExtensionString, ServiceId)
);

-- Create indexes for efficient grain lookups
CREATE INDEX IF NOT EXISTS IX_OrleansStorage_GrainType 
ON OrleansStorage (GrainTypeHash, GrainIdHash);

CREATE INDEX IF NOT EXISTS IX_OrleansStorage_ServiceId 
ON OrleansStorage (ServiceId);

-- Orleans Reminders table (if using reminders)
CREATE TABLE IF NOT EXISTS OrleansRemindersTable (
    ServiceId VARCHAR(150) NOT NULL,
    GrainId VARCHAR(150) NOT NULL,
    ReminderName VARCHAR(150) NOT NULL,
    StartTime TIMESTAMP NOT NULL,
    Period BIGINT NOT NULL,
    GrainHash INT NOT NULL,
    Version INT NOT NULL,

    CONSTRAINT PK_OrleansRemindersTable PRIMARY KEY (ServiceId, GrainId, ReminderName)
);

-- Orleans Clustering table for membership
CREATE TABLE IF NOT EXISTS OrleansMembershipTable (
    DeploymentId VARCHAR(150) NOT NULL,
    Address VARCHAR(45) NOT NULL,
    Port INT NOT NULL,
    Generation INT NOT NULL,
    SiloName VARCHAR(150) NOT NULL,
    HostName VARCHAR(150) NOT NULL,
    Status INT NOT NULL,
    ProxyPort INT,
    SuspectTimes VARCHAR(8000),
    StartTime TIMESTAMP NOT NULL,
    IAmAliveTime TIMESTAMP NOT NULL,

    CONSTRAINT PK_OrleansMembershipTable PRIMARY KEY (DeploymentId, Address, Port, Generation)
);

CREATE TABLE IF NOT EXISTS OrleansMembershipVersionTable (
    DeploymentId VARCHAR(150) NOT NULL PRIMARY KEY,
    Timestamp TIMESTAMP NOT NULL DEFAULT NOW(),
    Version INT NOT NULL DEFAULT 0
);

-- Grant message for verification
SELECT 'Orleans schema initialized successfully' AS message;
