<#
.SYNOPSIS
    Runs Titan load tests with common configurations.

.DESCRIPTION
    PowerShell script for running NBomber load tests against Titan.
    Requires Titan.AppHost to be running.

.PARAMETER Scenario
    Scenario to run: auth, character, trading, ratelimit, ratelimit-backoff, all (default: all)

.PARAMETER Users
    Number of concurrent users (default: 50)

.PARAMETER Duration
    Test duration in seconds (default: 60)

.PARAMETER Url
    API base URL (default: https://localhost:7001)

.EXAMPLE
    .\run-loadtest.ps1
    Runs all scenarios with default settings.

.EXAMPLE
    .\run-loadtest.ps1 -Scenario auth -Users 50 -Duration 120
    Runs auth scenario with 50 users for 2 minutes.

.EXAMPLE
    .\run-loadtest.ps1 -Scenario ratelimit -Users 20
    Tests rate limiting by hammering auth endpoint to trigger 429s.
#>

param(
    [ValidateSet("auth", "character", "trading", "ratelimit", "ratelimit-backoff", "all")]
    [string]$Scenario = "all",
    
    [int]$Users = 100,
    
    [int]$Duration = 60,
    
    [string]$Url = "http://localhost:5032"
)

$ErrorActionPreference = "Stop"
$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$srcDir = Join-Path $scriptDir "..\src"

Write-Host "==========================================" -ForegroundColor Cyan
Write-Host "  Titan Load Tests" -ForegroundColor Cyan
Write-Host "==========================================" -ForegroundColor Cyan
Write-Host "  Scenario: $Scenario"
Write-Host "  Users:    $Users"
Write-Host "  Duration: ${Duration}s"
Write-Host "  URL:      $Url"
Write-Host "==========================================" -ForegroundColor Cyan
Write-Host ""

# Check if AppHost is running by testing the health endpoint
try {
    $null = Invoke-WebRequest -Uri "$Url/health" -TimeoutSec 5 -SkipCertificateCheck 2>$null
    Write-Host "[OK] API is reachable at $Url" -ForegroundColor Green
}
catch {
    Write-Host "[WARNING] Could not reach $Url - make sure Titan.AppHost is running!" -ForegroundColor Yellow
    Write-Host "Start it with: dotnet run --project Titan.AppHost" -ForegroundColor Yellow
    Write-Host ""
    $continue = Read-Host "Continue anyway? (y/N)"
    if ($continue -ne "y") {
        exit 1
    }
}

# Run load tests
Push-Location $srcDir
try {
    dotnet run --project Titan.LoadTests -- `
        --url $Url `
        --scenario $Scenario `
        --users $Users `
        --duration $Duration
}
finally {
    Pop-Location
}

Write-Host ""
Write-Host "==========================================" -ForegroundColor Cyan
Write-Host "  Load test complete!" -ForegroundColor Green
Write-Host "  Reports saved to: src/Titan.LoadTests/reports/" -ForegroundColor Cyan
Write-Host "==========================================" -ForegroundColor Cyan
