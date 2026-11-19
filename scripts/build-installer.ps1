# Builds DupliXon.exe (self-contained, single file) and the DupliXon-Setup installer.
# Requirements: .NET 8 SDK, Inno Setup 6 (winget install JRSoftware.InnoSetup) and FFmpeg
# (winget install Gyan.FFmpeg.Essentials), or pass -Ffmpeg <folder with ffmpeg.exe>.
param([string]$Ffmpeg)
$ErrorActionPreference = 'Stop'
$root = Resolve-Path "$PSScriptRoot\.."
Set-Location $root

[xml]$props = Get-Content Directory.Build.props
$version = $props.Project.PropertyGroup.Version
Write-Host "DupliXon $version" -ForegroundColor Cyan

Write-Host '1/3 Publicando DupliXon.exe...'
dotnet publish src/DupliXon.App -c Release -r win-x64 --self-contained true `
    -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:EnableCompressionInSingleFile=true -p:DebugType=none -o artifacts/publish
if ($LASTEXITCODE) { throw 'dotnet publish failed' }

Write-Host '2/3 Buscando FFmpeg...'
if (-not $Ffmpeg) {
    $exe = Get-ChildItem "$env:LOCALAPPDATA\Microsoft\WinGet\Packages" -Recurse -Filter ffmpeg.exe -ErrorAction SilentlyContinue |
        Sort-Object { $_.FullName -notmatch 'essentials' }, Length | Select-Object -First 1
    if (-not $exe) { throw 'FFmpeg no encontrado: winget install Gyan.FFmpeg.Essentials' }
    $Ffmpeg = $exe.DirectoryName
}
Write-Host "   $Ffmpeg"

Write-Host '3/3 Creando instalador...'
$iscc = @("$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe", "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe") |
    Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $iscc) { throw 'Inno Setup 6 no encontrado: winget install JRSoftware.InnoSetup' }
& $iscc /Q "/DAppVersion=$version" "/DFfmpegDir=$Ffmpeg" installer\DupliXon.iss
if ($LASTEXITCODE) { throw 'ISCC failed' }

Get-Item "artifacts\DupliXon-Setup-$version.exe" | ForEach-Object {
    Write-Host ("Listo: {0} ({1:N0} MB)" -f $_.FullName, ($_.Length / 1MB)) -ForegroundColor Green
}
