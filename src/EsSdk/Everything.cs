// Public API over the Everything SDK: one synchronous, thread-safe search call.
using System.Runtime.InteropServices;

namespace EsSdk;

/// <summary>Everything sort constants (https://www.voidtools.com/support/everything/sdk/).</summary>
public enum SortOrder : uint
{
    None = 0,
    Name = 1,
    NameDescending = 2,
    Path = 3,
    PathDescending = 4,
    Size = 5,
    SizeDescending = 6,
    Extension = 7,
    ExtensionDescending = 8,
    Type = 9,
    TypeDescending = 10,
    DateCreated = 11,
    DateCreatedDescending = 12,
    DateModified = 13,
    DateModifiedDescending = 14,
    DateAccessed = 15,
    DateAccessedDescending = 16,
}

/// <summary>Options for a single Everything query.</summary>
public sealed record SearchOptions
{
    /// <summary>Maximum number of results to return (1-1000).</summary>
    public uint Limit { get; init; } = 50;

    /// <summary>Number of results to skip, for paging.</summary>
    public uint Offset { get; init; }

    /// <summary>Treat the query as a regular expression.</summary>
    public bool Regex { get; init; }

    /// <summary>Match case.</summary>
    public bool MatchCase { get; init; }

    /// <summary>Match whole words only.</summary>
    public bool MatchWholeWord { get; init; }

    /// <summary>Match against the full path, not just the file name.</summary>
    public bool MatchPath { get; init; }

    /// <summary>Result ordering.</summary>
    public SortOrder Sort { get; init; } = SortOrder.Path;

    /// <summary>Extra Everything clause appended to the query, e.g. <c>ext:cs folder:src</c>.</summary>
    public string? Filter { get; init; }

    /// <summary>
    /// Which per-result fields to fetch. Fewer fields means fewer native calls per row and a
    /// smaller request flag set, so ask for only what you will show.
    /// </summary>
    public FileFields Fields { get; init; } = FileFields.All;
}

/// <summary>Per-result fields a caller wants from the SDK.</summary>
[Flags]
public enum FileFields
{
    /// <summary>Only the full path and the folder flag.</summary>
    None = 0,

    /// <summary>Extension without the dot.</summary>
    Extension = 1,

    /// <summary>Size in bytes.</summary>
    Size = 2,

    /// <summary>Last write time.</summary>
    Modified = 4,

    /// <summary>Creation time.</summary>
    Created = 8,

    /// <summary>Last access time.</summary>
    Accessed = 16,

    /// <summary>What a search listing needs: extension, size and modified time.</summary>
    Default = Extension | Size | Modified,

    /// <summary>Everything the SDK can report.</summary>
    All = Extension | Size | Modified | Created | Accessed,
}


/// <summary>One file or folder matched by a query.</summary>
public sealed record FileEntry(
    string FullPath,
    bool IsFolder,
    string Extension,
    long Size,
    DateTime? DateModified,
    DateTime? DateCreated = null,
    DateTime? DateAccessed = null)
{
    /// <summary>File name without the directory part.</summary>
    public string Name => Path.GetFileName(FullPath);
}

/// <summary>Search results plus the unfiltered match count.</summary>
public sealed record SearchResults(uint Total, IReadOnlyList<FileEntry> Entries);

/// <summary>
/// A cheap health probe for agents: whether the SDK loaded, whether Everything is running,
/// and how many objects it has indexed. Read-only.
/// </summary>
public sealed record EverythingStatus(
    bool SdkLoaded,
    bool DatabaseLoaded,
    string? SdkPath,
    string? EverythingVersion,
    uint TotalCount,
    string? Error)
{
    /// <summary>True when the caller can run searches right now.</summary>
    public bool Ready => SdkLoaded && DatabaseLoaded;
}

/// <summary>Raised when the Everything SDK reports an error (usually: Everything is not running).</summary>
public sealed class EverythingException : Exception
{
    private static readonly string[] Messages =
    [
        "no error",
        "out of memory",
        "Everything is not running (IPC failed)",
        "register class failed",
        "create window failed",
        "create thread failed",
        "invalid index",
        "invalid call",
        "invalid request data",
        "bad parameter",
    ];

    public EverythingException(uint code) : base(Describe(code)) => Code = code;

    /// <summary>Raw <c>Everything_GetLastError</c> value.</summary>
    public uint Code { get; }

    internal static string Describe(uint code) =>
        code < (uint)Messages.Length ? Messages[code] : $"Everything SDK error {code}";
}

/// <summary>
/// Thin, thread-safe wrapper over the Everything SDK. Everything must be running in the background.
/// </summary>
public static class Everything
{
    // The SDK keeps process-wide state, so queries are serialized.
    private static readonly Lock Gate = new();

    // Windows paths are limited to 32767 characters including the terminator.
    private const int PathBufferLength = 32768;

