using System.Globalization;
using EsSdk;

namespace EsMcp;

/// <summary>
/// Builds the <c>structuredContent</c> payloads advertised by the tool output schemas, plus the
/// short human-readable status line. Field names and shapes here must match <see cref="ToolCatalog"/>.
/// </summary>
internal static class ResultPayload
{
    internal static object Build(ToolRequest request, SearchResults results)
    {
        var offset = (long)request.Options.Offset;
        var returned = results.Entries.Count;
        var consumed = offset + returned;
        var hasMore = results.Total > consumed;
        var full = request.Format == OutputFormat.Full;

        return new
        {
            total = results.Total,
            returned,
            offset,
            hasMore,
            nextOffset = hasMore ? consumed : (long?)null,
            results = full
                ? results.Entries.Select(ToFullEntry).ToArray()
                : results.Entries.Select(ToCompactEntry).ToArray(),
        };
    }

    internal static object BuildStatus(EverythingStatus status) => new
    {
        ready = status.Ready,
        sdkLoaded = status.SdkLoaded,
        databaseLoaded = status.DatabaseLoaded,
        version = status.EverythingVersion,
        sdkPath = status.SdkPath,
        totalCount = status.TotalCount,
        error = status.Error,
    };

    internal static string FormatStatus(EverythingStatus status)
    {
        if (status.Ready)
        {
            var version = status.EverythingVersion is null ? "version unknown" : $"SDK {status.EverythingVersion}";
            var path = status.SdkPath is null ? string.Empty : $" at {status.SdkPath}";
            return $"Everything is ready: {status.TotalCount.ToString("N0", CultureInfo.InvariantCulture)} objects " +
                   $"indexed ({version}{path}). Searches will work.";
        }

        var reason = status.Error ?? "Everything is not running (IPC failed).";
        return $"Everything is not ready: {reason} Searches will fail until this is fixed.";
    }

    /// <summary>
    /// The default row. Four fields, because <c>name</c> and <c>ext</c> are already in
    /// <c>path</c>, folder sizes are meaningless, and created/accessed cost tokens nobody asked for.
    /// Null fields are omitted rather than sent as nulls.
    /// </summary>
    private static Dictionary<string, object> ToCompactEntry(FileEntry entry)
    {
        var row = new Dictionary<string, object>(4) { ["path"] = entry.FullPath };

        if (entry.IsFolder) row["folder"] = true;
        else row["size"] = entry.Size;

        var modified = ShortTime(entry.DateModified);
        if (modified is not null) row["modified"] = modified;

        return row;
    }

    /// <summary>Everything the SDK reports, for callers that asked for <c>format:"full"</c>.</summary>
    private static object ToFullEntry(FileEntry entry) => new
    {
        path = entry.FullPath,
        name = entry.Name,
        folder = entry.IsFolder,
        ext = string.IsNullOrEmpty(entry.Extension) ? null : entry.Extension,
        size = entry.IsFolder ? (long?)null : entry.Size,
        modified = Iso(entry.DateModified),
        created = Iso(entry.DateCreated),
        accessed = Iso(entry.DateAccessed),
    };

    private static string? Iso(DateTime? value) =>
        value?.ToString("o", CultureInfo.InvariantCulture);

    /// <summary>Minute precision: enough to sort and read, a third of the characters of ISO 8601 round-trip.</summary>
    private static string? ShortTime(DateTime? value) =>
        value?.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);
}
