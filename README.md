# es-mcp

[![build](https://github.com/izicoder/es-mcp/actions/workflows/build.yml/badge.svg)](https://github.com/izicoder/es-mcp/actions/workflows/build.yml)
[![license](https://img.shields.io/badge/license-MIT-blue.svg)](LICENSE)
[![.NET](https://img.shields.io/badge/.NET-10.0-512bd4.svg)](https://dotnet.microsoft.com/download)

Instant file search for Windows, built for AI agents.

`es-mcp` is a [Model Context Protocol](https://modelcontextprotocol.io) server (stdio) that exposes the
[voidtools Everything SDK](https://www.voidtools.com/support/everything/sdk/) as tools. Queries run against
Everything's in-memory index, so lookups across millions of files return in milliseconds — no directory
walking, no indexing of your own.

It ships two tools:

| Tool | What it does |
| --- | --- |
| `search_files` | Find files and folders by name, extension, size or date. Full paths, sizes and timestamps. |
| `everything_status` | Say whether Everything is running and how large its index is, before searching. |

## Requirements

- Windows x64
- [.NET 10 runtime](https://dotnet.microsoft.com/download) (or build self-contained, see below)
- [Everything](https://www.voidtools.com/) installed and **running in the background**
- `Everything64.dll`, the SDK shipped with Everything, reachable in one of these ways:
  1. `EVERYTHING_SDK_PATH` set to the DLL or to the folder containing it
  2. sitting next to `es-mcp.exe`
  3. the Everything install folder (found automatically, e.g. `C:\Program Files\Everything\`)

The DLL is deliberately **not** bundled here — grab it from your own Everything install.

## Build

```console
dotnet build
dotnet test
dotnet publish src/EsMcp -c Release -o out/es-mcp
```

`dotnet publish` produces a framework-dependent single-file executable. For a machine without .NET installed,
add `-p:SelfContained=true`.

## Register with an MCP client

`dotnet publish` writes `out/es-mcp/es-mcp.exe`. Copy it wherever you keep tools, then point your client at it.
For opencode, in `~/.config/opencode/opencode.json`:

```json
{
  "mcp": {
    "servers": {
      "everything": {
        "type": "local",
        "command": ["C:\\tools\\es-mcp\\es-mcp.exe"]
      }
    }
  }
}
```

For Claude Desktop / Cursor style clients, the same idea: `"command": "C:\\tools\\es-mcp\\es-mcp.exe"`, no arguments.

## `search_files`

| Parameter | Type | Default | Description |
| --- | --- | --- | --- |
| `query` | string | required | Everything query (`*.sln`, `report ext:pdf`, `path:src`, `dm:today`, …) |
| `limit` | integer | 30 | 1-1000 rows; the reply's total count says whether more exist |
| `offset` | integer | 0 | rows to skip, for paging (`offset = nextOffset`) |
| `regex` | boolean | false | query is a regular expression |
| `case_sensitive` | boolean | false | match case |
| `whole_word` | boolean | false | whole words only |
| `match_path` | boolean | false | match the full path, not just the name |
| `sort` | string | `path` | name, path, size, extension, type, date_modified, date_created, date_accessed (+ `_desc`), `none`, or a numeric SDK constant; `sort_by` is an alias |
| `filter` | string | — | extra clause ANDed onto the query, e.g. `ext:cs path:src` |

The legacy tool name `search` is still accepted on calls.

### Structured results

Besides the text table, every successful call returns `structuredContent` that matches the tool's
`outputSchema`:

```json
{
  "total": 128,
  "returned": 2,
  "offset": 0,
  "hasMore": true,
  "nextOffset": 2,
  "results": [
    {
      "path": "E:\\work\\api\\Api.sln",
      "name": "Api.sln",
      "folder": false,
      "ext": "sln",
      "size": 3784,
      "modified": "2026-01-08T11:20:13.4412701+03:00",
      "created": "2025-11-02T09:14:00.0000000+03:00",
      "accessed": "2026-01-09T08:01:44.1200000+03:00"
    },
    {
      "path": "E:\\work\\api\\docs",
      "name": "docs",
      "folder": true,
      "ext": null,
      "size": null,
      "modified": "2026-01-30T17:42:10.0000000+03:00",
      "created": "2025-10-11T12:00:00.0000000+03:00",
      "accessed": "2026-02-01T10:22:03.0000000+03:00"
    }
  ]
}
```

Folders carry `size: null` — Everything returns only the folder entry, so its size is not disk usage.

The text form of the same page:

```
20 total, showing 3
    1.2 MB  2026-02-03  C:\src\api\README.md
    4.4 KB  2026-02-03  C:\src\api\docs\README.md
   <dir>    2026-01-30  C:\src\api\docs
... 17 more of 20 total (page with offset 3, raise limit, or narrow the query)
```

A query that matches nothing says exactly that, and says what to change:

```
No files or folders match "quarterly_report_final.csv" - nothing was found for this query.
Try a different query:
- a shorter fragment or wildcard: 'report*' instead of one exact file name
- drop narrowing terms: ext:, path:, dm: and size: are ANDed, so each one removes results
- search full paths: match_path=true, or wrap the fragment in path: like 'path:src\api'
- broaden dates or sizes: 'dm:thisyear' instead of 'dm:today', 'size:>1mb' instead of 'size:>1gb'
```

## `everything_status`

No parameters. Returns whether the SDK loaded, whether Everything is running, the indexed object count,
the Everything version and the DLL path.

```json
{
  "ready": true,
  "sdkLoaded": true,
  "databaseLoaded": true,
  "version": "1.4.1.1026",
  "sdkPath": "C:\\Program Files\\Everything\\Everything64.dll",
  "totalCount": 1845267,
  "error": null
}
```

When `ready` is `false`, the call is marked `isError` and `error` says what to fix. That makes it a cheap
precheck: an agent can call it once instead of discovering a dead Everything through a failed search.

## What the agent sees

The server is written to be usable without prior knowledge and without reading this README:

- **Tool names say what they do.** `search_files`, not `search`, so it wins over web-search tools in a tool list.
- **The description front-loads the decision**: use it *instead of* `dir`, `Get-ChildItem`, `where.exe` or `glob`,
  plus the query cheatsheet inline.
- **`initialize` returns `instructions`** — a short cheatsheet (query syntax, reply format, paging, error meaning)
  that clients inject into the model's context.
- **Annotations** mark both tools `readOnlyHint`, `idempotentHint`, `openWorldHint: false`, so agents know they are
  safe, local, repeatable reads.
- **Schemas carry the details**: 1-1000 limit range, `enum` of sort orders, `examples` for common calls, and output
  schemas so structured results are self-describing.
- **Empty and out-of-range results are self-correcting**: they name the failure and list concrete alternatives
  (broader fragment, drop `path:`/`ext:` terms, `match_path`, wider dates).
- **Protocol version negotiation**: the server echoes the client's version when it supports it (2024-11-05 through
  2025-11-25) and otherwise replies with its newest.
- **stdout stays protocol-clean**; diagnostics are written to stderr.

Agent-side, this replaces `dir`, `Get-ChildItem` and `where.exe`: one call answers "where is this file", "what is
the biggest thing on this drive", "list every `.sln` under `E:\work`".

## Layout

```
src/EsSdk/            Everything SDK interop (native bindings, DLL resolution, search and status API)
src/EsMcp/            MCP stdio server (protocol loop, tool catalog, argument parsing, result formatting)
tests/EsMcp.Tests/    xunit tests for the protocol, schemas, argument handling and result messages
tools/bench.mjs       round-trip latency benchmark against a published es-mcp.exe
```

`EsSdk` owns the only `DllImport` surface and one error model; `EsMcp` owns the protocol.

## Benchmark

Publish first, then run the script with Node:

```console
dotnet publish src/EsMcp -c Release -o out/es-mcp
node tools/bench.mjs out/es-mcp/es-mcp.exe 20
```

It prints a markdown table of p50/p95 round-trip latency per query shape, plus the number of objects Everything
has indexed.

## Notes and limits

- Everything must be running; the SDK talks to it over its IPC channel.
- The SDK keeps process-wide state, so `Everything.Search` and `Everything.GetStatus` serialize their calls.
- Folder results report no recursive size — treat their `size` as meaningless rather than as disk usage.
- `date_created` / `date_accessed` need those properties enabled in Everything's options to be meaningful.
- `everything_status` derives `totalCount` from a one-row `*` query, because the SDK only reports counts for the
  last query.

## License

[MIT](LICENSE) — the Everything SDK itself is © voidtools and distributed with Everything under its own terms.
