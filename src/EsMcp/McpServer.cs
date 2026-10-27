using System.Reflection;
using System.Text.Encodings.Web;
using System.Text.Json;
using EsSdk;

namespace EsMcp;

/// <summary>
/// Newline-delimited JSON-RPC 2.0 protocol layer. <see cref="HandleLine"/> takes one request line
/// and returns the response line, or <c>null</c> when the message gets no answer (notifications,
/// blank lines). Search and status are injected so the protocol can be tested without Everything.
/// </summary>
internal sealed class McpServer
{
    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = false,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    /// <summary>Parsed once; the schemas never change at runtime.</summary>
    private static readonly JsonDocument SearchSchemaDocument = JsonDocument.Parse(ToolCatalog.SearchSchema);

    private static readonly JsonDocument StatusSchemaDocument = JsonDocument.Parse(ToolCatalog.StatusSchema);

    /// <summary>Newest first. A client asking for one of these gets it echoed back.</summary>
    internal static readonly string[] SupportedProtocolVersions =
        ["2025-11-25", "2025-06-18", "2025-03-26", "2024-11-05"];

    internal const string LatestProtocolVersion = "2025-11-25";

    private readonly Func<ToolRequest, SearchResults> _search;
    private readonly Func<EverythingStatus> _status;
    private readonly Action<string> _log;

    internal McpServer(
        Func<ToolRequest, SearchResults>? search = null,
        Func<EverythingStatus>? status = null,
        Action<string>? log = null)
    {
        _search = search ?? (request => Everything.Search(request.Query, request.Options));
        _status = status ?? Everything.GetStatus;
        _log = log ?? LogToStderr;
    }

    /// <summary>Handle one input line. Returns the response line, or null when nothing should be written.</summary>
    internal string? HandleLine(string line)
    {
        if (string.IsNullOrWhiteSpace(line)) return null;

        JsonDocument request;
        try
        {
            request = JsonDocument.Parse(line);
        }
        catch (JsonException)
        {
            return Serialize(new
            {
                jsonrpc = "2.0",
                id = (object?)null,
                error = new { code = -32700, message = "parse error" },
            });
        }

        using (request) return Handle(request.RootElement);
    }

    private string? Handle(JsonElement request)
    {
        var id = request.TryGetProperty("id", out var idElement) ? idElement.Clone() : (JsonElement?)null;
        var method = request.TryGetProperty("method", out var m) && m.ValueKind == JsonValueKind.String ? m.GetString() : null;
        var parameters = request.TryGetProperty("params", out var p) ? p : default;

        try
        {
            switch (method)
            {
                case "initialize":
                    return Result(id, new
                    {
                        protocolVersion = NegotiateProtocol(parameters),
                        capabilities = new { tools = new { listChanged = false } },
                        serverInfo = new { name = "es-mcp", title = "Everything file search", version = Version },
                        instructions = ToolCatalog.Instructions,
                    });

                case "ping":
                    return Result(id, new { });

                case "tools/list":
                    return Result(id, new
                    {
                        tools = new[]
                        {
                            SearchSchemaDocument.RootElement.Clone(),
                            StatusSchemaDocument.RootElement.Clone(),
                        },
                    });

                case "tools/call":
                    return CallTool(id, parameters);

                case "resources/list":
                    return Result(id, new { resources = Array.Empty<object>() });

                case "prompts/list":
                    return Result(id, new { prompts = Array.Empty<object>() });

                case var _ when method is not null && method.StartsWith("notifications/", StringComparison.Ordinal):
                    return null; // notifications never get a response

                case null:
                    return null; // a response to something we never send

                default:
                    return Error(id, -32601, $"method not found: {method}");
            }
        }
        catch (JsonException ex)
        {
            return Error(id, -32602, $"invalid params: {ex.Message}");
        }
        catch (Exception ex)
        {
            _log($"internal error handling {method}: {ex}");
            return Error(id, -32603, $"internal error: {ex.Message}");
        }
    }

