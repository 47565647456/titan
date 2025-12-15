-- ============================================================
-- CockroachDB Automated Backup Schedules for Titan
-- ============================================================
-- This script creates backup schedules for both Titan databases.
-- Schedules are stored in the cluster and persist across restarts.
--
-- Default schedule:
--   - Full backup: Daily at midnight UTC
--   - Incremental backups: Every hour
--   - Revision history: Enabled for point-in-time restore
--
-- To view schedules: SHOW SCHEDULES;
-- To pause a schedule: PAUSE SCHEDULE <schedule_id>;
-- To drop a schedule: DROP SCHEDULE <schedule_id>;
-- ============================================================

-- ============================================================
-- DROP EXISTING SCHEDULES (if recreating)
-- ============================================================
-- Uncomment to recreate schedules:
-- DROP SCHEDULE IF EXISTS titan_backup_full;
-- DROP SCHEDULE IF EXISTS titan_admin_backup_full;

-- ============================================================
-- TITAN DATABASE BACKUP SCHEDULE
-- ============================================================

-- Create backup schedule for titan database
-- Hourly incremental backups with daily full backups
CREATE SCHEDULE IF NOT EXISTS titan_backup_schedule
FOR BACKUP DATABASE titan INTO 'userfile:///titan-scheduled-backup'
  WITH revision_history
  RECURRING '0 * * * *'
  FULL BACKUP '@daily'
  WITH SCHEDULE OPTIONS first_run = 'now', on_execution_failure = 'pause';

-- ============================================================
-- TITAN_ADMIN DATABASE BACKUP SCHEDULE
-- ============================================================

-- Create backup schedule for titan_admin database
-- Hourly incremental backups with daily full backups
CREATE SCHEDULE IF NOT EXISTS titan_admin_backup_schedule
FOR BACKUP DATABASE titan_admin INTO 'userfile:///titan-admin-scheduled-backup'
  WITH revision_history
  RECURRING '0 * * * *'
  FULL BACKUP '@daily'
  WITH SCHEDULE OPTIONS first_run = 'now', on_execution_failure = 'pause';

-- ============================================================
-- VERIFY SCHEDULES CREATED
-- ============================================================

-- Show all backup schedules
-- SHOW SCHEDULES FOR BACKUP;

-- Show schedule details
-- SHOW SCHEDULE titan_backup_schedule;
