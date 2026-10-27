// es-mcp - Model Context Protocol (stdio) server that exposes Everything search as tools.
// Newline-delimited JSON-RPC 2.0 on stdin/stdout; stdout carries protocol traffic only,
// diagnostics go to stderr.
using System.Text;

namespace EsMcp;

internal static class Program
{
    private static int Main()
    {
        var input = new StreamReader(Console.OpenStandardInput(), new UTF8Encoding(false));
        var output = new StreamWriter(Console.OpenStandardOutput(), new UTF8Encoding(false)) { AutoFlush = true };

        var server = new McpServer();

        string? line;
        while ((line = input.ReadLine()) is not null)
        {
            var response = server.HandleLine(line);
            if (response is not null) output.WriteLine(response);
        }

        return 0;
    }
}
