# clean.ps1 - Clean all build artifacts
Write-Host "Cleaning Titan..." -ForegroundColor Cyan

# Clean .NET build artifacts
Get-ChildItem -Path "$PSScriptRoot\Source" -Include "obj", "bin" -Directory -Recurse | 
    ForEach-Object {
        Write-Host "  Removing $_" -ForegroundColor Yellow
        Remove-Item $_ -Recurse -Force
    }

# Clean Docusaurus artifacts
$docsArtifacts = @("node_modules", ".docusaurus", "build")
foreach ($artifact in $docsArtifacts) {
    $path = Join-Path $PSScriptRoot "docs\$artifact"
    if (Test-Path $path) {
        Write-Host "  Removing $path" -ForegroundColor Yellow
        Remove-Item $path -Recurse -Force
    }
}

# Clean log files
if (Test-Path "$PSScriptRoot\Source\logs") {
    Write-Host "  Removing logs" -ForegroundColor Yellow
    Remove-Item "$PSScriptRoot\Source\logs" -Recurse -Force
}

Write-Host "Clean complete!" -ForegroundColor Green
