# test.ps1 - Run all tests
Write-Host "Running Titan Tests..." -ForegroundColor Cyan
Set-Location -Path $PSScriptRoot\Source
dotnet test Titan.Tests/Titan.Tests.csproj --verbosity normal
if ($LASTEXITCODE -eq 0) {
    Write-Host "All tests passed!" -ForegroundColor Green
} else {
    Write-Host "Some tests failed!" -ForegroundColor Red
}
