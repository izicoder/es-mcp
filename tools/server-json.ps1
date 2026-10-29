# Generates server.json for the MCP registry from a published release.
#
# The registry pins the bundle by sha256, so this reads the real release asset and cross-checks
# SHA256SUMS.txt instead of trusting a hand-typed number. The output is server.json in the repo
# root, which is committed: `mcp-publisher publish` reads it from there.
#
#   powershell -File tools\server-json.ps1 -Version 2.3.0
#   powershell -File tools\server-json.ps1 -Version 2.3.0 -Bundle .\es-mcp-2.3.0.mcpb
#
# Then, once:
#   mcp-publisher login github
#   mcp-publisher publish

[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$Version,
    [string]$Repo = 'izicoder/es-mcp',
    [string]$Bundle,
    [string]$OutFile
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

if ($Version -notmatch '^\d+\.\d+\.\d+$') { throw "Version must look like 2.3.0, got '$Version'" }

$tag = "v$Version"
$asset = "es-mcp-$Version.mcpb"
$url = "https://github.com/$Repo/releases/download/$tag/$asset"
if (-not $OutFile) { $OutFile = Join-Path $PSScriptRoot '..\server.json' }

$expected = $null
if (-not $Bundle) {
    $Bundle = Join-Path ([IO.Path]::GetTempPath()) "$tag-$asset"
    Get-ReleaseFile $url $Bundle

    # The release publishes SHA256SUMS.txt; refuse to write a manifest that disagrees with it.
    $sumFile = Join-Path ([IO.Path]::GetTempPath()) "$tag-SHA256SUMS.txt"
    Get-ReleaseFile "https://github.com/$Repo/releases/download/$tag/SHA256SUMS.txt" $sumFile
    $line = Get-Content $sumFile | Where-Object { $_ -match [regex]::Escape($asset) } | Select-Object -First 1
    if (-not $line) { throw "SHA256SUMS.txt has no entry for $asset" }
    $expected = ($line -split '\s+')[0]
}

$hash = (Get-FileHash $Bundle -Algorithm SHA256).Hash.ToLowerInvariant()
if ($expected -and $hash -ne $expected.ToLowerInvariant()) {
    throw "Hash mismatch: file is $hash, SHA256SUMS.txt says $expected"
}

# The registry wants the forge's own repository id, which survives renames.
$repoId = (& gh api "repos/$Repo" --jq '.id') 2>$null
if (-not $repoId) { throw "Could not read the repository id from gh; is the CLI logged in?" }

$json = @"
{
  "`$schema": "https://static.modelcontextprotocol.io/schemas/2025-09-29/server.schema.json",
  "name": "io.github.izicoder/es-mcp",
  "description": "Instant file search on Windows through Everything, for AI agents.",
  "version": "$Version",
  "websiteUrl": "https://github.com/$Repo",
  "repository": {
    "url": "https://github.com/$Repo",
    "source": "github",
    "id": "$($repoId.Trim())"
  },
  "packages": [
    {
      "registryType": "mcpb",
      "identifier": "$url",
      "version": "$Version",
      "fileSha256": "$hash",
      "transport": {
        "type": "stdio"
      }
    }
  ]
}
"@

Write-Utf8 $OutFile $json

Write-Host ""
Write-Host "Wrote $OutFile"
Write-Host "  $asset  $hash"
Write-Host ""
Write-Host "Publish it (once):"
Write-Host "  mcp-publisher login github"
Write-Host "  mcp-publisher publish"
