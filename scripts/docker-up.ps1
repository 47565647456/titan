# docker-up.ps1 - Start YugabyteDB and initialize schema
Write-Host "Starting YugabyteDB..." -ForegroundColor Cyan
Set-Location -Path $PSScriptRoot\..
docker-compose up -d

if ($LASTEXITCODE -eq 0) {
    Write-Host "Container started. Waiting for YugabyteDB to be ready..." -ForegroundColor Yellow
    
    # Wait loop
    $retries = 0
    $maxRetries = 30
    $ready = $false
    
    while ($retries -lt $maxRetries) {
        docker exec yugabyte bin/ysqlsh -h yugabyte -U yugabyte -c "SELECT 1" 2>$null | Out-Null
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
        Write-Host "YugabyteDB is ready! Initializing Schema..." -ForegroundColor Cyan
        
        # 1. Create DB
        docker exec yugabyte bin/ysqlsh -h yugabyte -U yugabyte -c "CREATE DATABASE titan;" 2>$null
        
        # 2. Apply Schema
        Get-Content scripts\init-orleans-db.sql | docker exec -i yugabyte bin/ysqlsh -h yugabyte -U yugabyte -d titan

        if ($?) {
            Write-Host "Schema initialized successfully!" -ForegroundColor Green
            Write-Host "  YSQL: localhost:5433" -ForegroundColor Cyan
            Write-Host "  Admin UI: http://localhost:15433" -ForegroundColor Cyan
        } else {
            Write-Host "Failed to apply schema!" -ForegroundColor Red
        }
    } else {
        Write-Host "Timed out waiting for YugabyteDB!" -ForegroundColor Red
    }

} else {
    Write-Host "Failed to start YugabyteDB container!" -ForegroundColor Red
}
