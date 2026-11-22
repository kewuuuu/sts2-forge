param(
    [string]$LogPath = (Join-Path $env:APPDATA 'SlayTheSpire2\logs\godot.log')
)

$ErrorActionPreference = 'Stop'

if (-not (Test-Path -LiteralPath $LogPath)) {
    throw "no log at $LogPath - start the game once"
}

$matches = Select-String -LiteralPath $LogPath -Pattern '\[sts2-forge\]'
if (-not $matches) {
    Write-Host ("No sts2-forge lines in " + $LogPath)
    exit 1
}

$matches | ForEach-Object { Write-Host ($_.LineNumber.ToString() + ': ' + $_.Line) }
