<#
.SYNOPSIS
    CockroachDB Backup Script for Titan databases.

.DESCRIPTION
    Backs up the Titan CockroachDB databases (titan and titan_admin) to the specified
    storage provider. Supports userfile (local), S3, GCS, and Azure Blob Storage.

.PARAMETER Provider
    Storage provider: userfile, s3, gcs, azure. Default: userfile

.PARAMETER Database
    Which database to backup: all, titan, titan_admin. Default: all

.PARAMETER RevisionHistory
    Enable revision history for point-in-time restore. Default: $true

.PARAMETER ContainerName
    Docker container name for CockroachDB. Default: titan-db

.PARAMETER S3Bucket
    S3 bucket name (required for s3 provider)

.PARAMETER S3AccessKey
    AWS Access Key ID (required for s3 provider)

.PARAMETER S3SecretKey
    AWS Secret Access Key (required for s3 provider)

.PARAMETER S3Region
    AWS Region. Default: us-east-1

.PARAMETER GcsBucket
    GCS bucket name (required for gcs provider)

.PARAMETER GcsCredentials
    Base64-encoded GCS service account JSON (required for gcs provider)

.PARAMETER AzureContainer
    Azure Blob container name (required for azure provider)

.PARAMETER AzureAccountName
    Azure Storage account name (required for azure provider)

.PARAMETER AzureAccountKey
    Azure Storage account key (required for azure provider)

.EXAMPLE
    # Backup to userfile (local)
    .\backup.ps1

.EXAMPLE
    # Backup to S3 with revision history
    .\backup.ps1 -Provider s3 -S3Bucket "my-backups" -S3AccessKey "XXX" -S3SecretKey "YYY"

.EXAMPLE
    # Backup only titan database without revision history
    .\backup.ps1 -Database titan -RevisionHistory:$false
#>

param(
    [ValidateSet("userfile", "s3", "gcs", "azure")]
    [string]$Provider = "userfile",
    
    [ValidateSet("all", "titan", "titan_admin")]
    [string]$Database = "all",
    
    [bool]$RevisionHistory = $true,
    
    [string]$ContainerName = "titan-db",
    
    # S3 parameters
    [string]$S3Bucket = "",
    [string]$S3AccessKey = "",
    [string]$S3SecretKey = "",
    [string]$S3Region = "us-east-1",
    
    # GCS parameters
    [string]$GcsBucket = "",
    [string]$GcsCredentials = "",
    
    # Azure parameters
    [string]$AzureContainer = "",
    [string]$AzureAccountName = "",
    [string]$AzureAccountKey = ""
)

$ErrorActionPreference = "Stop"

function Get-StorageUri {
    param(
        [string]$Provider,
        [string]$BackupName
    )
    
    switch ($Provider) {
        "userfile" {
            return "userfile:///$BackupName"
        }
        "s3" {
            if (-not $S3Bucket -or -not $S3AccessKey -or -not $S3SecretKey) {
                throw "S3 provider requires -S3Bucket, -S3AccessKey, and -S3SecretKey parameters"
            }
            return "s3://$S3Bucket/$BackupName`?AWS_ACCESS_KEY_ID=$S3AccessKey&AWS_SECRET_ACCESS_KEY=$S3SecretKey&AWS_REGION=$S3Region"
        }
        "gcs" {
            if (-not $GcsBucket -or -not $GcsCredentials) {
                throw "GCS provider requires -GcsBucket and -GcsCredentials parameters"
            }
            return "gs://$GcsBucket/$BackupName`?AUTH=specified&CREDENTIALS=$GcsCredentials"
        }
        "azure" {
            if (-not $AzureContainer -or -not $AzureAccountName -or -not $AzureAccountKey) {
                throw "Azure provider requires -AzureContainer, -AzureAccountName, and -AzureAccountKey parameters"
            }
            $encodedKey = [System.Web.HttpUtility]::UrlEncode($AzureAccountKey)
            return "azure-blob://$AzureContainer/$BackupName`?AZURE_ACCOUNT_NAME=$AzureAccountName&AZURE_ACCOUNT_KEY=$encodedKey"
        }
    }
}

function Invoke-CockroachBackup {
    param(
        [string]$DatabaseName,
        [string]$StorageUri,
        [bool]$WithRevisionHistory
    )
    
    $revisionHistoryClause = if ($WithRevisionHistory) { " WITH revision_history" } else { "" }
    $sql = "BACKUP DATABASE $DatabaseName INTO '$StorageUri' AS OF SYSTEM TIME '-10s'$revisionHistoryClause;"
    
    Write-Host "Backing up $DatabaseName to $Provider storage..." -ForegroundColor Cyan
    Write-Host "SQL: $sql" -ForegroundColor DarkGray
    
    $result = docker exec $ContainerName ./cockroach sql --certs-dir=/cockroach/certs --execute="$sql" 2>&1
    
    if ($LASTEXITCODE -ne 0) {
        Write-Host "ERROR: Backup of $DatabaseName failed!" -ForegroundColor Red
        Write-Host $result -ForegroundColor Red
        return $false
    }
    
    Write-Host "SUCCESS: $DatabaseName backup completed!" -ForegroundColor Green
    Write-Host $result -ForegroundColor DarkGray
    return $true
}

# Main execution
Write-Host "============================================" -ForegroundColor Yellow
Write-Host "CockroachDB Backup for Titan" -ForegroundColor Yellow
Write-Host "============================================" -ForegroundColor Yellow
Write-Host "Provider: $Provider"
Write-Host "Database: $Database"
Write-Host "Revision History: $RevisionHistory"
Write-Host ""

$success = $true
$timestamp = Get-Date -Format "yyyyMMdd-HHmmss"

# Backup titan database
if ($Database -eq "all" -or $Database -eq "titan") {
    $uri = Get-StorageUri -Provider $Provider -BackupName "titan-backup"
    if (-not (Invoke-CockroachBackup -DatabaseName "titan" -StorageUri $uri -WithRevisionHistory $RevisionHistory)) {
        $success = $false
    }
}

# Backup titan_admin database
if ($Database -eq "all" -or $Database -eq "titan_admin") {
    $uri = Get-StorageUri -Provider $Provider -BackupName "titan-admin-backup"
    if (-not (Invoke-CockroachBackup -DatabaseName "titan_admin" -StorageUri $uri -WithRevisionHistory $RevisionHistory)) {
        $success = $false
    }
}

Write-Host ""
if ($success) {
    Write-Host "============================================" -ForegroundColor Green
    Write-Host "All backups completed successfully!" -ForegroundColor Green
    Write-Host "============================================" -ForegroundColor Green
} else {
    Write-Host "============================================" -ForegroundColor Red
    Write-Host "Some backups failed. Check the output above." -ForegroundColor Red
    Write-Host "============================================" -ForegroundColor Red
    exit 1
}
