# One-click test for the colony mood system. Run it by double-clicking test-mood.bat.
Set-Location $PSScriptRoot

Write-Host "1/3 Stopping any old server or client..." -ForegroundColor Cyan
Get-Process Content.Server, Content.Client -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep -Seconds 1

Write-Host "2/3 Building (about 30 to 60 seconds)..." -ForegroundColor Cyan
dotnet build *> build.log
if ($LASTEXITCODE -ne 0) {
    Write-Host "BUILD FAILED. First errors:" -ForegroundColor Red
    Select-String -Path build.log -Pattern "error " | Select-Object -First 10 | ForEach-Object { $_.Line.Trim() }
    Write-Host "Send me those lines. The full output is in build.log." -ForegroundColor Yellow
    exit 1
}
Write-Host "Build OK." -ForegroundColor Green

Write-Host ""
Write-Host "3/3 Starting the server. The game window opens in about 12 seconds." -ForegroundColor Cyan
Write-Host "    Join the round, then open the in-game console and try:" -ForegroundColor Cyan
Write-Host "      setsatiation Hunger 80   (expect: Hungry -10)" -ForegroundColor Cyan
Write-Host "      setsatiation Rest 20     (expect: Tired -10)" -ForegroundColor Cyan
Write-Host "      setsatiation Rest 5      (expect: Exhausted -25)" -ForegroundColor Cyan
Write-Host "    Mood lines appear below within a second or two. Errors show in red." -ForegroundColor Cyan
Write-Host "    Close this window to stop the server. Full log: server.log" -ForegroundColor Cyan
Write-Host ""

Start-Process powershell -WorkingDirectory $PSScriptRoot -ArgumentList "-NoProfile", "-Command", "Start-Sleep 12; dotnet run --project Content.Client --no-build"

dotnet run --project Content.Server --no-build 2>&1 | Tee-Object -FilePath server.log | ForEach-Object {
    $line = "$_"
    if ($line -match "\[ERRO\]") {
        Write-Host $line -ForegroundColor Red
    }
    elseif ($line -match "system\.mood|Server Version") {
        Write-Host $line -ForegroundColor Green
    }
}
