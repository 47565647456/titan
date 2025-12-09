# PowerShell script to configure git hooks

$hooksPath = "scripts/git-hooks"

# Verify directory exists
if (-not (Test-Path $hooksPath)) {
    Write-Error "Hooks directory '$hooksPath' not found."
    exit 1
}

# Configure Git to use this directory for hooks
git config core.hooksPath $hooksPath

Write-Host "✅ Git hooks configured to use: $hooksPath" -ForegroundColor Green
Write-Host "The pre-push hook is now active for this repository." -ForegroundColor Cyan
