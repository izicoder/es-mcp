# Changelog

All notable changes to this project are documented here. This project follows
[Semantic Versioning](https://semver.org/) and [Keep a Changelog](https://keepachangelog.com/en/1.1.0/).

## [2.2.0] - 2026-10-28

Installs without hunting for the SDK DLL, and ships the artefacts winget needs.

### Added

- **`Everything64.dll` ships with the server.** It sits next to `es-mcp.exe` in every download, so a
  fresh install searches immediately instead of failing with a missing-library error. Anyone with a
  copy of their own can still pin it with `EVERYTHING_SDK_PATH`. See `third_party/NOTICE.md`.
- **A self-contained zip.** `es-mcp-win-x64-selfcontained.zip` (~32 MB) needs no .NET runtime, so it
  is the download for anyone who does not already have .NET 10. The framework-dependent zip stays
  under 1 MB.
- `SHA256SUMS.txt` in every release.
- `tools/winget-manifest.ps1`: builds the three winget manifests from the published asset and its
  checksum, so the sizes and hashes are never hand-typed. The output passes `winget validate`.

### Changed

- The missing-DLL message now says the DLL ships with the release, and treats
  `EVERYTHING_SDK_PATH` and an Everything install as fallbacks rather than the expected setup.
- Release zips no longer contain `.pdb` files or the XML doc file.

## [2.1.0] - 2026-10-28

Cheaper replies, and the numbers to prove it against the alternatives.

### Changed

- **Default replies are 65% smaller.** Rows now carry `path`, `folder`, `size` and `modified` only.
  `name` and `ext` were already inside `path`; `created` and `accessed` were rarely used and cost
  about 70 characters per row. Folders omit `size` instead of sending `null`.
- `modified` is `yyyy-MM-dd HH:mm` local time instead of a full ISO 8601 round-trip timestamp.
  Still sortable, a third of the characters.
- The text block of a default reply is a one line summary plus the row count. The rows live in
  `structuredContent`, so sending them twice was the single most expensive thing this server did.
- `SearchOptions.Fields` in `EsSdk` selects which per-result fields to fetch. Fewer fields means
  fewer native calls per row: the default reply no longer asks Everything for created/accessed.

### Added

- `format` on `search_files`: `json` (default, above), `text` (the table only, for clients that do
  not read structured content) and `full` (every field the SDK reports, including `name`, `ext`,
  `created` and `accessed`). `details:true` is an alias for `full`.
- `tools/compare.mjs`: measures cold start, time to first result, warm p50/p95 and payload size
  against other MCP file-search servers on the same machine and index.
- `tools/charts.mjs` and the two charts in `docs/`, generated from `tools/bench-results.json`.

## [2.0.0] - 2026-10-28

The project is now an MCP server only, aimed at AI agents.

### Removed

- **`es-cli` and `EsCli.Tests` are gone.** The command-line front end is no longer built or shipped.
  This is the breaking change behind the major version bump.
- `src/EsCli`, `tests/EsCli.Tests`, the `es-cli` publish step and the `EsCli.sln` solution.

### Added

- `structuredContent` and `outputSchema` for `search_files`: `total`, `returned`, `offset`, `hasMore`,
  `nextOffset` and a `results` array with `path`, `name`, `folder`, `ext`, `size`, `modified`, `created`
  and `accessed`.
- `everything_status` tool: reports SDK load state, whether Everything is running, indexed object count,
  Everything version and the resolved DLL path. Marked `isError` when search is not usable.
- Created and accessed timestamps on every result (SDK request flags `0x20` and `0x80`).
- MCP protocol version negotiation across 2024-11-05, 2025-03-26, 2025-06-18 and 2025-11-25
  (newest by default).
- Diagnostics on stderr, keeping stdout protocol-clean.
- Tests for the JSON-RPC loop: initialize negotiation, tools/list, tools/call, paging, errors,
  notifications and parse failures.
- Contract tests that keep the structured payload and the advertised output schemas in sync.
- `CHANGELOG.md`, a tag-triggered release workflow, and `dotnet format --verify-no-changes` in CI.
- Benchmark instructions in the README.

### Changed

- `EsCli.sln` replaced by `EsMcp.slnx`.
- The repository moved to `github.com/izicoder/es-mcp`.
- `tools/bench.mjs` reads `structuredContent.total` instead of parsing the text header.
- `EsSdk` exposes `Everything.GetStatus()` and `EverythingStatus`; `FileEntry` gained optional
  `DateCreated` and `DateAccessed`.

## [1.3.0] - 2026-10-27

### Added

- `type` / `type_desc` sort orders in the CLI and the MCP tool.
- `--limit=10` style values and `--` as an end-of-options marker.
- UTF-8 console output in the CLI.
- A shared, actionable message when `Everything64.dll` is missing or the wrong architecture.

## [1.2.0]

### Added

- The `search_files` MCP tool name; `search` kept as a call-time alias.
- Self-documenting tool description, server instructions, annotations and schema examples.
- Guidance for empty results, offset past the end, `folder:` misuse and silent regex failures.

## [1.1.0]

### Added

- MCP server (`es-mcp`) over the Everything SDK.

## [1.0.0]

### Added

- Initial `es-cli` and `EsSdk`.
