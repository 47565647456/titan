-- ============================================================
-- CockroachDB Backup Script for Titan
-- ============================================================
-- This script provides templates for backing up both Titan databases.
-- Replace the storage URI with your preferred storage backend.
--
-- Storage Options:
--   userfile:///          - Local CockroachDB userfile storage (no external deps)
--   s3://{bucket}/{path}  - Amazon S3
--   gs://{bucket}/{path}  - Google Cloud Storage  
--   azure-blob://{container} - Azure Blob Storage
--
-- Usage: Run via cockroach sql or the backup.ps1 script
-- ============================================================

-- ============================================================
-- BACKUP TITAN DATABASE (Orleans grain state, game data)
-- ============================================================

-- Full backup with revision history (recommended for production)
-- Enables point-in-time restore within the garbage collection window
BACKUP DATABASE titan INTO 'userfile:///titan-backup' 
  AS OF SYSTEM TIME '-10s' WITH revision_history;

-- Full backup without revision history (smaller, faster)
-- Use for development or when point-in-time restore is not needed
-- BACKUP DATABASE titan INTO 'userfile:///titan-backup' AS OF SYSTEM TIME '-10s';

-- Incremental backup (after an initial full backup exists)
-- BACKUP DATABASE titan INTO LATEST IN 'userfile:///titan-backup' 
--   AS OF SYSTEM TIME '-10s' WITH revision_history;

-- ============================================================
-- BACKUP TITAN_ADMIN DATABASE (Identity, admin users)
-- ============================================================

-- Full backup with revision history
BACKUP DATABASE titan_admin INTO 'userfile:///titan-admin-backup' 
  AS OF SYSTEM TIME '-10s' WITH revision_history;

-- Full backup without revision history
-- BACKUP DATABASE titan_admin INTO 'userfile:///titan-admin-backup' AS OF SYSTEM TIME '-10s';

-- ============================================================
-- CLOUD STORAGE EXAMPLES
-- ============================================================

-- Amazon S3 (with explicit credentials)
-- BACKUP DATABASE titan INTO 's3://{bucket}/titan-backup?AWS_ACCESS_KEY_ID={key}&AWS_SECRET_ACCESS_KEY={secret}' 
--   AS OF SYSTEM TIME '-10s' WITH revision_history;

-- Google Cloud Storage (with base64-encoded service account JSON)
-- BACKUP DATABASE titan INTO 'gs://{bucket}/titan-backup?AUTH=specified&CREDENTIALS={base64_json}' 
--   AS OF SYSTEM TIME '-10s' WITH revision_history;

-- Azure Blob Storage (with account key)
-- BACKUP DATABASE titan INTO 'azure-blob://{container}?AZURE_ACCOUNT_NAME={name}&AZURE_ACCOUNT_KEY={key}' 
--   AS OF SYSTEM TIME '-10s' WITH revision_history;
