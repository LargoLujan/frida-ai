namespace TeamsJarvis.Core;

/// <summary>Precio de un modelo en USD por 1 millón de tokens.</summary>
public readonly record struct ModelPricing(
    double InputPerMTok,
    double OutputPerMTok,
    double CacheReadPerMTok,
    double CacheWritePerMTok);

/// <summary>Uso de tokens de una llamada al LLM.</summary>
public readonly record struct TokenUsage(
    long InputTokens,
    long OutputTokens,
    long CacheReadTokens,
    long CacheWriteTokens);

/// <summary>
/// Estima el coste en USD del uso de la API de IA. Precios de referencia (por 1M tokens)
/// verificados contra la documentación de precios de Anthropic; ajustables si cambian.
/// Aislado para poder testearlo.
/// </summary>
public static class CostEstimator
{
    // Precios de referencia. Nota: son de la API (pay-as-you-go), NO de la suscripción Max.
    private static readonly Dictionary<string, ModelPricing> Prices = new(StringComparer.OrdinalIgnoreCase)
    {
        // Anthropic (entrada / salida / lectura-caché ~0,1x / escritura-caché ~1,25x)
        ["claude-opus-4-8"] = new(5.00, 25.00, 0.50, 6.25),
        ["claude-opus-4-7"] = new(5.00, 25.00, 0.50, 6.25),
        ["claude-sonnet-5"] = new(3.00, 15.00, 0.30, 3.75),
        ["claude-sonnet-4-6"] = new(3.00, 15.00, 0.30, 3.75),
        ["claude-haiku-4-5"] = new(1.00, 5.00, 0.10, 1.25),
        ["claude-fable-5"] = new(10.00, 50.00, 1.00, 12.50),

        // OpenAI (entrada / salida / entrada-cacheada; sin coste de escritura de caché)
        ["gpt-4o"] = new(2.50, 10.00, 1.25, 0.0),
        ["gpt-4o-mini"] = new(0.15, 0.60, 0.075, 0.0),
        ["gpt-4.1"] = new(2.00, 8.00, 0.50, 0.0),
        ["gpt-4.1-mini"] = new(0.40, 1.60, 0.10, 0.0),
        ["o4-mini"] = new(1.10, 4.40, 0.275, 0.0),

        // Google Gemini
        ["gemini-2.0-flash"] = new(0.10, 0.40, 0.025, 0.0),
        ["gemini-1.5-pro"] = new(1.25, 5.00, 0.3125, 0.0),
        ["gemini-1.5-flash"] = new(0.075, 0.30, 0.01875, 0.0),
    };

    // Si el modelo no está en la tabla, se usa este precio conservador (el de Opus).
    private static readonly ModelPricing Fallback = new(5.00, 25.00, 0.50, 6.25);

    // Modelos locales (Ollama): coste 0. Se detectan por nombre.
    private static readonly string[] LocalMarkers =
        ["llama", "qwen", "gemma", "phi", "mistral", "nomic", "sim-", "deepseek", "codellama"];

    public static bool IsLocal(string model) =>
        LocalMarkers.Any(m => model.Contains(m, StringComparison.OrdinalIgnoreCase));

    public static ModelPricing GetPricing(string model) =>
        Prices.TryGetValue(model, out var p) ? p : Fallback;

    public static bool IsKnownModel(string model) => Prices.ContainsKey(model);

    /// <summary>Coste en USD de una llamada.</summary>
    public static double EstimateUsd(string model, TokenUsage usage)
    {
        if (IsLocal(model)) return 0.0; // LLM local = gratis
        var p = GetPricing(model);
        return usage.InputTokens / 1_000_000.0 * p.InputPerMTok
             + usage.OutputTokens / 1_000_000.0 * p.OutputPerMTok
             + usage.CacheReadTokens / 1_000_000.0 * p.CacheReadPerMTok
             + usage.CacheWriteTokens / 1_000_000.0 * p.CacheWritePerMTok;
    }
}
