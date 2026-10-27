// es-cli — command-line search over the Everything index (voidtools Everything 1.4+).
using System.Globalization;
using System.Reflection;
using System.Text;
using System.Text.Json;
using EsSdk;

namespace EsCli;

internal static class Program
{
    internal const int ExitOk = 0;
    internal const int ExitUsage = 1;
    internal const int ExitSdkError = 2;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = false,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private static int Main(string[] args)
    {
        UseUtf8Output();

        try
        {
            var options = CommandLine.Parse(args);
            if (options is null) return ExitOk; // --help / --version already handled

            var results = Everything.Search(options.Query, options.ToSearchOptions());
            if (options.Json) WriteJson(results);
            else WriteTable(results, options.Query, options.Offset);
            return ExitOk;
        }
        catch (CommandLineException ex)
        {
            Console.Error.WriteLine($"error: {ex.Message}");
            Console.Error.WriteLine();
            Console.Error.WriteLine(CommandLine.Help);
            return ExitUsage;
        }
        catch (Exception ex) when (SdkLoadFailure.IsLoadFailure(ex))
        {
            Console.Error.WriteLine($"error: {SdkLoadFailure.Describe(ex)}");
            return ExitSdkError;
        }
        catch (EverythingException ex)
        {
            Console.Error.WriteLine($"error: {ex.Message}");
            return ExitSdkError;
        }
        catch (ArgumentException ex)
        {
            Console.Error.WriteLine($"error: {ex.Message}");
            return ExitUsage;
        }
    }

    /// <summary>Print non-ASCII paths correctly instead of through the console code page.</summary>
    private static void UseUtf8Output()
    {
        try
        {
            Console.OutputEncoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
        }
        catch (Exception ex) when (ex is IOException or PlatformNotSupportedException or System.Security.SecurityException)
        {
            // A redirected or headless console may refuse the change; keep the default then.
        }
    }

    private static void WriteTable(SearchResults results, string query, uint offset)
    {
        Console.Error.WriteLine($"# {results.Entries.Count} shown / {results.Total} total for \"{query}\"");

        foreach (var entry in results.Entries)
        {
            var kind = entry.IsFolder ? "d" : " ";
            var size = entry.IsFolder ? string.Empty : Display.HumanSize(entry.Size);
            var modified = entry.DateModified?.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture) ?? string.Empty;
            var extension = entry.Extension.Length > 0 ? $"[{entry.Extension}]" : string.Empty;

            Console.WriteLine($"{kind}  {size,9}  {modified,-16}  {entry.FullPath}  {extension}".TrimEnd());
        }

        if (results.Entries.Count == 0 && results.Total > 0)
            Console.Error.WriteLine($"# offset {offset} is past the last match ({results.Total} in total)");
        else if ((long)results.Total - offset - results.Entries.Count > 0)
            Console.Error.WriteLine($"# {results.Total - offset - results.Entries.Count} more — raise --limit or use --offset");
    }

    private static void WriteJson(SearchResults results)
    {
        // JSON Lines: one object per result, byte-for-byte consumable by jq.
        foreach (var entry in results.Entries)
        {
            Console.WriteLine(JsonSerializer.Serialize(new
            {
                path = entry.FullPath,
                name = entry.Name,
                folder = entry.IsFolder,
                ext = entry.Extension,
                size = entry.Size,
                modified = entry.DateModified?.ToString("o", CultureInfo.InvariantCulture),
            }, JsonOptions));
        }
    }
}

internal sealed record CommandLineOptions(
    string Query,
    uint Limit,
    uint Offset,
    bool Regex,
    bool MatchCase,
    bool MatchWholeWord,
    bool MatchPath,
    bool Json,
    SortOrder Sort)
{
    public SearchOptions ToSearchOptions() => new()
    {
        Limit = Limit,
        Offset = Offset,
        Regex = Regex,
        MatchCase = MatchCase,
        MatchWholeWord = MatchWholeWord,
        MatchPath = MatchPath,
        Sort = Sort,
    };
}

internal sealed class CommandLineException(string message) : Exception(message);

internal static class CommandLine
{
    internal const string Help = """
        usage: es-cli <query> [options]

        Query syntax is Everything's own: ext:exe, folder:, dm:today, wildcards, etc.

        options:
          -n, --limit N        maximum results to return (1-1000, default 50)
          -o, --offset N       skip the first N results (paging)
              --regex          treat the query as a regular expression
              --case           match case
              --whole-word     match whole words only
              --path           match against the full path, not just the file name
              --sort ORDER     name|name-desc|path|path-desc|size|size-desc|extension|
                               extension-desc|type|type-desc|date-modified|date-modified-desc|
                               date-created|date-created-desc|date-accessed|date-accessed-desc|none
                               (default: path)
              --json           one JSON object per result (JSON Lines)
          -h, --help           show this help
          -v, --version        show the version

        --limit=10, --offset=20 and --sort=size are equivalent to the spaced forms.
        -- ends option parsing, so a query may start with "-" (es-cli -- -draft).

        exit codes: 0 ok, 1 bad usage, 2 Everything SDK error (usually: Everything not running)

        Everything (voidtools) must be running in the background, and Everything64.dll must be
        reachable — next to the executable, in the Everything install folder, or via
        EVERYTHING_SDK_PATH.
        """;

