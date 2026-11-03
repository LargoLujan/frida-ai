using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using TeamsJarvis.Tools;

namespace TeamsJarvis.Core;

/// <summary>
/// Agente basado en la API de OpenAI (Chat Completions) con function calling y visión.
/// Usa HTTP directo para no depender de un SDK. La clave se lee de OPENAI_API_KEY.
/// </summary>
public sealed class OpenAiAgent : ILlmAgent
{
    private const string Endpoint = "https://api.openai.com/v1/chat/completions";
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromMinutes(3) };

    private readonly AgentPromptBuilder _prompt;
    private readonly ILogger<OpenAiAgent> _logger;

    public OpenAiAgent(AgentPromptBuilder prompt, ILogger<OpenAiAgent> logger)
    {
        _prompt = prompt;
        _logger = logger;
    }

    private static string ApiKey =>
        Environment.GetEnvironmentVariable("OPENAI_API_KEY")
        ?? throw new InvalidOperationException("Falta la clave de OpenAI (OPENAI_API_KEY o en Configuración).");

    public async Task<string> SelfTestAsync(CancellationToken ct = default)
    {
        var body = new JsonObject
        {
            ["model"] = _prompt.Options.Model,
            ["max_tokens"] = 16,
            ["messages"] = new JsonArray(new JsonObject { ["role"] = "user", ["content"] = "Responde solo: OK" }),
        };
        var json = await PostAsync(body, ct);
        return json["choices"]?[0]?["message"]?["content"]?.GetValue<string>()?.Trim() ?? "(sin respuesta)";
    }

    public async Task<(bool ShouldReply, string Reason, TokenUsage Usage)> TriageAsync(
        ChatContext context, List<ChatMessageDto> newMessages, CancellationToken ct = default)
    {
        var body = new JsonObject
        {
            ["model"] = _prompt.TriageModel(),
            ["max_tokens"] = 100,
            ["messages"] = new JsonArray(
                new JsonObject { ["role"] = "system", ["content"] = _prompt.TriageSystemPrompt() },
                new JsonObject { ["role"] = "user", ["content"] = _prompt.TriageUserText(context, newMessages) }),
        };
        var json = await PostAsync(body, ct);
        var text = json["choices"]?[0]?["message"]?["content"]?.GetValue<string>() ?? "";
        var u = json["usage"];
        var usage = new TokenUsage(u?["prompt_tokens"]?.GetValue<long>() ?? 0,
            u?["completion_tokens"]?.GetValue<long>() ?? 0,
            u?["prompt_tokens_details"]?["cached_tokens"]?.GetValue<long>() ?? 0, 0);
        var __t = VerdictParser.ParseTriage(text); return (__t.Reply, __t.Reason, usage);
    }

    public async Task<(AgentVerdict Verdict, TokenUsage Usage)> AnalyzeAsync(
        ChatContext context, string previousMemory, List<ChatMessageDto> newMessages,
        IProgress<string>? progress = null, CancellationToken ct = default)
    {
        var allTools = _prompt.Tools();
        var toolMap = allTools.ToDictionary(t => t.Name);
        var toolsJson = new JsonArray();
        foreach (var tool in allTools) toolsJson.Add(ToolSchema(tool));

        var messages = new JsonArray
        {
            new JsonObject { ["role"] = "system", ["content"] = _prompt.SystemPrompt() },
            new JsonObject { ["role"] = "user", ["content"] = BuildUserContent(context, previousMemory, newMessages) },
        };

        long totalIn = 0, totalOut = 0, totalCached = 0;
        string finalText = "";

        for (var iteration = 0; iteration < _prompt.Options.MaxToolIterations; iteration++)
        {
            var body = new JsonObject
            {
                ["model"] = _prompt.Options.Model,
                ["max_tokens"] = 16000,
                ["messages"] = messages.DeepClone(),
                ["tools"] = toolsJson.DeepClone(),
                ["tool_choice"] = "auto",
            };

            var json = await PostAsync(body, ct);

            var usage = json["usage"];
            totalIn += usage?["prompt_tokens"]?.GetValue<long>() ?? 0;
            totalOut += usage?["completion_tokens"]?.GetValue<long>() ?? 0;
            totalCached += usage?["prompt_tokens_details"]?["cached_tokens"]?.GetValue<long>() ?? 0;

            var message = json["choices"]?[0]?["message"];
            var toolCalls = message?["tool_calls"] as JsonArray;

            if (toolCalls is null || toolCalls.Count == 0)
            {
                finalText = message?["content"]?.GetValue<string>() ?? "";
                break;
            }

            // Reañadir el turno assistant con sus tool_calls y luego un mensaje 'tool' por cada resultado.
            messages.Add(CloneNode(message!));
            foreach (var call in toolCalls)
            {
                var id = call?["id"]?.GetValue<string>() ?? "";
                var name = call?["function"]?["name"]?.GetValue<string>() ?? "";
                var args = call?["function"]?["arguments"]?.GetValue<string>() ?? "{}";
                var result = await ExecuteToolAsync(toolMap, name, args, ct);
                messages.Add(new JsonObject
                {
                    ["role"] = "tool",
                    ["tool_call_id"] = id,
                    ["content"] = result,
                });
            }
        }

        var tokenUsage = new TokenUsage(totalIn, totalOut, totalCached, 0);
        _logger.LogInformation("OpenAI terminó para '{Chat}': {In}/{Out} tokens.", context.ChatName, totalIn, totalOut);

        if (!VerdictParser.TryParse(finalText, out var verdict))
            _logger.LogWarning("Veredicto no parseable (OpenAI): {Text}", ContextBuilder.Truncate(finalText, 500));
        return (verdict, tokenUsage);
    }

    private async Task<string> ExecuteToolAsync(Dictionary<string, IAgentTool> toolMap, string name, string argsJson, CancellationToken ct)
    {
        if (!toolMap.TryGetValue(name, out var tool)) return $"Tool desconocida: {name}";
        try
        {
            _logger.LogInformation("OpenAI → tool {Tool}({Args})", name, ContextBuilder.Truncate(argsJson, 300));
            return await tool.ExecuteAsync(ParseArgs(argsJson), ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Tool {Tool} falló.", name);
            return $"Error ejecutando la tool: {ex.Message}";
        }
    }

    private JsonArray BuildUserContent(ChatContext context, string memory, List<ChatMessageDto> newMessages)
    {
        var parts = new JsonArray
        {
            new JsonObject { ["type"] = "text", ["text"] = _prompt.UserText(context, memory, newMessages) },
        };
        foreach (var image in AgentPromptBuilder.Images(context))
        {
            parts.Add(new JsonObject
            {
                ["type"] = "image_url",
                ["image_url"] = new JsonObject { ["url"] = $"data:{image.MediaType};base64,{image.Base64Data}" },
            });
        }
        return parts;
    }

    private static JsonObject ToolSchema(IAgentTool tool) => new()
    {
        ["type"] = "function",
        ["function"] = new JsonObject
        {
            ["name"] = tool.Name,
            ["description"] = tool.Description,
            ["parameters"] = ParametersSchema(tool),
        },
    };

    internal static JsonObject ParametersSchema(IAgentTool tool)
    {
        var properties = new JsonObject();
        foreach (var (key, schema) in tool.SchemaProperties)
            properties[key] = JsonNode.Parse(JsonSerializer.Serialize(schema));
        return new JsonObject
        {
            ["type"] = "object",
            ["properties"] = properties,
            ["required"] = new JsonArray(tool.RequiredProperties.Select(r => (JsonNode)r).ToArray()),
        };
    }

    internal static Dictionary<string, JsonElement> ParseArgs(string json)
    {
        var result = new Dictionary<string, JsonElement>();
        try
        {
            using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(json) ? "{}" : json);
            if (doc.RootElement.ValueKind == JsonValueKind.Object)
                foreach (var prop in doc.RootElement.EnumerateObject())
                    result[prop.Name] = prop.Value.Clone();
        }
        catch { }
        return result;
    }

    private static JsonNode CloneNode(JsonNode node) => JsonNode.Parse(node.ToJsonString())!;

    private static async Task<JsonNode> PostAsync(JsonObject body, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, Endpoint)
        {
            Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json"),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", ApiKey);
        using var response = await Http.SendAsync(request, ct);
        var text = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"OpenAI {(int)response.StatusCode}: {ContextBuilder.Truncate(text, 500)}");
        return JsonNode.Parse(text) ?? throw new InvalidOperationException("Respuesta vacía de OpenAI.");
    }
}
