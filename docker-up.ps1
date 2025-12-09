# docker-up.ps1 - Start CockroachDB cluster
Write-Host "Starting CockroachDB cluster..." -ForegroundColor Cyan
Set-Location -Path $PSScriptRoot
docker-compose up -d
if ($LASTEXITCODE -eq 0) {
    Write-Host "CockroachDB cluster started!" -ForegroundColor Green
    Write-Host "  SQL: localhost:26257" -ForegroundColor Cyan
    Write-Host "  Admin UI: http://localhost:8080" -ForegroundColor Cyan
} else {
    Write-Host "Failed to start CockroachDB!" -ForegroundColor Red
}
