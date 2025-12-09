# docs-deploy.ps1 - Build and prepare docs for Cloudflare Pages deployment
Write-Host "Building Titan Documentation for Cloudflare Pages..." -ForegroundColor Cyan

Set-Location -Path $PSScriptRoot\..\docs

# Install dependencies if needed
if (-not (Test-Path "node_modules")) {
    Write-Host "Installing dependencies..." -ForegroundColor Yellow
    npm install
}

# Build the site
Write-Host "Building static site..." -ForegroundColor Yellow
npm run build

if ($LASTEXITCODE -eq 0) {
    Write-Host "Build complete! Output in docs/build/" -ForegroundColor Green
    Write-Host ""
    Write-Host "To deploy to Cloudflare Pages:" -ForegroundColor Cyan
    Write-Host "  1. Push to your git repository" -ForegroundColor White
    Write-Host "  2. Cloudflare Pages will auto-deploy from 'docs' folder" -ForegroundColor White
    Write-Host "  3. Or use: npx wrangler pages deploy build --project-name=titan-docs" -ForegroundColor White
} else {
    Write-Host "Build failed!" -ForegroundColor Red
}
