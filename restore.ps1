# restore.ps1 - Restore all dependencies
Write-Host "Restoring Titan dependencies..." -ForegroundColor Cyan

# Restore .NET packages
Write-Host "Restoring NuGet packages..." -ForegroundColor Yellow
Set-Location -Path $PSScriptRoot\Source
dotnet restore

# Restore npm packages for docs
Write-Host "Restoring npm packages for docs..." -ForegroundColor Yellow
Set-Location -Path $PSScriptRoot\docs
npm install

Write-Host "Restore complete!" -ForegroundColor Green
