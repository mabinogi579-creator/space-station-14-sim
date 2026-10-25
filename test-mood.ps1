# One-click test for the colony mood system. Run it by double-clicking test-mood.bat.
param([string]$Scenario = "SmallCrew", [string]$Map = "Box")
Set-Location $PSScriptRoot

Write-Host "1/3 Stopping any old server or client..." -ForegroundColor Cyan
Get-Process Content.Server, Content.Client -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep -Seconds 1

Write-Host "2/3 Building (about 30 to 60 seconds)..." -ForegroundColor Cyan
$sw = [System.Diagnostics.Stopwatch]::StartNew()
# Only build the two programs we run (not the tests and tools in the same solution).
dotnet build Content.Server\Content.Server.csproj *> build.log
$serverOk = ($LASTEXITCODE -eq 0)
if ($serverOk) {
    dotnet build Content.Client\Content.Client.csproj *>> build.log
    $serverOk = ($LASTEXITCODE -eq 0)
}
Write-Host ("Build took {0:N0} seconds." -f $sw.Elapsed.TotalSeconds) -ForegroundColor DarkGray
if (-not $serverOk) {
    Write-Host "BUILD FAILED. First errors:" -ForegroundColor Red
    Select-String -Path build.log -Pattern "error " | Select-Object -First 10 | ForEach-Object { $_.Line.Trim() }
    Write-Host "Send me those lines. The full output is in build.log." -ForegroundColor Yellow
    exit 1
}
Write-Host "Build OK." -ForegroundColor Green

Write-Host ""
Write-Host "Scenario: $Scenario    Map: $Map" -ForegroundColor Yellow
Write-Host "3/3 Starting the server. The game window opens as soon as the server is ready." -ForegroundColor Cyan
Write-Host "    The map is chosen in play.bat (Box, or BoxColony with the expanded dorms)." -ForegroundColor Cyan
Write-Host "    Join the round, then open the in-game console and try:" -ForegroundColor Cyan
Write-Host "      setsatiation Hunger 80   (expect: Hungry -10)" -ForegroundColor Cyan
Write-Host "      setsatiation Rest 20     (expect: Tired -10)" -ForegroundColor Cyan
Write-Host "      setsatiation Rest 5      (expect: Exhausted -25)" -ForegroundColor Cyan
Write-Host "    Mood lines appear below within a second or two. Errors show in red." -ForegroundColor Cyan
Write-Host "    Press Ctrl+C in this window to stop the server. Full log: server.log" -ForegroundColor Cyan
Write-Host ""

Remove-Item server.log, server.err, client.log -ErrorAction SilentlyContinue
$script:clientStarted = $false

# The server runs in a hidden window, so stray key presses can never crash it.
$server = Start-Process dotnet -WorkingDirectory $PSScriptRoot -WindowStyle Hidden -PassThru `
    -RedirectStandardOutput server.log -RedirectStandardError server.err `
    -ArgumentList "run --project Content.Server --no-build -- --cvar game.map=$Map --cvar colony.scenario=$Scenario"

function Show-Line($line) {
    if ($line -match "-> Ready" -and -not $script:clientStarted) {
        $script:clientStarted = $true
        Write-Host "Server is ready, starting the game window..." -ForegroundColor Cyan
        Start-Process powershell -WorkingDirectory $PSScriptRoot -ArgumentList "-NoProfile", "-NoExit", "-Command", "dotnet run --project Content.Client --no-build 2>&1 | Tee-Object -FilePath client.log"
    }
    if ($line -match "\[ERRO\]|\[FATL\]") { Write-Host $line -ForegroundColor Red }
    elseif ($line -match "system\.mood|system\.colony|Server Version") { Write-Host $line -ForegroundColor Green }
}

try {
    while (-not (Test-Path server.log)) { Start-Sleep -Milliseconds 200 }
    $fs = [System.IO.FileStream]::new("$PSScriptRoot\server.log", 'Open', 'Read', 'ReadWrite')
    $reader = [System.IO.StreamReader]::new($fs)
    while ($true) {
        while (($line = $reader.ReadLine()) -ne $null) { Show-Line $line }
        if ($server.HasExited) { break }
        Start-Sleep -Milliseconds 500
    }
    while (($line = $reader.ReadLine()) -ne $null) { Show-Line $line }
    Write-Host "The server stopped." -ForegroundColor Yellow
    if ((Test-Path server.err) -and (Get-Item server.err).Length -gt 0) {
        Write-Host "Server error output:" -ForegroundColor Red
        Get-Content server.err -TotalCount 15 | ForEach-Object { Write-Host $_ -ForegroundColor Red }
    }
}
finally {
    if (-not $server.HasExited) { Stop-Process -Id $server.Id -Force -ErrorAction SilentlyContinue }
    Get-Process Content.Server -ErrorAction SilentlyContinue | Stop-Process -Force
}
