# Handoff - es-mcp

Written 2026-10-29. Working directory: `E:\dev\vibe\es-cli`. Repo `izicoder/es-mcp`.
`main` at `cb21d44` plus the local `.mcpb` merge; **not pushed**. Release `v2.2.0` published.

## Goal

Make `es-mcp` (MCP stdio server over the voidtools Everything SDK, C#/.NET 10, Windows) cheap in
tokens, fast to start, and installable in one command. Publish it so it gets users.

## Done

- **v2.1.0** (`2043025` and earlier): compact default replies (`path, folder, size, modified`),
  `format` json/text/full, benchmark harness (`tools/compare.mjs`, `tools/charts.mjs`) and charts.
- **v2.2.0 SDK DLL bundled** (`00cc7af`, merged `11a31bc`): `third_party/Everything64.dll` committed
  and copied next to `es-mcp.exe` via a `<Content>` item, `.gitignore` exception,
  `third_party/NOTICE.md` records origin, SHA256 and the removal path.
- **Release assets** (tag `v2.2.0` pushed, workflow green): `es-mcp-win-x64.zip` (153 KB, needs
  .NET 10), `es-mcp-win-x64-selfcontained.zip` (33 MB), `SHA256SUMS.txt`. The workflow fails if the
  DLL is missing from either publish folder.
- **winget manifests** (`763d5ed`, merged `cb21d44`): `tools/winget-manifest.ps1` fetches the real
  asset, cross-checks `SHA256SUMS.txt`, writes three manifests to `out/winget/<version>`, prints the
  `submit` command for a first version and `update` for later ones. `winget validate` passes.
- **`.mcpb` bundle** (2026-10-29, branch `feat/mcpb-bundle`): `tools/mcpb.ps1 -Version X` publishes
  self-contained, writes `manifest.json` (spec 0.3, `server.type` binary, `compatibility.platforms`
  `win32`), zips the two files plus the manifest into `out/mcpb/es-mcp-X.mcpb` (~31.9 MB), then
  reopens the archive and fails unless `manifest.json`, `server/es-mcp.exe` and
  `server/Everything64.dll` are all present with forward-slash entry names. `release.yml` builds it
  from the self-contained publish, copies it to the job root, adds it to `SHA256SUMS.txt` and
  attaches it to the tag. The printed tail of the script carries the `server.json` values
  (`registryType mcpb`, asset URL, `fileSha256`).
- **Verified 2026-10-29**: 87 tests pass, build 0 warnings. `mcpb validate` on the generated
  manifest: "Manifest schema validation passes". Bundle unpacked into a temp folder and driven over
  stdio: `initialize` returns `es-mcp 2.2.0`, `tools/list` returns `search_files` and
  `everything_status`, and `everything_status` reports the SDK loaded from the bundle's own
  `server\Everything64.dll` with 1.87M objects indexed. The release job's version-from-tag and
  checksum blocks were run locally as a simulation.

## In progress

- winget-pkgs PR <https://github.com/microsoft/winget-pkgs/pull/463573>: CLA signed, checks green,
  `mergeable: clean`, `REVIEW_REQUIRED`. Waiting on a moderator; nothing to do but wait.

## Next steps

1. Push `main` (local commits only so far), then cut the next release (2.3.0) so the `.mcpb` and its
   checksum line actually exist as assets.
2. After that release: add the `.mcpb` row and `winget install izicoder.es-mcp` to the README Install
   section. Both are deliberately missing today so nobody runs a command or opens a file that 404s.
3. MCP registry: `mcp-publisher login github`, publish `server.json` with
   `registryType: mcpb`, the release asset URL (must contain "mcp" - ours does) and the
   `fileSha256` the build prints. Free, no review queue. Name must be `io.github.izicoder/es-mcp`.
4. Test the real one-click path on a machine with Claude Desktop (not installed here): double-click
   the `.mcpb`, accept the install dialog, check the server starts without a manual config edit.
5. Directory listings: awesome-mcp-servers, glama, mcp.so, pulsemcp.
6. Launch posts: voidtools forum, r/mcp, Show HN, dev.to "Why I skipped es.exe".
7. Check the GitHub repo description and topics; stale `izicoder/es-cli` pages were still indexed.

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
- `Expand-Archive` refuses a `.mcpb` extension. Use `[IO.Compression.ZipFile]::ExtractToDirectory`.
- `Compress-Archive out/es-mcp/*` fails while the server is running from `out/es-mcp`; stop it first.
- No `pwsh` here, only Windows PowerShell 5.1: scripts write UTF-8 without BOM by hand and avoid
  `??`/ternaries.
- `.gitattributes` forces CRLF for `*.cs`/`*.csproj`; LF from the editor fails `dotnet format`.
- Stop `es-mcp.exe` before rebuilding: it locks `out/es-mcp/es-mcp.exe`.

## Open questions

- Launch posts: who owns them, and when? Say the word and the directory listings start.
- Push happens on request only.

## Resume commands

```powershell
cd E:\dev\vibe\es-cli
git pull
dotnet build EsMcp.slnx -c Release; dotnet test EsMcp.slnx -c Release
dotnet format EsMcp.slnx --verify-no-changes
powershell -File tools\mcpb.ps1 -Version 2.2.0
npx --yes @anthropic-ai/mcpb validate out\mcpb\stage\manifest.json
powershell -File tools\winget-manifest.ps1 -Version 2.2.0
winget validate --manifest out\winget\2.2.0
```
