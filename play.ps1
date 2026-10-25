# Pick a scenario, then start the game on Box Station with it.
Set-Location $PSScriptRoot
$file = "Resources\Prototypes\_Colony\Setup\scenarios.yml"
$text = Get-Content $file -Raw
$matches2 = [regex]::Matches($text, "(?m)^\s+id: (\S+)\r?\n\s+name: (.+)\r?\n\s+description: (.+)")
Write-Host ""
Write-Host "Choose a starting scenario:" -ForegroundColor Cyan
$i = 1
foreach ($m in $matches2) {
    Write-Host ("  {0}. {1}" -f $i, $m.Groups[2].Value.Trim()) -ForegroundColor Yellow -NoNewline
    Write-Host ("  - " + $m.Groups[3].Value.Trim())
    $i++
}
Write-Host ""
$pick = Read-Host "Number (just press Enter for 1)"
if ([string]::IsNullOrWhiteSpace($pick)) { $pick = "1" }
$idx = [int]$pick - 1
if ($idx -lt 0 -or $idx -ge $matches2.Count) { Write-Host "Not a valid choice." -ForegroundColor Red; exit 1 }
$id = $matches2[$idx].Groups[1].Value
Write-Host ""
Write-Host "Choose a map:" -ForegroundColor Cyan
Write-Host "  1. Box Station" -ForegroundColor Yellow -NoNewline
Write-Host "  - the original station"
Write-Host "  2. Box Colony" -ForegroundColor Yellow -NoNewline
Write-Host "  - dorm block above the library: 21 rooms and a commons"
Write-Host "  3. Box Scattered" -ForegroundColor Yellow -NoNewline
Write-Host "  - beach and lounge rooms plus bedrooms in maintenance"
$mp = Read-Host "Number (just press Enter for 1)"
$map = "Box"
if ($mp -eq "2") { $map = "BoxColony" }
if ($mp -eq "3") { $map = "BoxScattered" }
& "$PSScriptRoot\test-mood.ps1" -Scenario $id -Map $map
