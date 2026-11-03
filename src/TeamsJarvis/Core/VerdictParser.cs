using System.Text.Json;

namespace TeamsJarvis.Core;

/// <summary>
/// Parseo tolerante del veredicto JSON del agente. Aislado del servicio para poder
/// testearlo sin dependencias (API, logger).
/// </summary>
public static class VerdictParser
{
    /// <summary>
    /// Intenta extraer el objeto JSON del texto final del agente. Siempre devuelve un
    /// veredicto: si el parseo falla, uno degradado (el texto como sugerencia de baja
    /// confianza). El valor de retorno indica si el JSON se interpretó correctamente.
    /// </summary>
    public static bool TryParse(string finalText, out AgentVerdict verdict)
    {
        try
        {
            var start = finalText.IndexOf('{');
            var end = finalText.LastIndexOf('}');
            if (start < 0 || end <= start)
                throw new JsonException("La respuesta no contiene JSON.");

            using var doc = JsonDocument.Parse(finalText[start..(end + 1)]);
            var root = doc.RootElement;

            verdict = new AgentVerdict(
                ShouldReply: root.TryGetProperty("should_reply", out var sr) && sr.ValueKind == JsonValueKind.True,
                SuggestedReply: GetStringProp(root, "suggested_reply"),
                Confidence: root.TryGetProperty("confidence", out var c) && c.ValueKind == JsonValueKind.Number ? c.GetDouble() : 0,
                Sources: GetStringProp(root, "sources"),
                Reasoning: GetStringProp(root, "reasoning"),
                ConversationMemory: GetStringProp(root, "conversation_memory"));
            return true;
        }
        catch
        {
            verdict = new AgentVerdict(
                ShouldReply: !string.IsNullOrWhiteSpace(finalText),
                SuggestedReply: finalText.Trim(),
                Confidence: 0.2,
                Sources: "",
                Reasoning: "(el agente no devolvió el formato esperado)",
                ConversationMemory: "");
            return false;
        }
    }

    /// <summary>
    /// Interpreta la respuesta del triaje ({"reply": true|false}). Ante formato inesperado
    /// devuelve true (mejor analizar de más que perder un mensaje que sí requería respuesta).
    /// </summary>
    /// <summary>Triaje con motivo: {"reply": bool, "reason": "..."}. Reutiliza ShouldReplyTriage.</summary>
    public static (bool Reply, string Reason) ParseTriage(string text)
    {
        var reply = ShouldReplyTriage(text);
        var reason = "";
        try
        {
            var start = text.IndexOf('{');
            var end = text.LastIndexOf('}');
            if (start >= 0 && end > start)
            {
                using var doc = JsonDocument.Parse(text[start..(end + 1)]);
                reason = GetStringProp(doc.RootElement, "reason");
            }
        }
        catch { }
        return (reply, reason);
    }

    public static bool ShouldReplyTriage(string text)
    {
        try
        {
            var start = text.IndexOf('{');
            var end = text.LastIndexOf('}');
            if (start >= 0 && end > start)
            {
                using var doc = JsonDocument.Parse(text[start..(end + 1)]);
                if (doc.RootElement.TryGetProperty("reply", out var r))
                {
                    if (r.ValueKind == JsonValueKind.True) return true;
                    if (r.ValueKind == JsonValueKind.False) return false;
                }
            }
        }
        catch { }
        // Heurística de respaldo.
        var lower = text.ToLowerInvariant();
        if (lower.Contains("\"reply\": false") || lower.Contains("reply=false") || lower.Contains("false")) return false;
        return true;
    }

    private static string GetStringProp(JsonElement root, string name) =>
        root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? ""
            : "";
}