    /// <summary>Run a query and return the matches.</summary>
    /// <exception cref="ArgumentException">The query is empty.</exception>
    /// <exception cref="EverythingException">The SDK rejected the query or Everything is not running.</exception>
    public static SearchResults Search(string query, SearchOptions? options = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(query);

        options ??= new SearchOptions();
        var limit = Math.Clamp(options.Limit, 1u, 1000u);
        var effectiveQuery = string.IsNullOrWhiteSpace(options.Filter) ? query : $"{query} {options.Filter}";

        lock (Gate)
        {
            Native.Everything_SetSearchW(effectiveQuery);
            Native.Everything_SetMatchPath(options.MatchPath);
            Native.Everything_SetMatchCase(options.MatchCase);
            Native.Everything_SetMatchWholeWord(options.MatchWholeWord);
            Native.Everything_SetRegex(options.Regex);
            Native.Everything_SetMax(limit);
            Native.Everything_SetOffset(options.Offset);
            Native.Everything_SetSort((uint)options.Sort);
            Native.Everything_SetRequestFlags(
                Native.ReqFileName | Native.ReqPath | Native.ReqFullPath |
                (options.Fields.HasFlag(FileFields.Extension) ? Native.ReqExtension : 0u) |
                (options.Fields.HasFlag(FileFields.Size) ? Native.ReqSize : 0u) |
                (options.Fields.HasFlag(FileFields.Created) ? Native.ReqDateCreated : 0u) |
                (options.Fields.HasFlag(FileFields.Modified) ? Native.ReqDateModified : 0u) |
                (options.Fields.HasFlag(FileFields.Accessed) ? Native.ReqDateAccessed : 0u));

            if (!Native.Everything_QueryW(true))
            {
                var code = Native.Everything_GetLastError();
                Native.Everything_CleanUp();
                throw new EverythingException(code);
            }

            try
            {
                var count = Native.Everything_GetNumResults();
                var total = Native.Everything_GetTotResults();
                var entries = new List<FileEntry>((int)Math.Min(count, limit));

                var buffer = new char[PathBufferLength];
                var pinned = GCHandle.Alloc(buffer, GCHandleType.Pinned);
                try
                {
                    for (uint i = 0; i < count; i++)
                    {
                        var length = Native.Everything_GetResultFullPathNameW(
                            i, pinned.AddrOfPinnedObject(), (uint)buffer.Length);

                        var path = length > 0 ? new string(buffer, 0, (int)Math.Min(length, (uint)buffer.Length)) : string.Empty;
                        var extension = options.Fields.HasFlag(FileFields.Extension)
                            ? Marshal.PtrToStringUni(Native.Everything_GetResultExtensionW(i)) ?? string.Empty
                            : string.Empty;

                        var size = options.Fields.HasFlag(FileFields.Size) && Native.Everything_GetResultSize(i, out var bytes)
                            ? bytes
                            : 0;
                        var modified = options.Fields.HasFlag(FileFields.Modified)
                            && Native.Everything_GetResultDateModified(i, out var modifiedTime)
                            ? ToLocalTime(modifiedTime)
                            : null;
                        var created = options.Fields.HasFlag(FileFields.Created)
                            && Native.Everything_GetResultDateCreated(i, out var createdTime)
                            ? ToLocalTime(createdTime)
                            : null;
                        var accessed = options.Fields.HasFlag(FileFields.Accessed)
                            && Native.Everything_GetResultDateAccessed(i, out var accessedTime)
                            ? ToLocalTime(accessedTime)
                            : null;

                        entries.Add(new FileEntry(
                            path, Native.Everything_IsFolderResult(i), extension, size, modified, created, accessed));
                    }
                }
                finally
                {
                    pinned.Free();
                }

                return new SearchResults(total, entries);
            }
            finally
            {
                Native.Everything_CleanUp();
            }
        }
    }

    /// <summary>
    /// Probe the SDK and the running Everything instance. Never throws: a missing DLL becomes
    /// <see cref="EverythingStatus.Error"/> with <c>SdkLoaded = false</c>.
    /// </summary>
    public static EverythingStatus GetStatus()
    {
        lock (Gate)
        {
            try
            {
                // First native call also triggers the DLL resolver, so a load failure lands here.
                var loaded = Native.Everything_IsDBLoaded();

                // The SDK reports counts for the last query only, so ask the cheapest one there is.
                uint total = 0;
                if (loaded)
                {
                    Native.Everything_SetSearchW("*");
                    Native.Everything_SetMax(1);
                    Native.Everything_SetOffset(0);
                    if (Native.Everything_QueryW(true)) total = Native.Everything_GetTotResults();
                    Native.Everything_CleanUp();
                }

                var major = Native.Everything_GetMajorVersion();
                var minor = Native.Everything_GetMinorVersion();
                var revision = Native.Everything_GetRevision();
                var build = Native.Everything_GetBuildNumber();
                var version = major + minor + revision + build == 0
                    ? null
                    : $"{major}.{minor}.{revision}.{build}";

                return new EverythingStatus(
                    SdkLoaded: true,
                    DatabaseLoaded: loaded,
                    SdkPath: DllResolver.LoadedPath,
                    EverythingVersion: version,
                    TotalCount: total,
                    Error: loaded ? null : "Everything is not running (IPC failed). Ask the user to start voidtools Everything.");
            }
            catch (Exception ex) when (SdkLoadFailure.IsLoadFailure(ex))
            {
                return new EverythingStatus(
                    SdkLoaded: false,
                    DatabaseLoaded: false,
                    SdkPath: DllResolver.LoadedPath,
                    EverythingVersion: null,
                    TotalCount: 0,
                    Error: SdkLoadFailure.Describe(ex));
            }
        }
    }

    /// <summary>Convert a Windows FILETIME to local time, tolerating values Everything did not populate.</summary>
    private static DateTime? ToLocalTime(long fileTime)
    {
        if (fileTime <= 0) return null;

        try
        {
            return DateTime.FromFileTimeUtc(fileTime).ToLocalTime();
        }
        catch (ArgumentOutOfRangeException)
        {
            return null;
        }
    }
}
