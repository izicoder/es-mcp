# Changelog

All notable changes to this project are documented here. This project follows
[Semantic Versioning](https://semver.org/) and [Keep a Changelog](https://keepachangelog.com/en/1.1.0/).

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
