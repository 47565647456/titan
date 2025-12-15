param (
    [switch]$MemoryOnly
)

# test.ps1 - Run all tests
Write-Host "Running Titan Tests..." -ForegroundColor Cyan

Set-Location -Path $PSScriptRoot\..\src
# Always run in-memory tests first
Write-Host "`n=== Running In-Memory Tests ===" -ForegroundColor Yellow
$env:USE_DATABASE = $null
dotnet test Titan.Tests/Titan.Tests.csproj --verbosity normal
$memoryResult = $LASTEXITCODE

if (-not $MemoryOnly) {
    # Then run database tests
    Write-Host "`n=== Running Database Tests ===" -ForegroundColor Yellow
    $env:USE_DATABASE = "true"
    dotnet test Titan.Tests/Titan.Tests.csproj --verbosity normal
    $dbResult = $LASTEXITCODE
} else {
    $dbResult = 0
}

# Summary
Write-Host "`n=== Test Summary ===" -ForegroundColor Cyan
if ($memoryResult -eq 0) {
    Write-Host "In-Memory Tests: PASSED" -ForegroundColor Green
} else {
    Write-Host "In-Memory Tests: FAILED" -ForegroundColor Red
}

if (-not $MemoryOnly) {
    if ($dbResult -eq 0) {
        Write-Host "Database Tests:  PASSED" -ForegroundColor Green
    } else {
        Write-Host "Database Tests:  FAILED" -ForegroundColor Red
    }
}

if ($memoryResult -eq 0 -and $dbResult -eq 0) {
    Write-Host "`nAll tests passed!" -ForegroundColor Green
    exit 0
} else {
    Write-Host "`nSome tests failed!" -ForegroundColor Red
    exit 1
}
