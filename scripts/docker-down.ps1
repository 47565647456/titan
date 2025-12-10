# docker-down.ps1 - Stop YugabyteDB
Write-Host "Stopping YugabyteDB..." -ForegroundColor Cyan
Set-Location -Path $PSScriptRoot\..
docker-compose down
Write-Host "YugabyteDB stopped!" -ForegroundColor Green
