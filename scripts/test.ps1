param (
    [switch]$WithDatabase
)

# test.ps1 - Run all tests
Write-Host "Running Titan Tests..." -ForegroundColor Cyan

if ($WithDatabase) {
    Write-Host "Enabled Database Persistence Tests (USE_DATABASE=true)" -ForegroundColor Yellow
    $env:USE_DATABASE = "true"
}

Set-Location -Path $PSScriptRoot\..\Source
dotnet test Titan.Tests/Titan.Tests.csproj --verbosity normal

if ($LASTEXITCODE -eq 0) {
    Write-Host "All tests passed!" -ForegroundColor Green
} else {
    Write-Host "Some tests failed!" -ForegroundColor Red
}
