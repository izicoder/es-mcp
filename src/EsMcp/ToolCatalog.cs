namespace EsMcp;

/// <summary>
/// Everything the calling agent sees before its first call: tool names, descriptions, input
/// schemas, output schemas and the server-level instructions returned from <c>initialize</c>.
/// Keep these texts front-loaded — clients truncate long descriptions.
/// </summary>
internal static class ToolCatalog
{
    /// <summary>Tool name. Explicit about the target (files) so agents prefer it over web search.</summary>
    internal const string Name = "search_files";

    /// <summary>Legacy name, still accepted on <c>tools/call</c> so older configs keep working.</summary>
    internal const string LegacyName = "search";

    /// <summary>Health probe so an agent can check before searching instead of failing blind.</summary>
    internal const string StatusName = "everything_status";

    internal const string Description =
        "Find any file or folder on this Windows machine instantly - full paths, sizes and dates from the " +
        "voidtools Everything index, with no directory walking. Prefer it over dir, Get-ChildItem, where.exe or " +
        "glob for locating files by name, extension, size or date, for anything outside the open project, and for " +
        "listing a directory tree. Query uses Everything syntax: plain words match file names ('report'), " +
        "ext:cs, path:src, dm:today, size:>1gb, wildcards ('report*.pdf'), quotes for spaces or literal paths " +
        "(path:\"C:\\work\\api\"), and regex:true for regular expressions. Space-separated terms are ANDed. " +
        "Returns structuredContent with total, returned, offset, hasMore and a compact results array " +
        "(path, folder, size, modified). When returned is below total, page with offset or narrow the query. " +
        "format:\"text\" returns a table instead, format:\"full\" adds name, ext, created and accessed. " +
        "Read-only and safe to call repeatedly.";

    internal const string StatusDescription =
        "Check that Everything search is usable before relying on it: reports whether the Everything SDK loaded, " +
        "whether voidtools Everything is running, the indexed object count, the SDK version and the DLL path. " +
        "Call it once when search_files returns an unexpected error or an empty result you cannot explain. " +
        "Read-only and cheap.";

    /// <summary>Short cheatsheet injected into the agent context by <c>initialize</c>.</summary>
    internal const string Instructions =
        """
        es-mcp - instant file search on Windows, backed by the voidtools Everything index.

        Tools:
          search_files(query, limit?, offset?, regex?, case_sensitive?, whole_word?, match_path?, sort?, filter?, format?)
          everything_status() - is Everything running, how many objects are indexed, SDK version and DLL path.

        Use search_files to locate a file or folder by name, extension, size or date, to list a directory tree, or
        to answer "where is X" anywhere on this machine - instead of dir, Get-ChildItem, where.exe or a filesystem walk.

        Query is Everything syntax:
          *.sln              file names ending in .sln
          report             names containing "report"
          ext:cs             by extension          path:src\api       by path fragment
          dm:today           modified today        size:>1gb          bigger than 1 GB
          "C:\work\api"      quoted literal path   regex:true         regular expression
        Space-separated terms are ANDed; use filter for the narrowing clause instead of one long query.
        folder: matches folder names only - use path:Downloads to search inside a folder. Named sorts
        are preferred (date_accessed can be slow); an unrecognised value falls back to path.

        Replies carry structuredContent: total, returned, offset, hasMore, nextOffset and results.
        Each result has path, folder, size and modified ('yyyy-MM-dd HH:mm'); folder results carry no
        size and no size is disk usage. format:"full" adds name, ext, created and accessed.
        format:"text" returns a plain table instead, for clients that do not read structuredContent.
        If hasMore is true, page with offset (nextOffset) or tighten the query.

        If a call fails or returns "Everything is not running", ask the user to start voidtools Everything,
        or call everything_status for the details.
        """;

