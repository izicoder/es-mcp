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

        return new
        {
            total = results.Total,
            returned,
            offset,
            hasMore,
            nextOffset = hasMore ? consumed : (long?)null,
            results = results.Entries.Select(ToEntry).ToArray(),
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

    private static object ToEntry(FileEntry entry) => new
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
}
