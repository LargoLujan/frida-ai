using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Options;

namespace TeamsJarvis.Core;

/// <summary>
/// LLM local (Ollama) para tareas auxiliares GRATIS: resúmenes de puesta al día y
/// extracción de tareas. No consume tokens de pago. Degrada con gracia (devuelve null)
/// si Ollama no está disponible, para que la app siga funcionando.
/// </summary>
public sealed class LocalLlm
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromMinutes(3) };
    private readonly JarvisOptions _options;

    public LocalLlm(IOptions<JarvisOptions> options) => _options = options.Value;

    private string BaseUrl => _options.OllamaBaseUrl.TrimEnd('/');

    /// <summary>Modelo local pequeño para estas tareas (el de triaje si es Ollama, si no uno por defecto).</summary>
    public string Model =>
        _options.TriageProvider.Equals("ollama", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(_options.TriageModel)
            ? _options.TriageModel
            : "llama3.2:3b";

    /// <summary>Resume la jornada a partir del panel de atención. Devuelve null si no hay LLM local.</summary>
    public async Task<string?> SummarizeDayAsync(AttentionReport report, CancellationToken ct = default)
    {
        if (report.Total == 0) return "Hoy no tienes menciones, preguntas ni recordatorios pendientes. 🎉";

        var sb = new StringBuilder();
        sb.AppendLine("Menciones:");
        foreach (var m in report.Mentions) sb.AppendLine($"- [{m.ChatName}] {m.Title}: {m.Detail}");
        sb.AppendLine("Preguntas pendientes:");
        foreach (var q in report.Questions) sb.AppendLine($"- [{q.ChatName}] {q.Title}: {q.Detail}");
        sb.AppendLine("Recordatorios:");
        foreach (var r in report.Reminders) sb.AppendLine($"- {r.Title} ({r.Detail})");

        const string system =
            "Eres el asistente del usuario. Resume en español, en pocas frases claras y con viñetas, " +
            "lo que requiere su atención hoy y qué debería priorizar. No inventes nada: usa solo lo que se te da.";
        return await ChatAsync(system, sb.ToString(), 500, ct);
    }

    /// <summary>
    /// Genera una FICHA DE MEMORIA estructurada de una conversación con el modelo local (gratis).
    /// Sin inventar: solo lo que aparezca en la transcripción. Null si Ollama no responde.
    /// </summary>
    public async Task<string?> SummarizeConversationMemoryAsync(string chatName, string transcript, CancellationToken ct = default)
    {
        const string system =
            "Eres un asistente que resume conversaciones de trabajo de Microsoft Teams en una FICHA DE MEMORIA " +
            "estructurada, en español, de forma natural y SIN inventar nada (usa solo lo que aparezca). " +
            "No uses emojis ni markdown.";
        var user =
            $"Conversación: {chatName}\n\n{transcript}\n\n" +
            "Devuelve una ficha con estas secciones (deja vacía la que no aplique, no la inventes):\n" +
            "TEMAS:\nDECISIONES Y ACUERDOS:\nPENDIENTES Y TAREAS:\nPERSONAS (quién es quién):\nFECHAS E HITOS:\n" +
            "Sé conciso, máximo 1200 caracteres.";
        return await ChatAsync(system, user, 700, ct);
    }

    /// <summary>Chat local sencillo. Null si Ollama no responde.</summary>
    public async Task<string?> ChatAsync(string system, string user, int maxTokens, CancellationToken ct = default)
    {
        try
        {
            var body = new JsonObject
            {
                ["model"] = Model,
                ["stream"] = false,
                ["messages"] = new JsonArray(
                    new JsonObject { ["role"] = "system", ["content"] = system },
                    new JsonObject { ["role"] = "user", ["content"] = user }),
                ["options"] = new JsonObject { ["num_predict"] = maxTokens },
            };
            using var req = new HttpRequestMessage(HttpMethod.Post, $"{BaseUrl}/api/chat")
            {
                Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json"),
            };
            using var resp = await Http.SendAsync(req, ct);
            if (!resp.IsSuccessStatusCode) return null;
            var raw = await resp.Content.ReadAsStringAsync(ct);
            var text = JsonNode.Parse(raw)?["message"]?["content"]?.GetValue<string>();
            return string.IsNullOrWhiteSpace(text) ? null : text.Trim();
        }
        catch
        {
            return null;
        }
    }
}
