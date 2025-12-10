# docker-reset.ps1 - Reset YugabyteDB (removes volumes)
Write-Host "Resetting YugabyteDB (WARNING: This deletes all data!)..." -ForegroundColor Red
$confirm = Read-Host "Are you sure? (y/N)"
if ($confirm -eq 'y' -or $confirm -eq 'Y') {
    Set-Location -Path $PSScriptRoot\..
    docker-compose down -v
    Write-Host "YugabyteDB reset complete!" -ForegroundColor Green
} else {
    Write-Host "Reset cancelled." -ForegroundColor Yellow
}
