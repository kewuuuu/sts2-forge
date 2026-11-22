# Builds the mod and stages the Workshop content folder: dist/workshop/
# Staging only: publishing is a separate, opt-in step (tools/WorkshopUploader --create / --update).
param(
    [string]$Configuration = 'Release',
    [switch]$NoBuild
)

$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $PSScriptRoot
$modId = 'sts2-forge'
$csproj = Join-Path $root ($modId + '.csproj')
$src = Join-Path $root ('dist\' + $modId)
$workshop = Join-Path $root 'dist\workshop'
$dest = Join-Path $workshop $modId

if (-not $NoBuild) {
    dotnet build $csproj -c $Configuration -v minimal
    if ($LASTEXITCODE -ne 0) {
        throw 'build failed'
    }
}

foreach ($name in @("$modId.dll", "$modId.json")) {
    $path = Join-Path $src $name
    if (-not (Test-Path -LiteralPath $path)) {
        throw "missing build output: $path"
    }
}

if (Test-Path -LiteralPath $dest) {
    Remove-Item -LiteralPath $dest -Recurse -Force
}
New-Item -ItemType Directory -Path $dest -Force | Out-Null
New-Item -ItemType Directory -Path $workshop -Force | Out-Null

foreach ($file in Get-ChildItem -LiteralPath $src -Recurse -File) {
    $relative = $file.FullName.Substring($src.Length).TrimStart([char]'\')
    $target = Join-Path $dest $relative
    $targetDir = Split-Path -Parent $target
    if (-not (Test-Path -LiteralPath $targetDir)) {
        New-Item -ItemType Directory -Path $targetDir -Force | Out-Null
    }

    Copy-Item -LiteralPath $file.FullName -Destination $target -Force
    $length = (Get-Item -LiteralPath $target).Length
    if ($length -ne $file.Length) {
        throw ("copy mismatch: " + $target + " is " + $length + " bytes, source is " + $file.Length)
    }

    Write-Host ("  staged " + $relative + "  " + $length + " bytes")
}

$manifestPath = Join-Path $dest "$modId.json"
$manifestText = [System.IO.File]::ReadAllText($manifestPath, [System.Text.Encoding]::UTF8)
$manifest = $manifestText | ConvertFrom-Json
if ($manifest.id -ne $modId) {
    throw ("staged manifest id '" + $manifest.id + "' is not '$modId': " + $manifestPath)
}
Write-Host ("  manifest id=" + $manifest.id + " version=" + $manifest.version)

$iconDir = Join-Path $root 'assets\icons'
if (Test-Path -LiteralPath $iconDir) {
    foreach ($file in Get-ChildItem -LiteralPath $iconDir -File -Filter *.png) {
        $to = Join-Path $workshop $file.Name
        Copy-Item -LiteralPath $file.FullName -Destination $to -Force
        $length = (Get-Item -LiteralPath $to).Length
        if ($length -ne $file.Length) {
            throw ("copy mismatch: " + $to)
        }

        Write-Host ("  staged " + $file.Name + "  " + $length + " bytes")
    }
}

$all = @(Get-ChildItem -LiteralPath $workshop -Recurse -File)
$total = 0
foreach ($file in $all) {
    $total += $file.Length
}
Write-Host ("content ready: " + $all.Count + " files, " + $total + " bytes")
