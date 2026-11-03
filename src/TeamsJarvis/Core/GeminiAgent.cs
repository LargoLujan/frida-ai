using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using TeamsJarvis.Tools;

namespace TeamsJarvis.Core;

/// <summary>
/// Agente basado en la API de Google Gemini (generateContent) con function calling y visión.
/// Usa HTTP directo. La clave se lee de GEMINI_API_KEY.
/// </summary>
public sealed class GeminiAgent : ILlmAgent
{
    private const string BaseUrl = "https://generativelanguage.googleapis.com/v1beta/models";
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromMinutes(3) };

    private readonly AgentPromptBuilder _prompt;
    private readonly ILogger<GeminiAgent> _logger;

    public GeminiAgent(AgentPromptBuilder prompt, ILogger<GeminiAgent> logger)
    {
        _prompt = prompt;
        _logger = logger;
    }

    private static string ApiKey =>
        Environment.GetEnvironmentVariable("GEMINI_API_KEY")
        ?? throw new InvalidOperationException("Falta la clave de Gemini (GEMINI_API_KEY o en Configuración).");

    public async Task<string> SelfTestAsync(CancellationToken ct = default)
    {
        var body = new JsonObject
        {
            ["contents"] = new JsonArray(new JsonObject
            {
                ["role"] = "user",
                ["parts"] = new JsonArray(new JsonObject { ["text"] = "Responde solo: OK" }),
            }),
        };
        var json = await PostAsync(_prompt.Options.Model, body, ct);
        return FirstText(json)?.Trim() ?? "(sin respuesta)";
    }

    public async Task<(bool ShouldReply, string Reason, TokenUsage Usage)> TriageAsync(
        ChatContext context, List<ChatMessageDto> newMessages, CancellationToken ct = default)
    {
        var body = new JsonObject
        {
            ["systemInstruction"] = new JsonObject { ["parts"] = new JsonArray(new JsonObject { ["text"] = _prompt.TriageSystemPrompt() }) },
            ["contents"] = new JsonArray(new JsonObject
            {
                ["role"] = "user",
                ["parts"] = new JsonArray(new JsonObject { ["text"] = _prompt.TriageUserText(context, newMessages) }),
            }),
            ["generationConfig"] = new JsonObject { ["maxOutputTokens"] = 100 },
        };
        var json = await PostAsync(_prompt.TriageModel(), body, ct);
        var text = string.Concat((json["candidates"]?[0]?["content"]?["parts"] as JsonArray ?? new JsonArray())
            .Select(p => p?["text"]?.GetValue<string>() ?? ""));
        var meta = json["usageMetadata"];
        var usage = new TokenUsage(meta?["promptTokenCount"]?.GetValue<long>() ?? 0,
            meta?["candidatesTokenCount"]?.GetValue<long>() ?? 0,
            meta?["cachedContentTokenCount"]?.GetValue<long>() ?? 0, 0);
        var __t = VerdictParser.ParseTriage(text); return (__t.Reply, __t.Reason, usage);
    }

    public async Task<(AgentVerdict Verdict, TokenUsage Usage)> AnalyzeAsync(
        ChatContext context, string previousMemory, List<ChatMessageDto> newMessages,
        IProgress<string>? progress = null, CancellationToken ct = default)
    {
        var allTools = _prompt.Tools();
        var toolMap = allTools.ToDictionary(t => t.Name);
        var declarations = new JsonArray();
        foreach (var tool in allTools) declarations.Add(FunctionDeclaration(tool));
        var toolsJson = new JsonArray(new JsonObject { ["functionDeclarations"] = declarations });

        var contents = new JsonArray
        {
            new JsonObject { ["role"] = "user", ["parts"] = BuildUserParts(context, previousMemory, newMessages) },
        };

        long totalIn = 0, totalOut = 0, totalCached = 0;
        string finalText = "";

        for (var iteration = 0; iteration < _prompt.Options.MaxToolIterations; iteration++)
        {
            var body = new JsonObject
            {
                ["systemInstruction"] = new JsonObject { ["parts"] = new JsonArray(new JsonObject { ["text"] = _prompt.SystemPrompt() }) },
                ["contents"] = contents.DeepClone(),
                ["tools"] = toolsJson.DeepClone(),
                ["generationConfig"] = new JsonObject { ["maxOutputTokens"] = 16000 },
            };

            var json = await PostAsync(_prompt.Options.Model, body, ct);

            var meta = json["usageMetadata"];
            totalIn += meta?["promptTokenCount"]?.GetValue<long>() ?? 0;
            totalOut += meta?["candidatesTokenCount"]?.GetValue<long>() ?? 0;
            totalCached += meta?["cachedContentTokenCount"]?.GetValue<long>() ?? 0;

            var parts = json["candidates"]?[0]?["content"]?["parts"] as JsonArray;
            var functionCalls = (parts ?? new JsonArray())
                .Where(p => p?["functionCall"] is not null).ToList();

            if (functionCalls.Count == 0)
            {
                finalText = string.Concat((parts ?? new JsonArray())
                    .Select(p => p?["text"]?.GetValue<string>() ?? ""));
                break;
            }

            // Añadir el turno 'model' con las functionCall y un turno 'user' con las functionResponse.
            contents.Add(new JsonObject { ["role"] = "model", ["parts"] = ClonePartsArray(parts!) });
            var responseParts = new JsonArray();
            foreach (var p in functionCalls)
            {
                var fc = p!["functionCall"]!;
                var name = fc["name"]?.GetValue<string>() ?? "";
                var argsNode = fc["args"];
                var result = await ExecuteToolAsync(toolMap, name, argsNode, ct);
                responseParts.Add(new JsonObject
                {
                    ["functionResponse"] = new JsonObject
                    {
                        ["name"] = name,
                        ["response"] = new JsonObject { ["result"] = result },
                    },
                });
            }
            contents.Add(new JsonObject { ["role"] = "user", ["parts"] = responseParts });
        }

        var tokenUsage = new TokenUsage(totalIn, totalOut, totalCached, 0);
        _logger.LogInformation("Gemini terminó para '{Chat}': {In}/{Out} tokens.", context.ChatName, totalIn, totalOut);

        if (!VerdictParser.TryParse(finalText, out var verdict))
            _logger.LogWarning("Veredicto no parseable (Gemini): {Text}", ContextBuilder.Truncate(finalText, 500));
        return (verdict, tokenUsage);
    }

    private async Task<string> ExecuteToolAsync(Dictionary<string, IAgentTool> toolMap, string name, JsonNode? args, CancellationToken ct)
    {
        if (!toolMap.TryGetValue(name, out var tool)) return $"Tool desconocida: {name}";
        try
        {
            var argsJson = args?.ToJsonString() ?? "{}";
            _logger.LogInformation("Gemini → tool {Tool}({Args})", name, ContextBuilder.Truncate(argsJson, 300));
            return await tool.ExecuteAsync(OpenAiAgent.ParseArgs(argsJson), ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Tool {Tool} falló.", name);
            return $"Error ejecutando la tool: {ex.Message}";
        }
    }

    private JsonArray BuildUserParts(ChatContext context, string memory, List<ChatMessageDto> newMessages)
    {
        var parts = new JsonArray { new JsonObject { ["text"] = _prompt.UserText(context, memory, newMessages) } };
        foreach (var image in AgentPromptBuilder.Images(context))
        {
            parts.Add(new JsonObject
            {
                ["inlineData"] = new JsonObject { ["mimeType"] = image.MediaType, ["data"] = image.Base64Data },
            });
        }
        return parts;
    }

    private static JsonObject FunctionDeclaration(IAgentTool tool) => new()
    {
        ["name"] = tool.Name,
        ["description"] = tool.Description,
        ["parameters"] = OpenAiAgent.ParametersSchema(tool),
    };

    private static string? FirstText(JsonNode json) =>
        (json["candidates"]?[0]?["content"]?["parts"] as JsonArray)?
            .Select(p => p?["text"]?.GetValue<string>()).FirstOrDefault(t => t is not null);

    private static JsonArray ClonePartsArray(JsonArray parts) =>
        (JsonArray)JsonNode.Parse(parts.ToJsonString())!;

    private static async Task<JsonNode> PostAsync(string model, JsonObject body, CancellationToken ct)
    {
        var url = $"{BaseUrl}/{model}:generateContent?key={ApiKey}";
        using var request = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json"),
        };
        using var response = await Http.SendAsync(request, ct);
        var text = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"Gemini {(int)response.StatusCode}: {ContextBuilder.Truncate(text, 500)}");
        return JsonNode.Parse(text) ?? throw new InvalidOperationException("Respuesta vacía de Gemini.");
    }
}
