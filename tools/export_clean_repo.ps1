# Exports a clean, open-source copy of the King's Valley II project without copyrighted ROM assets.
param(
    [string]$Target = "..\KingsValley-OpenSource"
)

$ErrorActionPreference = "Stop"
$sourceRoot = Split-Path -Parent $PSScriptRoot
if ([System.IO.Path]::IsPathRooted($Target)) {
    $targetRoot = [System.IO.Path]::GetFullPath($Target)
} else {
    $targetRoot = [System.IO.Path]::GetFullPath((Join-Path $sourceRoot $Target))
}

Write-Host "Exporting clean repository from: $sourceRoot" -ForegroundColor Cyan
Write-Host "Target clean repository:        $targetRoot" -ForegroundColor Cyan

if (-not (Test-Path $targetRoot)) {
    New-Item -ItemType Directory -Path $targetRoot -Force | Out-Null
}

$gitDir = Join-Path $targetRoot ".git"
$hasGit = Test-Path $gitDir

Write-Host "[1/3] Mirroring tracked project files with robocopy..." -ForegroundColor Yellow

$excludeDirs = @(
    ".git", "bin", "obj", ".vs", "TestResults", "FontCache",
    "work", "dist", "packages", "Rom", "RomMsx1", "scratch"
)

$excludeFiles = @(
    "*.rom", "*.bin.bak", "*.dsk", "*.tmp", "*.suo", "*.user",
    "audio.msxaudio", "*.vgm"
)

robocopy $sourceRoot $targetRoot /MIR /XD $excludeDirs /XF $excludeFiles /R:1 /W:1 /NJH /NJS /NDL /NC /NS

# Ensure Content Rom, RomMsx1, and scratch directories are completely removed if left behind
$romDir = Join-Path $targetRoot "src\KingsValley.Desktop\Content\Rom"
if (Test-Path $romDir) {
    Remove-Item $romDir -Recurse -Force -ErrorAction SilentlyContinue
}
$romMsx1Dir = Join-Path $targetRoot "src\KingsValley.Desktop\Content\RomMsx1"
if (Test-Path $romMsx1Dir) {
    Remove-Item $romMsx1Dir -Recurse -Force -ErrorAction SilentlyContinue
}
$scratchDir = Join-Path $targetRoot "scratch"
if (Test-Path $scratchDir) {
    Remove-Item $scratchDir -Recurse -Force -ErrorAction SilentlyContinue
}
Get-ChildItem -Path $targetRoot -Recurse -Directory -Include "bin", "obj" | Remove-Item -Recurse -Force -ErrorAction SilentlyContinue

Write-Host "[2/3] Verifying no prohibited proprietary files leaked..." -ForegroundColor Yellow

$leaks = Get-ChildItem -Path $targetRoot -Recurse -File | Where-Object {
    $_.Extension -in @(".rom", ".dsk", ".vgm") -or
    $_.Name -eq "audio.msxaudio" -or
    $_.FullName -match "\\Content\\Rom(\\|$)" -or
    $_.FullName -match "\\Content\\RomMsx1(\\|$)" -or
    $_.FullName -match "\\work\\"
}

if ($leaks.Count -gt 0) {
    Write-Error "Prohibited files detected in target: $($leaks.FullName -join ', ')"
    exit 1
}

Write-Host "Check passed: Zero copyrighted cartridge/art/audio assets in clean repo." -ForegroundColor Green

Write-Host "[3/3] Testing clean build..." -ForegroundColor Yellow
Push-Location $targetRoot
try {
    dotnet build KingsValley.slnx -c Release
    if ($LASTEXITCODE -eq 0) {
        Write-Host "SUCCESS: Clean repository builds cleanly." -ForegroundColor Green
        Write-Host "Target location: $targetRoot" -ForegroundColor Green
    } else {
        Write-Warning "Clean repository build failed."
    }
} finally {
    Pop-Location
}