    private string? CallTool(JsonElement? id, JsonElement parameters)
    {
        if (parameters.ValueKind != JsonValueKind.Object)
        {
            return Error(id, -32602, $"missing params - expected {{name: \"{ToolCatalog.Name}\", arguments: {{query: \"*.sln\"}}}}");
        }

        var name = parameters.TryGetProperty("name", out var n) && n.ValueKind == JsonValueKind.String ? n.GetString() : null;
        if (name is not (ToolCatalog.Name or ToolCatalog.LegacyName or ToolCatalog.StatusName))
        {
            return Error(id, -32602, $"unknown tool: {name} (available: {ToolCatalog.Name}, {ToolCatalog.StatusName})");
        }

        if (name == ToolCatalog.StatusName) return StatusTool(id);

        var arguments = parameters.TryGetProperty("arguments", out var a) && a.ValueKind == JsonValueKind.Object
            ? a
            : default;

        if (!ToolArguments.TryParse(arguments, out var toolRequest, out var error))
        {
            return ToolResult(id, error!, isError: true);
        }

        try
        {
            var results = _search(toolRequest!);
            var structured = toolRequest!.Format == OutputFormat.Text ? null : ResultPayload.Build(toolRequest!, results);

            return ToolResult(
                id,
                structured is null
                    ? ResultFormatter.Format(toolRequest!, results)
                    : ResultFormatter.Summary(toolRequest!, results),
                isError: false,
                structured);
        }
        catch (Exception ex) when (SdkLoadFailure.IsLoadFailure(ex))
        {
            _log(SdkLoadFailure.Describe(ex));
            return ToolResult(id, SdkLoadFailure.Describe(ex), isError: true);
        }
        catch (EverythingException ex)
        {
            return ToolResult(id, $"error: {ex.Message}. Ask the user to start voidtools Everything, then retry.", isError: true);
        }
        catch (ArgumentException ex)
        {
            return ToolResult(id, $"error: {ex.Message}", isError: true);
        }
    }

    private string? StatusTool(JsonElement? id)
    {
        EverythingStatus status;
        try
        {
            status = _status();
        }
        catch (Exception ex) when (SdkLoadFailure.IsLoadFailure(ex))
        {
            _log(SdkLoadFailure.Describe(ex));
            return ToolResult(id, SdkLoadFailure.Describe(ex), isError: true);
        }

        return ToolResult(id, ResultPayload.FormatStatus(status), isError: !status.Ready, ResultPayload.BuildStatus(status));
    }

    private static string NegotiateProtocol(JsonElement parameters)
    {
        if (parameters.ValueKind == JsonValueKind.Object
            && parameters.TryGetProperty("protocolVersion", out var value)
            && value.ValueKind == JsonValueKind.String)
        {
            var requested = value.GetString();
            if (requested is not null && Array.IndexOf(SupportedProtocolVersions, requested) >= 0) return requested;
        }

        return LatestProtocolVersion;
    }

    private string? ToolResult(JsonElement? id, string text, bool isError, object? structured = null)
    {
        if (id is null) return null;

        var content = new[] { new { type = "text", text } };
        object result = structured is null
            ? new { content, isError }
            : new { content, structuredContent = structured, isError };

        return Serialize(new { jsonrpc = "2.0", id, result });
    }

    private string? Result(JsonElement? id, object result) =>
        id is null ? null : Serialize(new { jsonrpc = "2.0", id, result });

    private string? Error(JsonElement? id, int code, string message)
    {
        if (id is null) return null;

        _log($"{code}: {message}");
        return Serialize(new { jsonrpc = "2.0", id, error = new { code, message } });
    }

    private static string Serialize(object value) => JsonSerializer.Serialize(value, Json);

    private static void LogToStderr(string message) => Console.Error.WriteLine($"[es-mcp] {message}");

    private static string Version =>
        typeof(McpServer).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
        ?? typeof(McpServer).Assembly.GetName().Version?.ToString()
        ?? "0.0.0";
}
