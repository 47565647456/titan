# restore.ps1 - Restore all dependencies
Write-Host "Restoring Titan dependencies..." -ForegroundColor Cyan
$root = "$PSScriptRoot\.."

# Restore .NET packages
Write-Host "Restoring NuGet packages..." -ForegroundColor Yellow
Set-Location -Path "$root\src"
dotnet restore

# Restore npm packages for docs
Write-Host "Restoring npm packages for docs..." -ForegroundColor Yellow
Set-Location -Path "$root\docs"
npm install

Write-Host "Restore complete!" -ForegroundColor Green