    private static string Version =>
        typeof(CommandLine).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
        ?? typeof(CommandLine).Assembly.GetName().Version?.ToString()
        ?? "0.0.0";

    private static readonly Dictionary<string, SortOrder> SortNames = new(StringComparer.OrdinalIgnoreCase)
    {
        ["name"] = SortOrder.Name,
        ["name-desc"] = SortOrder.NameDescending,
        ["path"] = SortOrder.Path,
        ["path-desc"] = SortOrder.PathDescending,
        ["size"] = SortOrder.Size,
        ["size-desc"] = SortOrder.SizeDescending,
        ["extension"] = SortOrder.Extension,
        ["extension-desc"] = SortOrder.ExtensionDescending,
        ["type"] = SortOrder.Type,
        ["type-desc"] = SortOrder.TypeDescending,
        ["date-created"] = SortOrder.DateCreated,
        ["date-created-desc"] = SortOrder.DateCreatedDescending,
        ["date-modified"] = SortOrder.DateModified,
        ["date-modified-desc"] = SortOrder.DateModifiedDescending,
        ["date-accessed"] = SortOrder.DateAccessed,
        ["date-accessed-desc"] = SortOrder.DateAccessedDescending,
        ["none"] = SortOrder.None,
    };

    /// <summary>Options whose value may be attached with <c>=</c> instead of a following argument.</summary>
    private static readonly HashSet<string> ValueOptions = new(StringComparer.Ordinal)
    {
        "-n", "--limit", "-o", "--offset", "--sort",
    };

    /// <summary>Parse argv. Returns <c>null</c> when the invocation was fully handled (help/version).</summary>
    internal static CommandLineOptions? Parse(string[] args)
    {
        string? query = null;
        var limit = 50u;
        var offset = 0u;
        var sort = SortOrder.Path;
        var json = false;
        var regex = false;
        var matchCase = false;
        var wholeWord = false;
        var matchPath = false;
        var endOfOptions = false;

        for (var i = 0; i < args.Length; i++)
        {
            var arg = args[i];

            if (endOfOptions)
            {
                query = AppendQuery(query, arg);
                continue;
            }

            if (arg == "--")
            {
                endOfOptions = true;
                continue;
            }

            // Long options accept --name=value as well as --name value.
            var option = arg;
            string? inlineValue = null;
            if (arg.StartsWith("--", StringComparison.Ordinal))
            {
                var separator = arg.IndexOf('=');
                if (separator > 2)
                {
                    option = arg[..separator];
                    inlineValue = arg[(separator + 1)..];
                }
            }

            if (inlineValue is not null && !ValueOptions.Contains(option))
                throw new CommandLineException($"{option} does not take a value");

            switch (option)
            {
                case "-n":
                case "--limit":
                    limit = ParseUInt(args, ref i, option, inlineValue);
                    break;

                case "-o":
                case "--offset":
                    offset = ParseUInt(args, ref i, option, inlineValue);
                    break;

                case "--sort":
                    var name = NextValue(args, ref i, option, inlineValue);
                    if (!SortNames.TryGetValue(name, out sort))
                        throw new CommandLineException($"unknown sort order: {name}");
                    break;

                case "--regex":
                    regex = true;
                    break;

                case "--case":
                case "--case-sensitive":
                    matchCase = true;
                    break;

                case "--whole-word":
                    wholeWord = true;
                    break;

                case "--path":
                case "--match-path":
                    matchPath = true;
                    break;

                case "--json":
                    json = true;
                    break;

                case "-h":
                case "--help":
                    Console.Out.WriteLine(Help);
                    return null;

                case "-v":
                case "--version":
                    Console.Out.WriteLine($"es-cli {Version}");
                    return null;

                default:
                    if (option.StartsWith('-') && option != "-")
                        throw new CommandLineException($"unknown option: {option}");

                    query = AppendQuery(query, arg);
                    break;
            }
        }

        if (string.IsNullOrWhiteSpace(query))
            throw new CommandLineException("missing query");

        if (limit is 0 or > 1000)
            throw new CommandLineException("--limit must be between 1 and 1000");

        return new CommandLineOptions(query, limit, offset, regex, matchCase, wholeWord, matchPath, json, sort);
    }

    /// <summary>Unquoted multi-word queries are re-joined with single spaces.</summary>
    private static string AppendQuery(string? query, string arg) =>
        query is null ? arg : $"{query} {arg}";

    private static string NextValue(string[] args, ref int index, string option, string? inlineValue)
    {
        if (inlineValue is not null) return inlineValue;
        if (++index >= args.Length) throw new CommandLineException($"{option} requires a value");
        return args[index];
    }

    private static uint ParseUInt(string[] args, ref int index, string option, string? inlineValue)
    {
        var raw = NextValue(args, ref index, option, inlineValue);
        return uint.TryParse(raw, NumberStyles.None, CultureInfo.InvariantCulture, out var value)
            ? value
            : throw new CommandLineException($"{option} requires a non-negative number, got: {raw}");
    }
}
