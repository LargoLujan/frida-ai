using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using TeamsJarvis.Tools;

namespace TeamsJarvis.Core;

/// <summary>
/// Agente con LLM LOCAL vía Ollama (coste 0). Ideal para el triaje (rápido en CPU con modelos
/// pequeños). También puede redactar (single-shot, sin herramientas: no consulta Fabric/Azure).
/// </summary>
public sealed class OllamaAgent : ILlmAgent
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromMinutes(5) };
    private readonly AgentPromptBuilder _prompt;
    private readonly ILogger<OllamaAgent> _logger;

    public OllamaAgent(AgentPromptBuilder prompt, ILogger<OllamaAgent> logger)
    {
        _prompt = prompt;
        _logger = logger;
    }

    private string BaseUrl => _prompt.Options.OllamaBaseUrl.TrimEnd('/');
    private string DeepModel => string.IsNullOrWhiteSpace(_prompt.Options.Model) ? "llama3.1:8b" : _prompt.Options.Model;

    public async Task<string> SelfTestAsync(CancellationToken ct = default)
    {
        var reply = await ChatAsync(_prompt.TriageModel(), "Responde solo: OK", "di OK", 16, ct);
        return reply.Text;
    }

    public async Task<(bool ShouldReply, string Reason, TokenUsage Usage)> TriageAsync(
        ChatContext context, List<ChatMessageDto> newMessages, CancellationToken ct = default)
    {
        var (text, usage) = await ChatAsync(
            _prompt.TriageModel(), _prompt.TriageSystemPrompt(), _prompt.TriageUserText(context, newMessages), 64, ct);
        var __t = VerdictParser.ParseTriage(text); return (__t.Reply, __t.Reason, usage);
    }

    public async Task<(AgentVerdict Verdict, TokenUsage Usage)> AnalyzeAsync(
        ChatContext context, string previousMemory, List<ChatMessageDto> newMessages,
        IProgress<string>? progress = null, CancellationToken ct = default)
    {
        // Local, sin herramientas: redacta a partir de la conversación (no consulta sistemas).
        var (text, usage) = await ChatAsync(
            DeepModel, _prompt.SystemPrompt(), _prompt.UserText(context, previousMemory, newMessages), 2000, ct);
        if (!VerdictParser.TryParse(text, out var verdict))
            _logger.LogWarning("Veredicto no parseable (Ollama): {Text}", ContextBuilder.Truncate(text, 400));
        return (verdict, usage);
    }

    private async Task<(string Text, TokenUsage Usage)> ChatAsync(
        string model, string system, string user, int maxTokens, CancellationToken ct)
    {
        var body = new JsonObject
        {
            ["model"] = model,
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
        var raw = await resp.Content.ReadAsStringAsync(ct);
        if (!resp.IsSuccessStatusCode)
            throw new InvalidOperationException(
                $"Ollama {(int)resp.StatusCode}: {ContextBuilder.Truncate(raw, 300)} (¿está Ollama abierto y el modelo '{model}' descargado?)");

        var json = JsonNode.Parse(raw)!;
        var text = json["message"]?["content"]?.GetValue<string>() ?? "";
        var usage = new TokenUsage(
            json["prompt_eval_count"]?.GetValue<long>() ?? 0,
            json["eval_count"]?.GetValue<long>() ?? 0, 0, 0);
        return (text, usage);
    }
}
