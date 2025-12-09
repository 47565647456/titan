# docker-down.ps1 - Stop CockroachDB cluster
Write-Host "Stopping CockroachDB cluster..." -ForegroundColor Cyan
Set-Location -Path $PSScriptRoot\..
docker-compose down
Write-Host "CockroachDB cluster stopped!" -ForegroundColor Green
