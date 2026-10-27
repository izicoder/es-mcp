using System.Text.Json;
using EsMcp;
using EsSdk;

namespace EsMcp.Tests;

public class McpServerTests
{
    private static McpServer Server(
        Func<ToolRequest, SearchResults>? search = null,
        Func<EverythingStatus>? status = null,
        Action<string>? log = null) =>
        new(
            search ?? (_ => new SearchResults(0, [])),
            status ?? (() => new EverythingStatus(true, true, @"C:\Everything\Everything64.dll", "1.4.1.1026", 1234, null)),
            log ?? (_ => { }));

    private static JsonElement Call(McpServer server, string json)
    {
        var response = server.HandleLine(json);
        Assert.NotNull(response);
        return JsonDocument.Parse(response!).RootElement;
    }

    private static FileEntry File(string path, long size = 1024, bool folder = false) =>
        new(
            path,
            folder,
            folder ? string.Empty : Path.GetExtension(path).TrimStart('.'),
            size,
            new DateTime(2026, 1, 2, 3, 4, 5, DateTimeKind.Local),
            new DateTime(2026, 1, 1, 1, 1, 1, DateTimeKind.Local),
            new DateTime(2026, 1, 3, 2, 2, 2, DateTimeKind.Local));

    private static JsonElement SearchCall(McpServer server, string arguments, string tool = "search_files") =>
        Call(server,
            "{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"tools/call\",\"params\":{\"name\":\"" + tool +
            "\",\"arguments\":" + arguments + "}}");

    // ---- initialize -------------------------------------------------------------------------

    [Fact]
    public void Initialize_echoes_a_protocol_version_the_client_asked_for()
    {
        var result = Call(Server(), """{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2024-11-05"}}""")
            .GetProperty("result");

        Assert.Equal("2024-11-05", result.GetProperty("protocolVersion").GetString());
    }

    [Fact]
    public void Initialize_falls_back_to_the_newest_supported_version()
    {
        var result = Call(Server(), """{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"1999-01-01"}}""")
            .GetProperty("result");

        Assert.Equal(McpServer.LatestProtocolVersion, result.GetProperty("protocolVersion").GetString());
    }

    [Fact]
    public void Initialize_without_params_uses_the_newest_supported_version()
    {
        var result = Call(Server(), """{"jsonrpc":"2.0","id":1,"method":"initialize"}""").GetProperty("result");

        Assert.Equal(McpServer.LatestProtocolVersion, result.GetProperty("protocolVersion").GetString());
    }

    [Fact]
    public void Initialize_advertises_tools_and_instructions()
    {
        var result = Call(Server(), """{"jsonrpc":"2.0","id":1,"method":"initialize","params":{}}""").GetProperty("result");

        Assert.True(result.GetProperty("capabilities").TryGetProperty("tools", out _));
        Assert.Equal("es-mcp", result.GetProperty("serverInfo").GetProperty("name").GetString());
        Assert.Equal(ToolCatalog.Instructions, result.GetProperty("instructions").GetString());
    }

    // ---- tools/list -------------------------------------------------------------------------

    [Fact]
    public void Tools_list_advertises_both_tools_with_output_schemas()
    {
        var tools = Call(Server(), """{"jsonrpc":"2.0","id":1,"method":"tools/list"}""")
            .GetProperty("result").GetProperty("tools").EnumerateArray().ToArray();

        Assert.Equal(2, tools.Length);
        Assert.Equal("search_files", tools[0].GetProperty("name").GetString());
        Assert.Equal("everything_status", tools[1].GetProperty("name").GetString());

        foreach (var tool in tools)
        {
            Assert.True(tool.TryGetProperty("outputSchema", out var output), $"{tool.GetProperty("name")} has no outputSchema");
            Assert.Equal("object", output.GetProperty("type").GetString());
            Assert.True(tool.GetProperty("annotations").GetProperty("readOnlyHint").GetBoolean());
        }
    }

    // ---- tools/call: search -----------------------------------------------------------------

