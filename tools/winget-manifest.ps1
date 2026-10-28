# Generates the winget manifests for a published release.
#
# winget needs the exact size and sha256 of the installer it will download, so this reads them
# from the real release asset instead of trusting a hand-typed number. Nothing here is committed:
# the output lands in out/winget, which is gitignored.
#
#   powershell -File tools\winget-manifest.ps1 -Version 2.2.0
#   powershell -File tools\winget-manifest.ps1 -Version 2.2.0 -Zip .\es-mcp-win-x64-selfcontained.zip
#
# Then submit the three files to https://github.com/microsoft/winget-pkgs under
# manifests/i/izicoder/es-mcp/<version>/ (see the printed instructions).

[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$Version,
    [string]$Repo = 'izicoder/es-mcp',
    [string]$Zip,
    [string]$OutDir
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

function Write-Utf8([string]$Path, [string]$Text) {
    [IO.File]::WriteAllText($Path, ($Text -replace "`r`n", "`n"), (New-Object Text.UTF8Encoding($false)))
}

function Get-ReleaseFile([string]$Uri, [string]$OutFile) {
    Write-Host "Downloading $Uri"
    Invoke-WebRequest -Uri $Uri -OutFile $OutFile -UseBasicParsing
}

if ($Version -notmatch '^\d+\.\d+\.\d+$') { throw "Version must look like 2.2.0, got '$Version'" }

$tag = "v$Version"
$asset = 'es-mcp-win-x64-selfcontained.zip'
$url = "https://github.com/$Repo/releases/download/$tag/$asset"
if (-not $OutDir) { $OutDir = Join-Path $PSScriptRoot "..\out\winget\$Version" }

$expected = $null
if (-not $Zip) {
    $Zip = Join-Path ([IO.Path]::GetTempPath()) "$tag-$asset"
    Get-ReleaseFile $url $Zip

    # The release publishes SHA256SUMS.txt; refuse to write a manifest that disagrees with it.
    $sumFile = Join-Path ([IO.Path]::GetTempPath()) "$tag-SHA256SUMS.txt"
    Get-ReleaseFile "https://github.com/$Repo/releases/download/$tag/SHA256SUMS.txt" $sumFile
    $sums = Get-Content $sumFile
    $line = $sums | Where-Object { $_ -match [regex]::Escape($asset) } | Select-Object -First 1
    if (-not $line) { throw "SHA256SUMS.txt has no entry for $asset" }
    $expected = ($line -split '\s+')[0]
}

$hash = (Get-FileHash $Zip -Algorithm SHA256).Hash.ToLowerInvariant()
if ($expected -and $hash -ne $expected.ToLowerInvariant()) {
    throw "Hash mismatch: file is $hash, SHA256SUMS.txt says $expected"
}
$size = (Get-Item $Zip).Length

New-Item -ItemType Directory -Force -Path $OutDir | Out-Null

$header = @"
# yaml-language-server: `$schema=https://aka.ms/winget-manifest.{0}.1.10.0.schema.json
PackageIdentifier: izicoder.es-mcp
PackageVersion: $Version
"@

Write-Utf8 (Join-Path $OutDir 'izicoder.es-mcp.yaml') (@"
$($header -f 'version')
DefaultLocale: en-US
ManifestType: version
ManifestVersion: 1.10.0
"@)

Write-Utf8 (Join-Path $OutDir 'izicoder.es-mcp.installer.yaml') (@"
$($header -f 'installer')
Platform:
  - Windows.Desktop
MinimumOSVersion: 10.0.0.0
InstallerType: zip
NestedInstallerType: portable
NestedInstallerFiles:
  - RelativeFilePath: es-mcp.exe
    PortableCommandAlias: es-mcp
InstallModes:
  - interactive
  - silent
  - silentWithProgress
UpgradeBehavior: install
Installers:
  - Architecture: x64
    InstallerUrl: $url
    InstallerSha256: $($hash.ToUpperInvariant())
ManifestType: installer
ManifestVersion: 1.10.0
"@)

Write-Utf8 (Join-Path $OutDir 'izicoder.es-mcp.locale.en-US.yaml') (@"
$($header -f 'defaultLocale')
PackageLocale: en-US
Publisher: izicoder
PublisherUrl: https://github.com/izicoder
PublisherSupportUrl: https://github.com/$Repo/issues
PackageName: es-mcp
PackageUrl: https://github.com/$Repo
License: MIT
LicenseUrl: https://github.com/$Repo/blob/main/LICENSE
Copyright: Copyright (c) 2026 izicoder
ShortDescription: Everything search for AI agents, over MCP.
Description: An MCP stdio server for voidtools Everything. Gives an AI agent instant, indexed search over every file on the machine, with compact replies that cost about 1,100 tokens for 30 rows. Everything must be installed and running; the SDK DLL ships with this package.
Moniker: es-mcp
Tags:
  - everything
  - file-search
  - mcp
  - search
  - voidtools
ReleaseNotesUrl: https://github.com/$Repo/releases/tag/$tag
ManifestType: defaultLocale
ManifestVersion: 1.10.0
"@)

Write-Host ""
Write-Host "Wrote 3 manifests to $OutDir"
Write-Host "  $asset  $size bytes  sha256 $hash"
Write-Host ""
Write-Host "Submit them (one time setup: fork winget-pkgs, accept the CLA on the first PR):"
Write-Host "  wingetcreate submit --token <github-token> $OutDir"
Write-Host "or copy the files to manifests/i/izicoder/es-mcp/$Version/ in your fork and open a PR."
