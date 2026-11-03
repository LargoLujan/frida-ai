namespace TeamsJarvis.Core;

/// <summary>Devuelve el agente (ILlmAgent) de un proveedor concreto. Permite usar un proveedor
/// distinto para el triaje (p. ej. Ollama local, gratis) y para la redacción (nube).</summary>
public sealed class AgentSelector
{
    private readonly ClaudeAgent _claude;
    private readonly OpenAiAgent _openai;
    private readonly GeminiAgent _gemini;
    private readonly OllamaAgent _ollama;
    private readonly SimulatedAgent _simulation;

    public AgentSelector(ClaudeAgent claude, OpenAiAgent openai, GeminiAgent gemini,
        OllamaAgent ollama, SimulatedAgent simulation)
    {
        _claude = claude;
        _openai = openai;
        _gemini = gemini;
        _ollama = ollama;
        _simulation = simulation;
    }

    public ILlmAgent For(string provider) => provider.ToLowerInvariant() switch
    {
        "openai" => _openai,
        "gemini" => _gemini,
        "ollama" => _ollama,
        "simulation" => _simulation,
        _ => _claude,
    };
}
