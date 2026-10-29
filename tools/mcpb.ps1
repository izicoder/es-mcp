# Builds the .mcpb bundle: a zip holding manifest.json plus the server in server/.
#
# A .mcpb is what Claude Desktop and other MCPB-aware apps open for one-click install. The spec
# wants a self-contained binary - a framework-dependent exe would install fine and then fail on a
# machine without .NET 10 - so this publishes self-contained by default.
#
#   powershell -File tools\mcpb.ps1 -Version 2.2.0
#   powershell -File tools\mcpb.ps1 -Version 2.2.0 -SourceDir out\es-mcp-selfcontained
#
# The output lands in out/mcpb, which is gitignored. The release workflow attaches it to the tag and
# adds it to SHA256SUMS.txt.

[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$Version,
    [string]$Repo = 'izicoder/es-mcp',
    # Existing publish folder to pack. Omit to publish a fresh self-contained build.
    [string]$SourceDir,
    [string]$OutDir,
    [string]$OutFile,
    # Publish first even when -SourceDir was given.
    [switch]$Publish
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

function Write-Utf8([string]$Path, [string]$Text) {
    [IO.File]::WriteAllText($Path, ($Text -replace "`r`n", "`n"), (New-Object Text.UTF8Encoding($false)))
}

if ($Version -notmatch '^\d+\.\d+\.\d+$') { throw "Version must look like 2.2.0, got '$Version'" }

$root = Resolve-Path (Join-Path $PSScriptRoot '..')
if (-not $OutDir) { $OutDir = Join-Path $root 'out\mcpb' }
if (-not $OutFile) { $OutFile = Join-Path $OutDir "es-mcp-$Version.mcpb" }

$repoUrl = "https://github.com/$Repo"

if ($Publish -or -not $SourceDir) {
    $SourceDir = Join-Path $OutDir 'publish'
    Write-Host "Publishing self-contained to $SourceDir"
    & dotnet publish (Join-Path $root 'src\EsMcp') -c Release -o $SourceDir --nologo `
        -p:SelfContained=true -p:DebugType=none -p:DebugSymbols=false -p:GenerateDocumentationFile=false
    if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed with exit code $LASTEXITCODE" }
}

$SourceDir = (Resolve-Path $SourceDir).Path
foreach ($required in @('es-mcp.exe', 'Everything64.dll')) {
    if (-not (Test-Path (Join-Path $SourceDir $required))) {
        throw "$SourceDir is missing $required"
    }
}

# Straight quotes only: the manifest is JSON, not markdown.
$longDescription = @'
Search every file on this Windows machine in milliseconds, without walking directories.

es-mcp is a local MCP server for the voidtools Everything index. It answers "where is X" with full
paths, sizes and dates, and its replies are compact enough to keep an agent's context small.

Requires Everything (https://www.voidtools.com) installed and running in the background. The
Everything SDK DLL is included, so nothing else needs downloading.
'@

# Single-quoted here-string: ${__dirname} must reach the manifest untouched.
$manifest = @'
{
  "manifest_version": "0.3",
  "name": "es-mcp",
  "display_name": "Everything Search",
  "version": "__VERSION__",
  "description": "Instant file search on Windows, backed by the Everything index.",
  "long_description": "__LONG__",
  "author": {
    "name": "izicoder",
    "url": "https://github.com/izicoder"
  },
  "repository": {
    "type": "git",
    "url": "__REPO__"
  },
  "homepage": "__REPO__",
  "documentation": "__REPO__#readme",
  "support": "__REPO__/issues",
  "license": "MIT",
  "keywords": ["everything", "file-search", "mcp", "search", "voidtools", "windows"],
  "server": {
    "type": "binary",
    "entry_point": "server/es-mcp.exe",
    "mcp_config": {
      "command": "${__dirname}/server/es-mcp.exe",
      "env": {}
    }
  },
  "tools": [
    {
      "name": "search_files",
      "description": "Find any file or folder on this Windows machine instantly - full paths, sizes and dates from the Everything index. Supports Everything query syntax: ext:cs, path:src, dm:today, size:>1gb, wildcards and regex:true."
    },
    {
      "name": "everything_status",
      "description": "Check that Everything search is usable: reports whether the SDK loaded, whether Everything is running, and how many objects are indexed."
    }
  ],
  "tools_generated": false,
  "compatibility": {
    "platforms": ["win32"]
  }
}
'@

$longJson = ($longDescription -replace '\\', '\\' -replace '"', '\"' -replace "`r`n", '\n' -replace "`n", '\n')
$manifest = $manifest.Replace('__VERSION__', $Version).Replace('__REPO__', $repoUrl).Replace('__LONG__', $longJson)

# Parse it back: a typo in the here-string should fail here, not in Claude Desktop.
$parsed = $manifest | ConvertFrom-Json
if ($parsed.version -ne $Version) { throw "manifest version came out as $($parsed.version)" }

$stage = Join-Path $OutDir 'stage'
if (Test-Path $stage) { Remove-Item $stage -Recurse -Force }
$serverDir = Join-Path $stage 'server'
New-Item -ItemType Directory -Force -Path $serverDir | Out-Null

Get-ChildItem $SourceDir -File | Where-Object { $_.Extension -notin @('.pdb', '.xml') } |
    Copy-Item -Destination $serverDir
Write-Utf8 (Join-Path $stage 'manifest.json') $manifest

if (Test-Path $OutFile) { Remove-Item $OutFile -Force }
New-Item -ItemType Directory -Force -Path (Split-Path $OutFile) | Out-Null
Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem

# Entries are written by hand with forward slashes. CreateFromDirectory uses backslashes on Windows,
# which some unzippers take literally and turn into a file called "server\es-mcp.exe".
$zip = [IO.Compression.ZipFile]::Open($OutFile, [IO.Compression.ZipArchiveMode]::Create)
try {
    $files = @(Get-Item (Join-Path $stage 'manifest.json'))
    $files += @(Get-ChildItem $serverDir -File)
    foreach ($file in $files) {
        $relative = $file.FullName.Substring($stage.Length + 1).Replace('\', '/')
        [IO.Compression.ZipFileExtensions]::CreateEntryFromFile(
            $zip, $file.FullName, $relative, [IO.Compression.CompressionLevel]::Optimal) | Out-Null
    }
} finally {
    $zip.Dispose()
}

# Read the archive back: the bundle is only worth shipping if it unpacks to the layout MCPB expects.
$zip = [IO.Compression.ZipFile]::OpenRead($OutFile)
try {
    $names = @($zip.Entries | ForEach-Object { $_.FullName })
} finally {
    $zip.Dispose()
}
if ($names | Where-Object { $_ -match '\\' }) { throw "$OutFile has backslash entry names: $($names -join ', ')" }
foreach ($expected in @('manifest.json', 'server/es-mcp.exe', 'server/Everything64.dll')) {
    if ($names -notcontains $expected) { throw "$OutFile has no $expected (has: $($names -join ', '))" }
}

$hash = (Get-FileHash $OutFile -Algorithm SHA256).Hash.ToLowerInvariant()
$size = (Get-Item $OutFile).Length

Write-Host ""
Write-Host "Wrote $OutFile"
Write-Host ("  {0} bytes  sha256 {1}" -f $size, $hash)
Write-Host ("  {0} files, manifest_version {1}" -f $names.Count, $parsed.manifest_version)
Write-Host ""
Write-Host "Test it: unpack and run server\es-mcp.exe by hand, or double-click the .mcpb in Claude Desktop."
Write-Host "Publish to the MCP registry (https://registry.modelcontextprotocol.io), once the .mcpb is a release asset:"
Write-Host "  mcp-publisher login github"
Write-Host "  server.json packages[0]: registryType mcpb, identifier"
Write-Host "    $repoUrl/releases/download/v$Version/es-mcp-$Version.mcpb"
Write-Host "    fileSha256 $hash"
