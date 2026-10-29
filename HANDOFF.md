# Handoff - es-mcp

Written 2026-10-29. Working directory: `E:\dev\vibe\es-cli`. Repo `izicoder/es-mcp`.
`main` pushed and clean; **release `v2.3.0` published**, both workflows green.

## Goal

Make `es-mcp` (MCP stdio server over the voidtools Everything SDK, C#/.NET 10, Windows) cheap in
tokens, fast to start, and installable in one command. Publish it so it gets users.

## Done

- **v2.1.0** (`2043025` and earlier): compact default replies (`path, folder, size, modified`),
  `format` json/text/full, benchmark harness (`tools/compare.mjs`, `tools/charts.mjs`) and charts.
- **v2.2.0 SDK DLL bundled** (`00cc7af`): `third_party/Everything64.dll` committed and copied next
  to `es-mcp.exe` via a `<Content>` item, `.gitignore` exception, `third_party/NOTICE.md` records
  origin, SHA256 and the removal path.
- **winget manifests** (`763d5ed`): `tools/winget-manifest.ps1` fetches the real asset, cross-checks
  `SHA256SUMS.txt`, writes three manifests to `out/winget/<version>`, prints the submit command.
- **v2.3.0 `.mcpb` bundle** (`5b57f82`, tag `v2.3.0`): `tools/mcpb.ps1 -Version X` publishes
  self-contained, writes `manifest.json` (spec 0.3, `server.type` binary, `platforms win32`), and
  zips manifest plus `server/es-mcp.exe` and `server/Everything64.dll` into `out/mcpb/es-mcp-X.mcpb`
  with forward-slash entry names. `release.yml` builds it from the self-contained publish, attaches
  it to the tag and adds it to `SHA256SUMS.txt`. README now leads with the bundle.
- **Release assets for v2.3.0**, all present and hash-verified against `SHA256SUMS.txt`:
  `es-mcp-2.3.0.mcpb` (33,086,365 B, sha256 `879bf1a2e763176f241204c8d22b192cac8ee795fcd873d7eee7160bf82410f0`),
  `es-mcp-win-x64-selfcontained.zip` (33,085,394 B), `es-mcp-win-x64.zip` (153,154 B).
- **MCP registry manifest** (`tools/server-json.ps1`, `server.json`): reads the release `.mcpb`,
  cross-checks `SHA256SUMS.txt`, pulls the repo id from `gh`, writes `server.json`. Validates against
  the registry's `2025-09-29` schema.
- **Verified 2026-10-29**: 87 tests pass, build 0 warnings, `dotnet format` clean. `mcpb validate`
  passes. The release `.mcpb` was downloaded, unpacked and driven over stdio: `initialize` returns
  `es-mcp 2.3.0`, `everything_status` reports SDK 1.5.0.1423 loaded from the bundle's own
  `server\Everything64.dll` with 1,875,930 objects indexed. `winget validate` passes on the
  regenerated 2.3.0 manifests.

## In progress

- winget-pkgs PR <https://github.com/microsoft/winget-pkgs/pull/463573> is still **open for 2.2.0**,
  so it is now stale. `out/winget/2.3.0` holds validated 2.3.0 manifests; updating means pushing
  them over the fork branch (or `wingetcreate update`). Until it merges, keep
  `winget install izicoder.es-mcp` out of the README.

## Next steps

1. MCP registry publish, one command each: `mcp-publisher login github`, then `mcp-publisher publish`
   from the repo root. `server.json` is ready and already points at the released bundle.
2. Bump the winget PR to 2.3.0, then add `winget install izicoder.es-mcp` to the README once merged.
3. Test the real one-click path on a machine with Claude Desktop (not installed here): double-click
   the `.mcpb`, accept the dialog, confirm the server starts with no manual config edit.
4. Directory listings: awesome-mcp-servers, glama, mcp.so, pulsemcp.
5. Launch posts: voidtools forum, r/mcp, Show HN, dev.to "Why I skipped es.exe".
6. Check the GitHub repo description and topics; stale `izicoder/es-cli` pages were still indexed.

## Decisions and constraints

- No NuGet: the audience does not have .NET, nobody discovers MCP servers there. winget installs.
- The bundle is always the self-contained build. A framework-dependent `.mcpb` installs cleanly and
  then dies on a machine without .NET 10, which is worse than a 32 MB download.
- The DLL is redistributed unmodified; the SDK zip has no licence file and exists to be built
  against. Revert path is `NOTICE.md`. Users can still pin `EVERYTHING_SDK_PATH`.
- Lead with cold start and token cost; warm search is a tie with the ctypes competitor.
- Never commit `Everything64.dll` outside `third_party/`.

## Gotchas

- `<None>` + `CopyToPublishDirectory` does **not** reach a `PublishSingleFile` output. Use `<Content>`.
- `winget validate` rejects `InstallerSize` and `Scope: user` for `InstallerType: zip`.
- New winget packages need a moderator; a merged version cannot be resubmitted, bump instead.
- `ZipFile.CreateFromDirectory` writes `server\es-mcp.exe` on Windows. `tools/mcpb.ps1` adds entries
  by hand with `/` so unzippers do not create one file with a backslash in its name.
- A `.mcpb` built locally and the one from the release have different hashes (zip timestamps). The
  release asset is the one the registry pins.
- `Expand-Archive` refuses a `.mcpb` extension. Use `[IO.Compression.ZipFile]::ExtractToDirectory`.
- `Compress-Archive out/es-mcp/*` fails while the server is running from `out/es-mcp`; stop it first.
- The running MCP server in a session locks `out/es-mcp/es-mcp.exe`. Publish to another folder
  (`-o out/verify`) rather than killing the server that the session is using.
- No `pwsh` here, only Windows PowerShell 5.1: scripts write UTF-8 without BOM by hand and avoid
  `??`/ternaries.
- `.gitattributes` forces CRLF for `*.cs`/`*.csproj`; LF from the editor fails `dotnet format`.
- `ajv-cli` needs `--strict=false` on the registry schema; it uses the non-standard `example` keyword.

## Open questions

- Launch posts: who owns them, and when? Say the word and the directory listings start.
- Push happens on request only.

## Resume commands

```powershell
cd E:\dev\vibe\es-cli
git pull
dotnet build EsMcp.slnx -c Release; dotnet test EsMcp.slnx -c Release
dotnet format EsMcp.slnx --verify-no-changes
powershell -File tools\mcpb.ps1 -Version 2.3.0
npx --yes @anthropic-ai/mcpb validate out\mcpb\stage\manifest.json
powershell -File tools\winget-manifest.ps1 -Version 2.3.0
winget validate --manifest out\winget\2.3.0
powershell -File tools\server-json.ps1 -Version 2.3.0
```
