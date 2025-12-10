# test-db.ps1 - Run only database persistence tests
Write-Host "Running Database Persistence Tests..." -ForegroundColor Cyan

Set-Location -Path $PSScriptRoot\..\Source

# Set environment to use database
$env:USE_DATABASE = "true"

Write-Host "`n=== Database Tests ===" -ForegroundColor Yellow
dotnet test Titan.Tests/Titan.Tests.csproj --verbosity normal --filter "FullyQualifiedName~DatabasePersistence"
$result = $LASTEXITCODE

# Summary
Write-Host "`n=== Test Summary ===" -ForegroundColor Cyan
if ($result -eq 0) {
    Write-Host "Database Tests: PASSED" -ForegroundColor Green
    exit 0
} else {
    Write-Host "Database Tests: FAILED" -ForegroundColor Red
    exit 1
}