    [Fact]
    public void Search_returns_text_and_structured_content()
    {
        var server = Server(_ => new SearchResults(2, [File(@"C:\a\b.txt"), File(@"C:\a\src", folder: true)]));

        var result = SearchCall(server, """{"query":"*.txt"}""").GetProperty("result");

        Assert.False(result.GetProperty("isError").GetBoolean());
        Assert.StartsWith("2 results", result.GetProperty("content")[0].GetProperty("text").GetString(), StringComparison.Ordinal);

        var structured = result.GetProperty("structuredContent");
        Assert.Equal(2, structured.GetProperty("total").GetInt32());
        Assert.Equal(2, structured.GetProperty("returned").GetInt32());
        Assert.Equal(0, structured.GetProperty("offset").GetInt32());
        Assert.False(structured.GetProperty("hasMore").GetBoolean());
        Assert.Equal(JsonValueKind.Null, structured.GetProperty("nextOffset").ValueKind);

        var rows = structured.GetProperty("results").EnumerateArray().ToArray();
        Assert.Equal(@"C:\a\b.txt", rows[0].GetProperty("path").GetString());
        Assert.Equal("b.txt", rows[0].GetProperty("name").GetString());
        Assert.False(rows[0].GetProperty("folder").GetBoolean());
        Assert.Equal("txt", rows[0].GetProperty("ext").GetString());
        Assert.Equal(1024, rows[0].GetProperty("size").GetInt64());
        Assert.NotNull(rows[0].GetProperty("modified").GetString());
        Assert.NotNull(rows[0].GetProperty("created").GetString());
        Assert.NotNull(rows[0].GetProperty("accessed").GetString());

        Assert.True(rows[1].GetProperty("folder").GetBoolean());
        Assert.Equal(JsonValueKind.Null, rows[1].GetProperty("size").ValueKind);
        Assert.Equal(JsonValueKind.Null, rows[1].GetProperty("ext").ValueKind);
    }

    [Fact]
    public void Search_reports_paging_with_nextOffset()
    {
        var server = Server(request => new SearchResults(10, [File(@"C:\a\1.txt"), File(@"C:\a\2.txt")]));

        var structured = SearchCall(server, """{"query":"x","limit":2}""")
            .GetProperty("result").GetProperty("structuredContent");

        Assert.True(structured.GetProperty("hasMore").GetBoolean());
        Assert.Equal(2, structured.GetProperty("nextOffset").GetInt32());
    }

    [Fact]
    public void Second_page_continues_the_offset()
    {
        var server = Server(_ => new SearchResults(10, [File(@"C:\a\3.txt")]));

        var structured = SearchCall(server, """{"query":"x","limit":2,"offset":2}""")
            .GetProperty("result").GetProperty("structuredContent");

        Assert.Equal(2, structured.GetProperty("offset").GetInt32());
        Assert.Equal(3, structured.GetProperty("nextOffset").GetInt32());
        Assert.True(structured.GetProperty("hasMore").GetBoolean());
    }

    [Fact]
    public void Invalid_arguments_are_an_error_without_structured_content()
    {
        var result = SearchCall(Server(), """{}""").GetProperty("result");

        Assert.True(result.GetProperty("isError").GetBoolean());
        Assert.Equal(ToolArguments.MissingQueryError, result.GetProperty("content")[0].GetProperty("text").GetString());
        Assert.False(result.TryGetProperty("structuredContent", out _));
    }

    [Fact]
    public void Legacy_tool_name_still_works()
    {
        var server = Server(_ => new SearchResults(1, [File(@"C:\a\b.txt")]));

        var result = SearchCall(server, """{"query":"x"}""", tool: "search").GetProperty("result");

        Assert.False(result.GetProperty("isError").GetBoolean());
        Assert.Equal(1, result.GetProperty("structuredContent").GetProperty("total").GetInt32());
    }

