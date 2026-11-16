#Requires -Version 7.2
[CmdletBinding()]
param(
    [string]$InputDirectory = '',
    [ValidatePattern('^[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+$')]
    [string]$Repository = 'pearl-eye/Gathering-Animation',
    [string]$ReleaseTag = ''
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$repoRoot = Split-Path -Parent $PSScriptRoot
$pluginName = 'Gather animation'
$displayName = 'Gathering Animation'
if ([string]::IsNullOrWhiteSpace($InputDirectory)) {
    $InputDirectory = Join-Path $repoRoot 'artifacts/plugin'
}
$InputDirectory = (Resolve-Path -LiteralPath $InputDirectory).Path
$manifest = Get-Content -LiteralPath (Join-Path $InputDirectory "$pluginName.json") -Raw | ConvertFrom-Json -AsHashtable
$sourceManifest = Get-Content -LiteralPath (Join-Path $repoRoot "$pluginName.json") -Raw | ConvertFrom-Json -AsHashtable
if ($manifest['InternalName'] -cne $pluginName -or $manifest['Name'] -cne $displayName -or
    $sourceManifest['InternalName'] -cne $pluginName -or $sourceManifest['Name'] -cne $displayName -or
    $manifest['DalamudApiLevel'] -ne 15) {
    throw 'Expected display name Gathering Animation, compatible internal name Gather animation, and Dalamud API 15.'
}
foreach ($field in @('WorkingPluginId', 'InstalledFromUrl', 'Disabled', 'Testing', 'ScheduledForDeletion', 'IsThirdParty')) {
    if ($manifest.Contains($field)) {
        throw "Installed-plugin state must not be distributed: $field"
    }
}
if ([string]$manifest['RepoUrl'] -cne "https://github.com/$Repository") {
    throw 'Manifest RepoUrl must identify this release repository.'
}
$version = [string]$manifest['AssemblyVersion']
if ($version -notmatch '^\d+\.\d+\.\d+\.\d+$' -or $sourceManifest['AssemblyVersion'] -cne $version) {
    throw 'The built and source manifest versions must agree in four-part form.'
}
function Normalize-Version([string]$text) {
    $parsed = [Version]$text
    '{0}.{1}.{2}.{3}' -f $parsed.Major, $parsed.Minor, [Math]::Max(0, $parsed.Build), [Math]::Max(0, $parsed.Revision)
}
[xml]$project = Get-Content -LiteralPath (Join-Path $repoRoot "$pluginName.csproj") -Raw
$projectVersion = [string]($project.Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1)
if ((Normalize-Version $projectVersion) -cne $version) {
    throw 'Project Version must match the original plugin version without incrementing it.'
}
$assemblyPath = Join-Path $InputDirectory "$pluginName.dll"
# Static metadata only: this does not load Plugin or enable its game hooks.
$assembly = [Reflection.AssemblyName]::GetAssemblyName($assemblyPath)
$fileVersion = [Diagnostics.FileVersionInfo]::GetVersionInfo($assemblyPath)
if ($assembly.Name -cne $pluginName -or $assembly.Version.ToString() -cne $version -or
    $fileVersion.FileVersion -cne $version -or $fileVersion.ProductVersion -cne $version) {
    throw 'DLL assembly, file and product versions must match the manifest.'
}
if ($fileVersion.ProductName -cne $displayName -or $fileVersion.FileDescription -cne $displayName) {
    throw 'DLL product and title must use the display name Gathering Animation.'
}
$stream = [IO.File]::OpenRead($assemblyPath)
$pe = [Reflection.PortableExecutable.PEReader]::new($stream)
try {
    if ($pe.PEHeaders.CoffHeader.Machine -ne [Reflection.PortableExecutable.Machine]::Amd64) {
        throw 'The plugin must target x64.'
    }
} finally {
    $pe.Dispose()
    $stream.Dispose()
}
$deps = Get-Content -LiteralPath (Join-Path $InputDirectory "$pluginName.deps.json") -Raw | ConvertFrom-Json -AsHashtable
$libraryKeys = @($deps['libraries'].Keys)
if ($libraryKeys.Count -ne 1 -or -not $libraryKeys[0].StartsWith("$pluginName/", [StringComparison]::Ordinal)) {
    throw 'The dependency manifest must contain only the plugin itself.'
}
$libraryKey = $libraryKeys[0]
if ((Normalize-Version $libraryKey.Substring($pluginName.Length + 1)) -cne $version) {
    throw 'The dependency version must match the plugin (three- or four-part versions are accepted).'
}
$runtimeTarget = [string]$deps['runtimeTarget']['name']
$target = $deps['targets'][$runtimeTarget]
if ($runtimeTarget -notmatch '^\.NETCoreApp,Version=v10\.0(?:/[^/]+)?$' -or
    $null -eq $target -or $target.Count -ne 1 -or -not $target.Contains($libraryKey)) {
    throw 'Unexpected runtime dependency target; .NET 10 is required.'
}
$runtime = $target[$libraryKey]['runtime']
if ($null -eq $runtime -or $runtime.Count -ne 1 -or -not $runtime.Contains("$pluginName.dll")) {
    throw 'Host assemblies or other runtime assets must not be bundled.'
}
$expectedTag = "v$version"
if ([string]::IsNullOrWhiteSpace($ReleaseTag)) { $ReleaseTag = $expectedTag }
if ($ReleaseTag -cnotin @($expectedTag, "$expectedTag-rename")) {
    throw "Expected release tag: $expectedTag or $expectedTag-rename"
}

$files = @("$pluginName.dll", "$pluginName.json", "$pluginName.deps.json")
$artifacts = Join-Path $repoRoot 'artifacts'
$packageDirectory = Join-Path $artifacts $pluginName
[IO.Directory]::CreateDirectory($packageDirectory) | Out-Null
foreach ($item in Get-ChildItem -LiteralPath $packageDirectory -Force) {
    if ($item.PSIsContainer -or $item.Name -cnotin $files) {
        throw "Unexpected file in staging directory: $($item.Name)"
    }
}
foreach ($name in $files) {
    Copy-Item -LiteralPath (Join-Path $InputDirectory $name) -Destination (Join-Path $packageDirectory $name) -Force
}
$zipPath = Join-Path $artifacts 'latest.zip'
$fileStream = [IO.File]::Open($zipPath, [IO.FileMode]::Create)
$zip = [IO.Compression.ZipArchive]::new($fileStream, [IO.Compression.ZipArchiveMode]::Create)
try {
    foreach ($name in ($files | Sort-Object -CaseSensitive)) {
        $entry = $zip.CreateEntry($name, [IO.Compression.CompressionLevel]::Optimal)
        $entry.LastWriteTime = [DateTimeOffset]::new(2000, 1, 1, 0, 0, 0, [TimeSpan]::Zero)
        $source = [IO.File]::OpenRead((Join-Path $packageDirectory $name))
        $destination = $entry.Open()
        try { $source.CopyTo($destination) } finally { $destination.Dispose(); $source.Dispose() }
    }
} finally { $zip.Dispose(); $fileStream.Dispose() }
$zip = [IO.Compression.ZipFile]::OpenRead($zipPath)
try {
    if ($zip.Entries.Count -ne 3) { throw 'The ZIP must contain exactly three root files.' }
    foreach ($entry in $zip.Entries) {
        if ($entry.FullName -cnotin $files) { throw "Unexpected ZIP entry: $($entry.FullName)" }
        $source = $entry.Open()
        try { $hash = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($source)) } finally { $source.Dispose() }
        if ($hash -cne (Get-FileHash -LiteralPath (Join-Path $InputDirectory $entry.FullName) -Algorithm SHA256).Hash) {
            throw "ZIP entry differs from its build input: $($entry.FullName)"
        }
    }
} finally { $zip.Dispose() }
$repoEntry = [ordered]@{}
foreach ($key in $manifest.Keys) { $repoEntry[$key] = $manifest[$key] }
$download = "https://github.com/$Repository/releases/download/$ReleaseTag/latest.zip"
$repoEntry['DownloadLinkInstall'] = $download
$repoEntry['DownloadLinkUpdate'] = $download
$repoEntry['DownloadLinkTesting'] = $download
$repoEntry['DownloadCount'] = 0
$repoEntry['LastUpdate'] = [DateTimeOffset]::UtcNow.ToUnixTimeSeconds()
$utf8 = [Text.UTF8Encoding]::new($false)
$repoPath = Join-Path $artifacts 'repo.json'
[IO.File]::WriteAllText($repoPath, (ConvertTo-Json -InputObject @($repoEntry) -Depth 32) + "`n", $utf8)
$checksumPath = Join-Path $artifacts 'SHA256SUMS'
$lines = foreach ($name in @('latest.zip', 'repo.json')) {
    '{0}  {1}' -f (Get-FileHash -LiteralPath (Join-Path $artifacts $name) -Algorithm SHA256).Hash.ToLowerInvariant(), $name
}
[IO.File]::WriteAllText($checksumPath, ($lines -join "`n") + "`n", $utf8)
Write-Host "Validated $displayName $version (internal name: $pluginName); packaged three files without host assemblies."
[pscustomobject]@{ Version = $version; ReleaseTag = $ReleaseTag; ZipPath = $zipPath; RepositoryPath = $repoPath; ChecksumPath = $checksumPath }
