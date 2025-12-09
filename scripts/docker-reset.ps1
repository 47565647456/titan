# docker-reset.ps1 - Reset CockroachDB cluster (removes volumes)
Write-Host "Resetting CockroachDB cluster (WARNING: This deletes all data!)..." -ForegroundColor Red
$confirm = Read-Host "Are you sure? (y/N)"
if ($confirm -eq 'y' -or $confirm -eq 'Y') {
    Set-Location -Path $PSScriptRoot\..
    docker-compose down -v
    Write-Host "CockroachDB cluster reset complete!" -ForegroundColor Green
} else {
    Write-Host "Reset cancelled." -ForegroundColor Yellow
}