    [Fact]
    public void A_missing_sdk_is_reported_as_a_tool_error()
    {
        var server = Server(_ => throw new DllNotFoundException("Everything64.dll"));

        var result = SearchCall(server, """{"query":"x"}""").GetProperty("result");

        Assert.True(result.GetProperty("isError").GetBoolean());
        Assert.Contains("Everything64.dll", result.GetProperty("content")[0].GetProperty("text").GetString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Everything_not_running_tells_the_agent_what_to_do()
    {
        var server = Server(_ => throw new EverythingException(2));

        var text = SearchCall(server, """{"query":"x"}""")
            .GetProperty("result").GetProperty("content")[0].GetProperty("text").GetString();

        Assert.Contains("start voidtools Everything", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Unknown_tool_is_a_jsonrpc_error()
    {
        var response = Call(Server(), """{"jsonrpc":"2.0","id":1,"method":"tools/call","params":{"name":"nope","arguments":{}}}""");

        Assert.Equal(-32602, response.GetProperty("error").GetProperty("code").GetInt32());
        Assert.Contains("search_files", response.GetProperty("error").GetProperty("message").GetString(), StringComparison.Ordinal);
    }

    // ---- tools/call: status -----------------------------------------------------------------

    [Fact]
    public void Status_tool_reports_a_ready_index()
    {
        var result = SearchCall(Server(), "{}", tool: "everything_status").GetProperty("result");

        Assert.False(result.GetProperty("isError").GetBoolean());
        Assert.Contains("Everything is ready", result.GetProperty("content")[0].GetProperty("text").GetString(), StringComparison.Ordinal);

        var structured = result.GetProperty("structuredContent");
        Assert.True(structured.GetProperty("ready").GetBoolean());
        Assert.True(structured.GetProperty("sdkLoaded").GetBoolean());
        Assert.True(structured.GetProperty("databaseLoaded").GetBoolean());
        Assert.Equal(1234, structured.GetProperty("totalCount").GetInt32());
        Assert.Equal(JsonValueKind.Null, structured.GetProperty("error").ValueKind);
    }

    [Fact]
    public void Status_tool_reports_an_unready_index_as_an_error()
    {
        var server = Server(status: () => new EverythingStatus(false, false, null, null, 0, "Everything64.dll was not found."));

        var result = SearchCall(server, "{}", tool: "everything_status").GetProperty("result");

        Assert.True(result.GetProperty("isError").GetBoolean());
        Assert.Contains("Everything is not ready", result.GetProperty("content")[0].GetProperty("text").GetString(), StringComparison.Ordinal);

        var structured = result.GetProperty("structuredContent");
        Assert.False(structured.GetProperty("ready").GetBoolean());
        Assert.Contains("was not found", structured.GetProperty("error").GetString(), StringComparison.Ordinal);
    }

    // ---- protocol basics --------------------------------------------------------------------

    [Fact]
    public void Ping_returns_an_empty_result()
    {
        var response = Call(Server(), """{"jsonrpc":"2.0","id":7,"method":"ping"}""");

        Assert.Equal(7, response.GetProperty("id").GetInt32());
        Assert.Equal(JsonValueKind.Object, response.GetProperty("result").ValueKind);
    }

    [Theory]
    [InlineData("""{"jsonrpc":"2.0","method":"notifications/initialized"}""")]
    [InlineData("""{"jsonrpc":"2.0","method":"notifications/cancelled","params":{}}""")]
    [InlineData("   ")]
    [InlineData("""{"jsonrpc":"2.0","id":1,"result":{}}""")]
    public void Messages_that_need_no_answer_get_none(string line)
    {
        Assert.Null(Server().HandleLine(line));
    }

    [Fact]
    public void A_broken_line_gets_a_parse_error_with_a_null_id()
    {
        var response = Call(Server(), "{not json");

        Assert.Equal(JsonValueKind.Null, response.GetProperty("id").ValueKind);
        Assert.Equal(-32700, response.GetProperty("error").GetProperty("code").GetInt32());
    }

    [Fact]
    public void Unknown_methods_are_rejected_and_logged_to_stderr()
    {
        var messages = new List<string>();
        var server = Server(log: messages.Add);

        var response = Call(server, """{"jsonrpc":"2.0","id":1,"method":"tools/frobnicate"}""");

        Assert.Equal(-32601, response.GetProperty("error").GetProperty("code").GetInt32());
        Assert.Single(messages);
    }
}
