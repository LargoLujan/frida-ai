using TeamsJarvis.Core;

namespace TeamsJarvis.Tests;

public class CostEstimatorTests
{
    [Fact]
    public void EstimatesOpusCostByComponent()
    {
        // 1M entrada + 1M salida en Opus 4.8 = 5 + 25 = 30 USD
        var usage = new TokenUsage(1_000_000, 1_000_000, 0, 0);
        var cost = CostEstimator.EstimateUsd("claude-opus-4-8", usage);
        Assert.Equal(30.0, cost, precision: 4);
    }

    [Fact]
    public void CacheReadIsMuchCheaperThanInput()
    {
        var input = CostEstimator.EstimateUsd("claude-opus-4-8", new TokenUsage(1_000_000, 0, 0, 0));
        var cacheRead = CostEstimator.EstimateUsd("claude-opus-4-8", new TokenUsage(0, 0, 1_000_000, 0));
        Assert.True(cacheRead < input);
        Assert.Equal(0.50, cacheRead, precision: 4); // ~0,1x del precio de entrada
    }

    [Fact]
    public void SumsAllComponents()
    {
        // Opus: 0.5M in (2.5) + 0.2M out (5.0) + 1M cacheRead (0.5) + 0.1M cacheWrite (0.625) = 8.625
        var usage = new TokenUsage(500_000, 200_000, 1_000_000, 100_000);
        var cost = CostEstimator.EstimateUsd("claude-opus-4-8", usage);
        Assert.Equal(8.625, cost, precision: 4);
    }

    [Fact]
    public void HaikuCheaperThanSonnetCheaperThanOpus()
    {
        var usage = new TokenUsage(1_000_000, 1_000_000, 0, 0);
        var haiku = CostEstimator.EstimateUsd("claude-haiku-4-5", usage);
        var sonnet = CostEstimator.EstimateUsd("claude-sonnet-5", usage);
        var opus = CostEstimator.EstimateUsd("claude-opus-4-8", usage);
        Assert.True(haiku < sonnet);
        Assert.True(sonnet < opus);
    }

    [Fact]
    public void UnknownModelUsesConservativeFallback()
    {
        Assert.False(CostEstimator.IsKnownModel("modelo-inventado"));
        var cost = CostEstimator.EstimateUsd("modelo-inventado", new TokenUsage(1_000_000, 0, 0, 0));
        Assert.Equal(5.0, cost, precision: 4); // fallback = precio de Opus
    }

    [Fact]
    public void ZeroUsageIsZeroCost()
    {
        Assert.Equal(0.0, CostEstimator.EstimateUsd("claude-opus-4-8", new TokenUsage(0, 0, 0, 0)));
    }

    [Theory]
    [InlineData("llama3.2:3b")]
    [InlineData("qwen2.5:7b")]
    [InlineData("gemma2:2b")]
    [InlineData("nomic-embed-text")]
    public void LocalModelsAreFree(string model)
    {
        Assert.True(CostEstimator.IsLocal(model));
        // Aunque consuman muchos tokens, coste = 0 (corren en tu PC).
        Assert.Equal(0.0, CostEstimator.EstimateUsd(model, new TokenUsage(1_000_000, 1_000_000, 0, 0)));
    }

    [Fact]
    public void CloudModelsAreNotLocal()
    {
        Assert.False(CostEstimator.IsLocal("claude-opus-4-8"));
        Assert.False(CostEstimator.IsLocal("gpt-4o"));
    }
}
