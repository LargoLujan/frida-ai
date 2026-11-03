using TeamsJarvis.Core;

namespace TeamsJarvis.Tests;

public class McpConfigParserTests
{
    [Fact]
    public void ParsesStandardMcpServersObject()
    {
        var json = """
            {
              "mcpServers": {
                "fabric-lineage-remote": {
                  "command": "python3.exe",
                  "args": ["server.py", "--base-url", "http://127.0.0.1:8765"]
                }
              }
            }
            """;
        var servers = McpConfigParser.Parse(json);
        Assert.Single(servers);
        Assert.Equal("fabric-lineage-remote", servers[0].Name);
        Assert.Equal("stdio", servers[0].Type);
        Assert.Equal("python3.exe", servers[0].Command);
        Assert.Equal(3, servers[0].Arguments.Count);
        Assert.Equal("--base-url", servers[0].Arguments[1]);
    }

    [Fact]
    public void ParsesEnvVariables()
    {
        var json = """
            {"mcpServers": {"s": {"command": "node", "args": ["x.js"], "env": {"TOKEN": "abc", "MODE": "prod"}}}}
            """;
        var s = McpConfigParser.Parse(json)[0];
        Assert.Equal("abc", s.Env["TOKEN"]);
        Assert.Equal("prod", s.Env["MODE"]);
    }

    [Fact]
    public void InfersHttpWhenUrlPresent()
    {
        var s = McpConfigParser.Parse("""{"mcpServers": {"remote": {"url": "https://example.com/mcp"}}}""")[0];
        Assert.Equal("http", s.Type);
        Assert.Equal("https://example.com/mcp", s.Url);
    }

    [Fact]
    public void ParsesBareMap()
    {
        var s = McpConfigParser.Parse("""{"azure": {"command": "npx", "args": ["-y", "@azure/mcp"]}}""");
        Assert.Single(s);
        Assert.Equal("azure", s[0].Name);
    }

    [Fact]
    public void ParsesNativeArray()
    {
        var s = McpConfigParser.Parse("""[{"Name": "x", "Type": "stdio", "Command": "cmd", "Arguments": ["a"]}]""");
        Assert.Single(s);
        Assert.Equal("x", s[0].Name);
        Assert.Equal("cmd", s[0].Command);
    }

    [Fact]
    public void EmptyOrWhitespaceIsEmptyList()
    {
        Assert.Empty(McpConfigParser.Parse(""));
        Assert.Empty(McpConfigParser.Parse("   "));
    }

    [Fact]
    public void InvalidJsonThrowsFormatException()
    {
        Assert.Throws<FormatException>(() => McpConfigParser.Parse("{not json"));
    }

    [Fact]
    public void RoundTripsToStandardJson()
    {
        var json = """{"mcpServers": {"s": {"command": "node", "args": ["x.js"]}}}""";
        var parsed = McpConfigParser.Parse(json);
        var back = McpConfigParser.ToStandardJson(parsed);
        Assert.Contains("mcpServers", back);
        Assert.Contains("\"s\"", back);
        // Y se puede volver a parsear.
        Assert.Single(McpConfigParser.Parse(back));
    }
}
