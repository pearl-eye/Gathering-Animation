#Requires -Version 7.2
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$DalamudPath,
    [string]$DotNet = 'dotnet',
    [ValidatePattern('^[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+$')]
    [string]$Repository = 'pearl-eye/GatheringAnimation',
    [string]$ReleaseTag = ''
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$libraryPath = (Resolve-Path -LiteralPath $DalamudPath).Path
$libraries = @('Dalamud', 'Dalamud.Bindings.ImGui', 'FFXIVClientStructs', 'InteropGenerator.Runtime', 'Lumina', 'Lumina.Excel')
foreach ($name in $libraries) {
    $path = Join-Path $libraryPath "$name.dll"
    if (-not [IO.File]::Exists($path) -or [Reflection.AssemblyName]::GetAssemblyName($path).Name -cne $name) {
        throw "Missing or invalid Dalamud reference: $name.dll"
    }
}
$dalamud = [Reflection.AssemblyName]::GetAssemblyName((Join-Path $libraryPath 'Dalamud.dll'))
if ($dalamud.Version.Major -ne 15) {
    throw 'This plugin requires Dalamud API 15 references.'
}

$outputDirectory = Join-Path $PSScriptRoot 'artifacts/plugin'
& $DotNet build (Join-Path $PSScriptRoot 'Gather animation.csproj') -c Release `
    "-p:DalamudLibPath=$libraryPath" --output $outputDirectory --nologo
if ($LASTEXITCODE -ne 0) {
    throw 'Gather animation build failed.'
}
& (Join-Path $PSScriptRoot 'scripts/Package.ps1') -InputDirectory $outputDirectory `
    -Repository $Repository -ReleaseTag $ReleaseTag
