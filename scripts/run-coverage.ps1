#!/usr/bin/env pwsh
<#
.SYNOPSIS
    Runs all tests with code coverage and generates an HTML report.

.DESCRIPTION
    This script:
    1. Cleans the coverage output directory
    2. Runs all tests with XPlat Code Coverage
    3. Generates an HTML report using ReportGenerator
    4. Opens the report in the default browser

.PARAMETER ReportGeneratorPath
    Path to the reportgenerator executable. If not specified, the script will
    look for it in PATH and common .NET tool locations.

.EXAMPLE
    ./scripts/run-coverage.ps1

.EXAMPLE
    ./scripts/run-coverage.ps1 -ReportGeneratorPath "C:\Users\Dan\Documents\Unreal Projects\ReportGenerator\src\ReportGenerator.Console.NetCore\bin\Release\net10.0\ReportGenerator.exe"
#>

param(
    [string]$ReportGeneratorPath
)

$ErrorActionPreference = "Stop"

# Find ReportGenerator if not specified
if (-not $ReportGeneratorPath) {
    # Try PATH first
    $rgCmd = Get-Command "reportgenerator" -ErrorAction SilentlyContinue
    if ($rgCmd) {
        $ReportGeneratorPath = $rgCmd.Source
    }
    # Try common .NET tools location
    elseif (Test-Path "$env:USERPROFILE\.dotnet\tools\reportgenerator.exe") {
        $ReportGeneratorPath = "$env:USERPROFILE\.dotnet\tools\reportgenerator.exe"
    }
    else {
        Write-Host "ReportGenerator not found!" -ForegroundColor Red
        Write-Host "Install it with: dotnet tool install -g dotnet-reportgenerator-globaltool" -ForegroundColor Yellow
        Write-Host "Or specify the path with: -ReportGeneratorPath <path>" -ForegroundColor Yellow
        exit 1
    }
}

Write-Host "Using ReportGenerator: $ReportGeneratorPath" -ForegroundColor Gray

# Paths relative to repo root
$repoRoot = Split-Path -Parent $PSScriptRoot
$coverageDir = Join-Path $repoRoot "coverage"
$testResultsDir = Join-Path $coverageDir "TestResults"
$reportDir = Join-Path $coverageDir "report"
$runsettings = Join-Path $repoRoot "Source/coverage.runsettings"

Write-Host "=== Titan Code Coverage ===" -ForegroundColor Cyan

# Clean previous results
if (Test-Path $coverageDir) {
    Write-Host "Cleaning previous coverage data..." -ForegroundColor Yellow
    Remove-Item -Recurse -Force $coverageDir
}

# Create output directory
New-Item -ItemType Directory -Path $coverageDir -Force | Out-Null

# Run tests with coverage
Write-Host "`nRunning tests with coverage collection..." -ForegroundColor Cyan
dotnet test "$repoRoot/Source/Titan.sln" `
    --collect:"XPlat Code Coverage" `
    --settings $runsettings `
    --results-directory $testResultsDir

if ($LASTEXITCODE -ne 0) {
    Write-Host "Tests failed!" -ForegroundColor Red
    exit $LASTEXITCODE
}

# Generate HTML report
Write-Host "`nGenerating HTML report..." -ForegroundColor Cyan
& $ReportGeneratorPath `
    -reports:"$testResultsDir/**/coverage.cobertura.xml" `
    -targetdir:$reportDir `
    -reporttypes:Html

if ($LASTEXITCODE -ne 0) {
    Write-Host "Report generation failed!" -ForegroundColor Red
    exit $LASTEXITCODE
}

# Open report
$reportPath = Join-Path $reportDir "index.html"
Write-Host "`n=== Coverage Complete ===" -ForegroundColor Green
Write-Host "Report: $reportPath" -ForegroundColor Green

# Ask to open
$open = Read-Host "Open report in browser? (Y/n)"
if ($open -ne "n") {
    Start-Process $reportPath
}
