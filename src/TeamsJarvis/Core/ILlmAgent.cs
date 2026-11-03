namespace TeamsJarvis.Core;

/// <summary>
/// Agente de IA que analiza una conversación y devuelve un veredicto con la respuesta
/// sugerida y el uso de tokens. Implementado por cada proveedor (Claude, OpenAI, Gemini).
/// </summary>
public interface ILlmAgent
{
    /// <summary>Prueba mínima de conectividad con el proveedor (para diagnóstico).</summary>
    Task<string> SelfTestAsync(CancellationToken ct = default);

    /// <summary>
    /// Triaje barato: decide si los mensajes nuevos merecen respuesta (con un motivo breve), sin
    /// usar tools ni el modelo potente. Ahorra el análisis completo en los mensajes que no lo necesitan.
    /// </summary>
    Task<(bool ShouldReply, string Reason, TokenUsage Usage)> TriageAsync(
        ChatContext context, List<ChatMessageDto> newMessages, CancellationToken ct = default);

    /// <summary>
    /// Analiza los mensajes nuevos con acceso a las tools y emite el veredicto.
    /// <paramref name="progress"/> (opcional) recibe mensajes de estado en vivo para la UI
    /// (p. ej. "Consultando Fabric…", "Redactando…").
    /// </summary>
    Task<(AgentVerdict Verdict, TokenUsage Usage)> AnalyzeAsync(
        ChatContext context,
        string previousMemory,
        List<ChatMessageDto> newMessages,
        IProgress<string>? progress = null,
        CancellationToken ct = default);
}
