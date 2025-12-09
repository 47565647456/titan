# run-all.ps1 - Start all services (opens new terminals)
Write-Host "Starting Titan Services..." -ForegroundColor Cyan

$sourcePath = Join-Path $PSScriptRoot "..\Source"

# Start each service in a new terminal
Start-Process pwsh -ArgumentList "-NoExit", "-Command", "cd '$sourcePath'; dotnet run --project Titan.IdentityHost"
Start-Process pwsh -ArgumentList "-NoExit", "-Command", "cd '$sourcePath'; dotnet run --project Titan.InventoryHost"
Start-Process pwsh -ArgumentList "-NoExit", "-Command", "cd '$sourcePath'; dotnet run --project Titan.TradingHost"
Start-Process pwsh -ArgumentList "-NoExit", "-Command", "cd '$sourcePath'; dotnet run --project Titan.API"

Write-Host "Started all services in separate terminals!" -ForegroundColor Green
Write-Host "  IdentityHost: localhost:11111" -ForegroundColor Cyan
Write-Host "  InventoryHost: localhost:11112" -ForegroundColor Cyan
Write-Host "  TradingHost: localhost:11113" -ForegroundColor Cyan
Write-Host "  API: https://localhost:5001/swagger" -ForegroundColor Cyan
Write-Host "  Orleans Dashboard: http://localhost:8081" -ForegroundColor Cyan
