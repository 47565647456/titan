-- ============================================================
-- CockroachDB Restore Script for Titan
-- ============================================================
-- This script provides templates for restoring Titan databases.
-- 
-- IMPORTANT: 
--   - You cannot restore a database that already exists.
--   - Drop the database first or restore to a new database name.
--   - Point-in-time restore requires backups made WITH revision_history.
--
-- Usage: Run via cockroach sql or modify backup.ps1 for restore
-- ============================================================

-- ============================================================
-- VIEW AVAILABLE BACKUPS
-- ============================================================

-- List all backups in a collection
-- SHOW BACKUPS IN 'userfile:///titan-backup';

-- Show details of a specific backup
-- SHOW BACKUP FROM LATEST IN 'userfile:///titan-backup';

-- ============================================================
-- RESTORE TITAN DATABASE
-- ============================================================

-- Restore to latest backup
-- Note: titan database must not exist (drop first if needed)
RESTORE DATABASE titan FROM LATEST IN 'userfile:///titan-backup';

-- Point-in-time restore (requires revision_history backup)
-- Restore to exact timestamp before an issue occurred
-- RESTORE DATABASE titan FROM LATEST IN 'userfile:///titan-backup' 
--   AS OF SYSTEM TIME '2025-12-15 10:00:00+00:00';

-- Restore to a specific backup (not latest)
-- First run SHOW BACKUPS to get available timestamps
-- RESTORE DATABASE titan FROM '2025/12/15-100000.00' IN 'userfile:///titan-backup';

-- ============================================================
-- RESTORE TITAN_ADMIN DATABASE
-- ============================================================

-- Restore to latest backup
RESTORE DATABASE titan_admin FROM LATEST IN 'userfile:///titan-admin-backup';

-- Point-in-time restore
-- RESTORE DATABASE titan_admin FROM LATEST IN 'userfile:///titan-admin-backup' 
--   AS OF SYSTEM TIME '2025-12-15 10:00:00+00:00';

-- ============================================================
-- CLOUD STORAGE EXAMPLES
-- ============================================================

-- Amazon S3
-- RESTORE DATABASE titan FROM LATEST IN 's3://{bucket}/titan-backup?AWS_ACCESS_KEY_ID={key}&AWS_SECRET_ACCESS_KEY={secret}';

-- Google Cloud Storage
-- RESTORE DATABASE titan FROM LATEST IN 'gs://{bucket}/titan-backup?AUTH=specified&CREDENTIALS={base64_json}';

-- Azure Blob Storage
-- RESTORE DATABASE titan FROM LATEST IN 'azure-blob://{container}?AZURE_ACCOUNT_NAME={name}&AZURE_ACCOUNT_KEY={key}';

-- ============================================================
-- PRE-RESTORE: DROP EXISTING DATABASE (USE WITH CAUTION!)
-- ============================================================

-- DROP DATABASE IF EXISTS titan CASCADE;
-- DROP DATABASE IF EXISTS titan_admin CASCADE;
