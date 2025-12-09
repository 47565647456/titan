# build.ps1 - Build all projects
Write-Host "Building Titan..." -ForegroundColor Cyan
Set-Location -Path $PSScriptRoot\Source
dotnet build
if ($LASTEXITCODE -eq 0) {
    Write-Host "Build succeeded!" -ForegroundColor Green
} else {
    Write-Host "Build failed!" -ForegroundColor Red
}
