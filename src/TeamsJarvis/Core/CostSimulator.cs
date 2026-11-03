namespace TeamsJarvis.Core;

/// <summary>Configuración de una simulación de coste mensual.</summary>
public record SimConfig
{
    /// <summary>Mensajes entrantes (de otros) a analizar por día laborable.</summary>
    public int IncomingPerWorkday { get; init; } = 25;
    public int Workdays { get; init; } = 22;
    /// <summary>Fracción de mensajes que el triaje considera dignos de respuesta (0-1).</summary>
    public double ReplyRatio { get; init; } = 0.55;
    public bool TriageEnabled { get; init; } = true;

    public required string DeepModel { get; init; }
    public required string TriageModel { get; init; }

    // Tamaños representativos (tokens) del flujo real, incluyendo Fabric/Azure como contexto.
    public int SystemTokens { get; init; } = 900;
    public int ToolSchemaTokens { get; init; } = 1200;
    public int UserContextTokens { get; init; } = 1600;   // transcripción + memoria
    public int TriageTokens { get; init; } = 400;         // prompt de triaje (in)
    public int ToolIterations { get; init; } = 3;         // Fabric + Azure + 1
    public int ToolResultTokens { get; init; } = 1500;
    public int AssistantStepTokens { get; init; } = 250;
    public int VerdictTokens { get; init; } = 500;
    public bool CacheSystemPrompt { get; init; } = true;  // solo Claude cachea el system prompt
}

/// <summary>Resultado de una simulación mensual.</summary>
public record SimReport(
    string Label,
    int MessagesAnalyzed,
    int DeepRuns,
    long TotalInputTokens,
    long TotalOutputTokens,
    double TriageCostUsd,
    double DeepCostUsd)
{
    public double TotalCostUsd => TriageCostUsd + DeepCostUsd;
}

/// <summary>
/// Calcula el coste mensual estimado de un patrón de uso, modelando el triaje (modelo barato)
/// y el análisis profundo con bucle de tools (Fabric/Azure) y caché del system prompt.
/// </summary>
public static class CostSimulator
{
    public static SimReport Simulate(SimConfig c, string label)
    {
        var messages = c.IncomingPerWorkday * c.Workdays;

        // Coste de triaje (una llamada barata por mensaje entrante).
        double triageCost = 0;
        if (c.TriageEnabled)
        {
            var triageUsage = new TokenUsage(c.TriageTokens, 15, 0, 0);
            triageCost = CostEstimator.EstimateUsd(c.TriageModel, triageUsage) * messages;
        }

        // Cuántos llegan al análisis profundo.
        var deepRuns = c.TriageEnabled ? (int)Math.Round(messages * c.ReplyRatio) : messages;

        var deepUsage = SimulateDeepUsage(c);
        var deepCostPer = CostEstimator.EstimateUsd(c.DeepModel, deepUsage);
        var deepCost = deepCostPer * deepRuns;

        long totalIn = (c.TriageEnabled ? (long)c.TriageTokens * messages : 0) + deepUsage.InputTokens * deepRuns;
        long totalOut = (c.TriageEnabled ? 15L * messages : 0) + deepUsage.OutputTokens * deepRuns;

        return new SimReport(label, messages, deepRuns, totalIn, totalOut, triageCost, deepCost);
    }

    /// <summary>Uso de tokens de un análisis profundo (bucle de tools con caché del system prompt).</summary>
    public static TokenUsage SimulateDeepUsage(SimConfig c)
    {
        long inTokens = 0, outTokens = 0, cacheRead = 0, cacheWrite = 0;
        var accumulated = 0;
        var iterations = Math.Max(1, c.ToolIterations);

        for (var i = 0; i < iterations; i++)
        {
            if (c.CacheSystemPrompt)
            {
                if (i == 0) cacheWrite += c.SystemTokens;
                else cacheRead += c.SystemTokens;
            }
            else
            {
                inTokens += c.SystemTokens; // sin caché: el system prompt entra a precio completo cada vez
            }

            inTokens += c.ToolSchemaTokens + c.UserContextTokens + accumulated;

            var isLast = i == iterations - 1;
            outTokens += isLast ? c.VerdictTokens : c.AssistantStepTokens;
            if (!isLast) accumulated += c.ToolResultTokens + c.AssistantStepTokens;
        }

        return new TokenUsage(inTokens, outTokens, cacheRead, cacheWrite);
    }
}
