using TeamsJarvis.Core;
using Xunit.Abstractions;

namespace TeamsJarvis.Tests;

public class CostSimulatorTests(ITestOutputHelper output)
{
    // Patrón del usuario: ~25 mensajes entrantes/día laborable, todos con contexto Fabric/Azure.
    private static SimConfig Base(string deep, string triage, bool triageOn) => new()
    {
        DeepModel = deep,
        TriageModel = triage,
        TriageEnabled = triageOn,
        CacheSystemPrompt = deep.StartsWith("claude"),
    };

    [Fact]
    public void PrintMonthlyCostTable()
    {
        var configs = new[]
        {
            ("Opus 4.8, SIN triaje", Base("claude-opus-4-8", "claude-haiku-4-5", false)),
            ("Opus 4.8 + triaje Haiku", Base("claude-opus-4-8", "claude-haiku-4-5", true)),
            ("Sonnet 5 + triaje Haiku", Base("claude-sonnet-5", "claude-haiku-4-5", true)),
            ("Haiku 4.5 + triaje Haiku", Base("claude-haiku-4-5", "claude-haiku-4-5", true)),
            ("Gemini Pro + triaje Flash", Base("gemini-1.5-pro", "gemini-2.0-flash", true)),
            ("Gemini Flash + triaje Flash", Base("gemini-2.0-flash", "gemini-2.0-flash", true)),
            ("gpt-4o + triaje 4o-mini", Base("gpt-4o", "gpt-4o-mini", true)),
        };

        output.WriteLine($"{"Configuración",-32} {"$/mes",8} {"deep",6} {"triaje$",9} {"deep$",9}");
        output.WriteLine(new string('-', 70));
        foreach (var (label, cfg) in configs)
        {
            var r = CostSimulator.Simulate(cfg, label);
            output.WriteLine($"{label,-32} {r.TotalCostUsd,8:0.00} {r.DeepRuns,6} {r.TriageCostUsd,9:0.00} {r.DeepCostUsd,9:0.00}");
        }
    }

    [Fact]
    public void TriageReducesCost()
    {
        var withTriage = CostSimulator.Simulate(Base("claude-opus-4-8", "claude-haiku-4-5", true), "con");
        var withoutTriage = CostSimulator.Simulate(Base("claude-opus-4-8", "claude-haiku-4-5", false), "sin");
        Assert.True(withTriage.TotalCostUsd < withoutTriage.TotalCostUsd);
        Assert.True(withTriage.DeepRuns < withoutTriage.DeepRuns);
    }

    [Fact]
    public void CheaperModelsCostLess()
    {
        var opus = CostSimulator.Simulate(Base("claude-opus-4-8", "claude-haiku-4-5", true), "opus");
        var sonnet = CostSimulator.Simulate(Base("claude-sonnet-5", "claude-haiku-4-5", true), "sonnet");
        var haiku = CostSimulator.Simulate(Base("claude-haiku-4-5", "claude-haiku-4-5", true), "haiku");
        Assert.True(sonnet.TotalCostUsd < opus.TotalCostUsd);
        Assert.True(haiku.TotalCostUsd < sonnet.TotalCostUsd);
    }

    [Fact]
    public void ToolLoopIsBounded()
    {
        // Aunque suban las iteraciones, el coste crece de forma acotada (no explota → sin bucles infinitos).
        var few = CostSimulator.SimulateDeepUsage(Base("claude-opus-4-8", "claude-haiku-4-5", true) with { ToolIterations = 3 });
        var many = CostSimulator.SimulateDeepUsage(Base("claude-opus-4-8", "claude-haiku-4-5", true) with { ToolIterations = 15 });
        Assert.True(many.InputTokens > few.InputTokens);
        Assert.True(many.InputTokens < few.InputTokens * 20); // crecimiento acotado
    }

    [Fact]
    public void CachingReducesFullPriceInput()
    {
        var cached = CostSimulator.SimulateDeepUsage(Base("claude-opus-4-8", "claude-haiku-4-5", true) with { CacheSystemPrompt = true });
        var uncached = CostSimulator.SimulateDeepUsage(Base("claude-opus-4-8", "claude-haiku-4-5", true) with { CacheSystemPrompt = false });
        // Con caché, parte del system prompt va como cache_read en vez de input a precio completo.
        Assert.True(cached.InputTokens < uncached.InputTokens);
        Assert.True(cached.CacheReadTokens > 0);
    }
}
