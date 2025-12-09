# docker-up.ps1 - Start CockroachDB cluster and initialize schema
Write-Host "Starting CockroachDB cluster..." -ForegroundColor Cyan
Set-Location -Path $PSScriptRoot\..
docker-compose up -d

if ($LASTEXITCODE -eq 0) {
    Write-Host "Containers started. Waiting for CockroachDB to be ready..." -ForegroundColor Yellow
    
    # Wait loop
    $retries = 0
    $maxRetries = 30
    $ready = $false
    
    while ($retries -lt $maxRetries) {
        docker exec roach1 ./cockroach sql --insecure --host=roach1 -e "SELECT 1" | Out-Null
        if ($?) {
            $ready = $true
            break
        }
        Write-Host "." -NoNewline -ForegroundColor DarkGray
        Start-Sleep -Seconds 1
        $retries++
    }
    
    Write-Host "" # Newline

    if ($ready) {
        Write-Host "CockroachDB is ready! Initializing Schema..." -ForegroundColor Cyan
        
        # 1. Create DB
        docker exec roach1 ./cockroach sql --insecure --host=roach1 -e "CREATE DATABASE IF NOT EXISTS titan;"
        
        # 2. Apply Schema
        Get-Content scripts\init-orleans-db.sql | docker exec -i roach1 ./cockroach sql --insecure --host=roach1 -d titan

        if ($?) {
            Write-Host "Schema initialized successfully!" -ForegroundColor Green
            Write-Host "  SQL: localhost:26257" -ForegroundColor Cyan
            Write-Host "  Admin UI: http://localhost:8080" -ForegroundColor Cyan
        } else {
            Write-Host "Failed to apply schema!" -ForegroundColor Red
        }
    } else {
        Write-Host "Timed out waiting for CockroachDB!" -ForegroundColor Red
    }

} else {
    Write-Host "Failed to start CockroachDB containers!" -ForegroundColor Red
}