    /// <summary>JSON Schema for <c>search_files</c>, advertised by <c>tools/list</c>.</summary>
    internal const string SearchSchema =
        """
        {
          "name": "search_files",
          "title": "Search files and folders (Everything)",
          "description": "Find any file or folder on this Windows machine instantly - full paths, sizes and dates from the voidtools Everything index, with no directory walking. Prefer it over dir, Get-ChildItem, where.exe or glob for locating files by name, extension, size or date, for anything outside the open project, and for listing a directory tree. Query uses Everything syntax: plain words match file names ('report'), ext:cs, path:src, dm:today, size:>1gb, wildcards ('report*.pdf'), quotes for spaces or literal paths (path:\"C:\\work\\api\"), and regex:true for regular expressions. Space-separated terms are ANDed. Returns structuredContent with total, returned, offset, hasMore and a compact results array (path, folder, size, modified). When returned is below total, page with offset or narrow the query. format:\"text\" returns a table instead, format:\"full\" adds name, ext, created and accessed. Read-only and safe to call repeatedly.",
          "annotations": {
            "title": "Search files and folders (Everything)",
            "readOnlyHint": true,
            "destructiveHint": false,
            "idempotentHint": true,
            "openWorldHint": false
          },
          "inputSchema": {
            "type": "object",
            "properties": {
              "query": {
                "type": "string",
                "description": "Everything query. Plain words match file names; ext:cs, path:src, dm:today, size:>1gb, wildcards and quoted paths all work. folder: matches folder names only - use path: to search inside a folder. Examples: '*.sln', 'report ext:pdf', 'path:\"C:\\work\\api\"'."
              },
              "limit": {
                "type": "integer",
                "minimum": 1,
                "maximum": 1000,
                "default": 30,
                "description": "Maximum rows to return (1-1000, default 30). The reply's total count tells you whether more exist."
              },
              "offset": {
                "type": "integer",
                "minimum": 0,
                "default": 0,
                "description": "Rows to skip, for paging: call again with offset = nextOffset."
              },
              "regex": {
                "type": "boolean",
                "default": false,
                "description": "Treat the query as a regular expression (Everything's own engine)."
              },
              "case_sensitive": {
                "type": "boolean",
                "default": false,
                "description": "Match case. Off by default because Windows paths are case-insensitive."
              },
              "whole_word": {
                "type": "boolean",
                "default": false,
                "description": "Match whole words only, instead of substrings."
              },
              "match_path": {
                "type": "boolean",
                "default": false,
                "description": "Match against the full path instead of just the file name. Needed for fragments like 'src/api' outside of path:."
              },
              "sort": {
                "type": "string",
                "default": "path",
                "enum": [
                  "name", "name_desc",
                  "path", "path_desc",
                  "size", "size_desc",
                  "extension", "extension_desc",
                  "type", "type_desc",
                  "date_modified", "date_modified_desc",
                  "date_created", "date_created_desc",
                  "date_accessed", "date_accessed_desc",
                  "none"
                ],
                "description": "Result ordering. Use size_desc for 'largest files', date_modified_desc for 'most recently changed'. Named sorts are preferred; date_accessed can take seconds on large indexes, and an unrecognised value falls back to path. A numeric Everything sort constant (0-16) is also accepted; sort_by works as an alias."
              },
              "filter": {
                "type": "string",
                "description": "Extra Everything clause ANDed onto the query: 'ext:cs path:src', 'dm:thisweek'. Cleaner than one long query string."
              },
              "format": {
                "type": "string",
                "enum": ["json", "text", "full"],
                "default": "json",
                "description": "Reply shape. json (default): rows in structuredContent plus a one line summary, cheapest for an agent. text: the table only, for clients that never read structuredContent. full: adds name, ext, created and accessed to every row."
              }
            },
            "required": ["query"],
            "examples": [
              { "query": "*.sln" },
              { "query": "report", "filter": "ext:pdf dm:thisweek" },
              { "query": "*", "sort": "size_desc", "limit": 10 },
              { "query": "config", "match_path": true, "filter": "path:src" }
            ]
          },
          "outputSchema": {
            "type": "object",
            "properties": {
              "total": { "type": "integer", "description": "Every match for the query, not just this page." },
              "returned": { "type": "integer", "description": "Rows in this page." },
              "offset": { "type": "integer", "description": "Rows skipped before this page." },
              "hasMore": { "type": "boolean", "description": "True when rows after this page exist." },
              "nextOffset": { "type": ["integer", "null"], "description": "Offset to pass on the next call, or null on the last page." },
              "results": {
                "type": "array",
                "description": "One object per match, in the requested sort order. Default rows carry path, folder, size and modified; format:\"full\" adds name, ext, created and accessed.",
                "items": {
                  "type": "object",
                  "properties": {
                    "path": { "type": "string", "description": "Full path." },
                    "folder": { "type": "boolean", "description": "True when the entry is a directory. Omitted for files." },
                    "size": { "type": "integer", "description": "Size in bytes. Omitted for folders, whose size Everything does not report." },
                    "modified": { "type": "string", "description": "Local time, 'yyyy-MM-dd HH:mm'." },
                    "name": { "type": "string", "description": "File or folder name without the directory. format:\"full\" only - already inside path." },
                    "ext": { "type": ["string", "null"], "description": "Extension without the dot. format:\"full\" only - already inside path." },
                    "created": { "type": ["string", "null"], "description": "ISO 8601 local time. format:\"full\" only." },
                    "accessed": { "type": ["string", "null"], "description": "ISO 8601 local time. format:\"full\" only." }
                  },
                  "required": ["path"]
                }
              }
            },
            "required": ["total", "returned", "offset", "hasMore", "results"]
          }
        }
        """;

    /// <summary>JSON Schema for <c>everything_status</c>.</summary>
    internal const string StatusSchema =
        """
        {
          "name": "everything_status",
          "title": "Everything status",
          "description": "Check that Everything search is usable before relying on it: reports whether the Everything SDK loaded, whether voidtools Everything is running, the indexed object count, the SDK version and the DLL path. Call it once when search_files returns an unexpected error or an empty result you cannot explain. Read-only and cheap.",
          "annotations": {
            "title": "Everything status",
            "readOnlyHint": true,
            "destructiveHint": false,
            "idempotentHint": true,
            "openWorldHint": false
          },
          "inputSchema": {
            "type": "object",
            "properties": {}
          },
          "outputSchema": {
            "type": "object",
            "properties": {
              "ready": { "type": "boolean", "description": "True when searches will work right now." },
              "sdkLoaded": { "type": "boolean", "description": "True when Everything64.dll loaded." },
              "databaseLoaded": { "type": "boolean", "description": "True when Everything is running and its index is loaded." },
              "version": { "type": ["string", "null"], "description": "Everything SDK version, or null." },
              "sdkPath": { "type": ["string", "null"], "description": "Full path the SDK loaded from, or null." },
              "totalCount": { "type": "integer", "description": "Objects in the index." },
              "error": { "type": ["string", "null"], "description": "What to fix when ready is false, or null." }
            },
            "required": ["ready", "sdkLoaded", "databaseLoaded", "version", "sdkPath", "totalCount", "error"]
          }
        }
        """;
}
