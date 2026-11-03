using System.Text.Json;
using Microsoft.Extensions.Logging;
using TeamsJarvis.Tools;

namespace TeamsJarvis.Core;

/// <summary>
/// Agente de SIMULACIÓN: ejecuta el mismo flujo (triaje + análisis con bucle de tools) pero
/// SIN llamar a ninguna API. Estima el uso de tokens a partir de los prompts reales para
/// calcular costes realistas y detectar bugs/bucles/rendimiento sin gastar dinero.
/// Se activa con Provider = "simulation".
/// </summary>
public sealed class SimulatedAgent : ILlmAgent
{
    private const int SimulatedToolIterations = 3; // típico: Fabric + Azure + una comprobación más
    private const int ToolResultTokens = 1500;     // tamaño medio de un resultado de tool (JSON)
    private const int AssistantStepTokens = 250;   // texto + llamadas a tool por iteración
    private const int VerdictTokens = 500;         // JSON de veredicto final

    private readonly AgentPromptBuilder _prompt;
    private readonly ILogger<SimulatedAgent> _logger;

    public SimulatedAgent(AgentPromptBuilder prompt, ILogger<SimulatedAgent> logger)
    {
        _prompt = prompt;
        _logger = logger;
    }

    public Task<string> SelfTestAsync(CancellationToken ct = default) =>
        Task.FromResult("OK (simulación)");

    public Task<(bool ShouldReply, string Reason, TokenUsage Usage)> TriageAsync(
        ChatContext context, List<ChatMessageDto> newMessages, CancellationToken ct = default)
    {
        var inputTokens = TokenEstimator.Text(_prompt.TriageSystemPrompt())
                        + TokenEstimator.Text(_prompt.TriageUserText(context, newMessages));
        var usage = new TokenUsage(inputTokens, 10, 0, 0);

        // Heurística determinista: ~55% de los mensajes merecen respuesta.
        var last = newMessages.LastOrDefault()?.Text ?? "";
        var shouldReply = Math.Abs(last.GetHashCode()) % 100 < 55;
        var reason = shouldReply
            ? "(simulación) El mensaje parece pedirte algo o ir dirigido a ti."
            : "(simulación) Parece conversación sin una petición directa para ti.";
        return Task.FromResult((shouldReply, reason, usage));
    }

    public Task<(AgentVerdict Verdict, TokenUsage Usage)> AnalyzeAsync(
        ChatContext context, string previousMemory, List<ChatMessageDto> newMessages,
        IProgress<string>? progress = null, CancellationToken ct = default)
    {
        var systemTokens = TokenEstimator.Text(_prompt.SystemPrompt());
        var toolSchemaTokens = _prompt.Tools().Sum(ToolSchemaTokens);
        var baseUserTokens = TokenEstimator.Text(_prompt.UserText(context, previousMemory, newMessages))
                           + TokenEstimator.Images(AgentPromptBuilder.Images(context).Count);

        long inTokens = 0, outTokens = 0, cacheRead = 0, cacheWrite = 0;
        var accumulatedContext = 0; // resultados de tools + pasos assistant que se van acumulando

        var iterations = Math.Min(SimulatedToolIterations, Math.Max(1, _prompt.Options.MaxToolIterations));
        for (var i = 0; i < iterations; i++)
        {
            // El system prompt se cachea: se escribe la primera vez, se lee después.
            if (i == 0) cacheWrite += systemTokens;
            else cacheRead += systemTokens;

            inTokens += toolSchemaTokens + baseUserTokens + accumulatedContext;

            var isLast = i == iterations - 1;
            outTokens += isLast ? VerdictTokens : AssistantStepTokens;
            if (!isLast) accumulatedContext += ToolResultTokens + AssistantStepTokens;
        }

        var usage = new TokenUsage(inTokens, outTokens, cacheRead, cacheWrite);
        var cost = CostEstimator.EstimateUsd(_prompt.Options.Model, usage);
        _logger.LogInformation("Simulación para '{Chat}': {In}/{Out} tokens ≈ ${Cost:0.000}.",
            context.ChatName, inTokens, outTokens, cost);

        // Respuesta simulada contextual (referencia al último mensaje y al interlocutor),
        // para que se vea cómo quedaría y el coste estimado, sin llamar a ninguna API.
        var lastIncoming = newMessages.LastOrDefault(m => !m.IsMe);
        var who = lastIncoming?.AuthorName is { Length: > 0 } a ? a : "el interlocutor";
        var about = ContextBuilder.Truncate(lastIncoming?.Text ?? "", 120);
        var reply =
            $"Hola {who}, sobre «{about}»: [aquí la IA redactaría la respuesta usando el contexto de la " +
            $"conversación y, si hiciera falta, datos reales de Fabric/Azure/SharePoint].\n\n" +
            $"— RESPUESTA SIMULADA: no se ha llamado a ninguna API. Coste real estimado de esta respuesta " +
            $"≈ ${cost:0.000} con el modelo {_prompt.Options.Model}.";

        var verdict = new AgentVerdict(
            ShouldReply: true,
            SuggestedReply: reply,
            Confidence: 0.5,
            Sources: $"Simulación · modelo {_prompt.Options.Model} · esta respuesta ≈ ${cost:0.000} " +
                     $"({inTokens} tokens entrada / {outTokens} salida). Acumulado en 💶 Costes.",
            Reasoning: "modo simulación para estimar consumos sin gastar",
            ConversationMemory: previousMemory);
        return Task.FromResult((verdict, usage));
    }

    private static int ToolSchemaTokens(IAgentTool tool) =>
        TokenEstimator.Text(tool.Name + tool.Description + JsonSerializer.Serialize(tool.SchemaProperties));
}
