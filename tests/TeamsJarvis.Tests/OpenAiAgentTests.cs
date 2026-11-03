using System.Text.Json;
using TeamsJarvis.Core;

namespace TeamsJarvis.Tests;

public class ParseArgsTests
{
    [Fact]
    public void ParsesObjectArguments()
    {
        var args = OpenAiAgent.ParseArgs("""{"query": "pipeline ventas", "count": 5}""");
        Assert.Equal("pipeline ventas", args["query"].GetString());
        Assert.Equal(5, args["count"].GetInt32());
    }

    [Fact]
    public void ElementsSurviveAfterParse()
    {
        // Los JsonElement deben ser válidos tras liberar el JsonDocument interno (clonados).
        var args = OpenAiAgent.ParseArgs("""{"path": "/workspaces"}""");
        // Forzar uso posterior:
        var value = args["path"].GetString();
        Assert.Equal("/workspaces", value);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not json")]
    [InlineData("[1,2,3]")]  // no es objeto
    public void HandlesInvalidOrNonObjectGracefully(string input)
    {
        var args = OpenAiAgent.ParseArgs(input);
        Assert.Empty(args);
    }
}

public class MultiProviderCostTests
{
    [Fact]
    public void KnowsOpenAiAndGeminiModels()
    {
        Assert.True(CostEstimator.IsKnownModel("gpt-4o"));
        Assert.True(CostEstimator.IsKnownModel("gemini-2.0-flash"));
    }

    [Fact]
    public void Gpt4oCostIsComputed()
    {
        // gpt-4o: 1M in (2.50) + 1M out (10.00) = 12.50
        var cost = CostEstimator.EstimateUsd("gpt-4o", new TokenUsage(1_000_000, 1_000_000, 0, 0));
        Assert.Equal(12.50, cost, precision: 4);
    }

    [Fact]
    public void GeminiFlashIsCheap()
    {
        var flash = CostEstimator.EstimateUsd("gemini-2.0-flash", new TokenUsage(1_000_000, 1_000_000, 0, 0));
        var opus = CostEstimator.EstimateUsd("claude-opus-4-8", new TokenUsage(1_000_000, 1_000_000, 0, 0));
        Assert.True(flash < opus);
    }
}
