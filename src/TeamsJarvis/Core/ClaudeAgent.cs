using System.Text.Json;
using Anthropic;
using Anthropic.Models.Messages;
using Microsoft.Extensions.Logging;
using TeamsJarvis.Tools;

namespace TeamsJarvis.Core;

/// <summary>Agente basado en la API de Anthropic (Claude), con bucle de tools y visión.</summary>
public sealed class ClaudeAgent : ILlmAgent
{
    private readonly AnthropicClient _client = new();
    private readonly AgentPromptBuilder _prompt;
    private readonly ILogger<ClaudeAgent> _logger;

    public ClaudeAgent(AgentPromptBuilder prompt, ILogger<ClaudeAgent> logger)
    {
        _prompt = prompt;
        _logger = logger;
    }

    public async Task<string> SelfTestAsync(CancellationToken ct = default)
    {
        var response = await _client.Messages.Create(new MessageCreateParams
        {
            Model = _prompt.Options.Model,
            MaxTokens = 32,
            Messages = [new() { Role = Role.User, Content = "Responde solo: OK" }],
        }, cancellationToken: ct);
        return string.Join("", response.Content.Select(b => b.Value).OfType<TextBlock>().Select(t => t.Text));
    }

    public async Task<(bool ShouldReply, string Reason, TokenUsage Usage)> TriageAsync(
        ChatContext context, List<ChatMessageDto> newMessages, CancellationToken ct = default)
    {
        var response = await _client.Messages.Create(new MessageCreateParams
        {
            Model = _prompt.TriageModel(),
            MaxTokens = 100,
            System = _prompt.TriageSystemPrompt(),
            Messages = [new() { Role = Role.User, Content = _prompt.TriageUserText(context, newMessages) }],
        }, cancellationToken: ct);

        var text = string.Join("", response.Content.Select(b => b.Value).OfType<TextBlock>().Select(t => t.Text));
        var usage = new TokenUsage(response.Usage.InputTokens, response.Usage.OutputTokens,
            response.Usage.CacheReadInputTokens ?? 0, response.Usage.CacheCreationInputTokens ?? 0);
        var __t = VerdictParser.ParseTriage(text); return (__t.Reply, __t.Reason, usage);
    }

    public async Task<(AgentVerdict Verdict, TokenUsage Usage)> AnalyzeAsync(
        ChatContext context, string previousMemory, List<ChatMessageDto> newMessages,
        IProgress<string>? progress = null, CancellationToken ct = default)
    {
        var allTools = _prompt.Tools();
        var toolMap = allTools.ToDictionary(t => t.Name);
        var tools = allTools.Select(BuildToolDefinition).Select(t => new ToolUnion(t)).ToList();
        var system = new List<TextBlockParam>
        {
            new() { Text = _prompt.SystemPrompt(), CacheControl = new CacheControlEphemeral() },
        };

        var messages = new List<MessageParam> { new() { Role = Role.User, Content = BuildUserContent(context, previousMemory, newMessages) } };

        Message? response = null;
        long totalIn = 0, totalOut = 0, totalCacheRead = 0, totalCacheWrite = 0;

        for (var iteration = 0; iteration < _prompt.Options.MaxToolIterations; iteration++)
        {
            response = await _client.Messages.Create(new MessageCreateParams
            {
                Model = _prompt.Options.Model,
                MaxTokens = 16000,
                Thinking = new ThinkingConfigAdaptive(),
                OutputConfig = new OutputConfig { Effort = ParseEffort(_prompt.Options.Effort) },
                System = system,
                Tools = tools,
                Messages = messages,
            }, cancellationToken: ct);

            totalIn += response.Usage.InputTokens;
            totalOut += response.Usage.OutputTokens;
            totalCacheRead += response.Usage.CacheReadInputTokens ?? 0;
            totalCacheWrite += response.Usage.CacheCreationInputTokens ?? 0;

            if (response.StopReason != "tool_use")
            {
                progress?.Report("Redactando la respuesta…");
                break;
            }

            var assistantContent = new List<ContentBlockParam>();
            var toolResults = new List<ContentBlockParam>();

            foreach (var block in response.Content)
            {
                if (block.TryPickText(out TextBlock? text))
                    assistantContent.Add(new TextBlockParam { Text = text.Text });
                else if (block.TryPickThinking(out ThinkingBlock? thinking))
                    assistantContent.Add(new ThinkingBlockParam { Thinking = thinking.Thinking, Signature = thinking.Signature });
                else if (block.TryPickRedactedThinking(out RedactedThinkingBlock? redacted))
                    assistantContent.Add(new RedactedThinkingBlockParam { Data = redacted.Data });
                else if (block.TryPickToolUse(out ToolUseBlock? toolUse))
                {
                    progress?.Report($"Consultando {FriendlyTool(toolUse.Name)}…");
                    assistantContent.Add(new ToolUseBlockParam { ID = toolUse.ID, Name = toolUse.Name, Input = toolUse.Input });
                    var (result, isError) = await ExecuteToolAsync(toolMap, toolUse.Name, toolUse.Input, ct);
                    toolResults.Add(new ToolResultBlockParam { ToolUseID = toolUse.ID, Content = result, IsError = isError });
                }
            }

            messages.Add(new MessageParam { Role = Role.Assistant, Content = assistantContent });
            messages.Add(new MessageParam { Role = Role.User, Content = toolResults });
        }

        var usage = new TokenUsage(totalIn, totalOut, totalCacheRead, totalCacheWrite);
        _logger.LogInformation("Claude terminó para '{Chat}': {In}/{Out} tokens (cache {CR}/{CW}).",
            context.ChatName, totalIn, totalOut, totalCacheRead, totalCacheWrite);

        var finalText = string.Join("\n",
            (response?.Content ?? []).Select(b => b.Value).OfType<TextBlock>().Select(t => t.Text));
        if (!VerdictParser.TryParse(finalText, out var verdict))
            _logger.LogWarning("Veredicto no parseable: {Text}", ContextBuilder.Truncate(finalText, 500));
        return (verdict, usage);
    }

    private async Task<(string Result, bool IsError)> ExecuteToolAsync(
        Dictionary<string, IAgentTool> toolMap, string name,
        IReadOnlyDictionary<string, JsonElement> input, CancellationToken ct)
    {
        if (!toolMap.TryGetValue(name, out var tool))
            return ($"Tool desconocida: {name}", true);
        try
        {
            _logger.LogInformation("Claude → tool {Tool}({Input})", name,
                ContextBuilder.Truncate(JsonSerializer.Serialize(input), 300));
            return (await tool.ExecuteAsync(input, ct), false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Tool {Tool} falló.", name);
            return ($"Error ejecutando la tool: {ex.Message}", true);
        }
    }

    private List<ContentBlockParam> BuildUserContent(ChatContext context, string memory, List<ChatMessageDto> newMessages)
    {
        var content = new List<ContentBlockParam> { new TextBlockParam { Text = _prompt.UserText(context, memory, newMessages) } };
        foreach (var image in AgentPromptBuilder.Images(context))
            content.Add(new ImageBlockParam { Source = new Base64ImageSource { Data = image.Base64Data, MediaType = image.MediaType } });
        return content;
    }

    private static Tool BuildToolDefinition(IAgentTool tool) => new()
    {
        Name = tool.Name,
        Description = tool.Description,
        InputSchema = new()
        {
            Properties = tool.SchemaProperties.ToDictionary(kv => kv.Key, kv => JsonSerializer.SerializeToElement(kv.Value)),
            Required = tool.RequiredProperties.ToList(),
        },
    };

    /// <summary>Nombre legible de una tool para el indicador de progreso.</summary>
    private static string FriendlyTool(string name) => name switch
    {
        "fabric_api" => "Microsoft Fabric",
        "run_cli" => "el sistema (Azure/Fabric CLI)",
        "search_sharepoint" => "SharePoint",
        "read_sharepoint_file" => "un documento de SharePoint",
        "read_attachment" => "un adjunto",
        "list_chats" => "tus chats",
        "get_chat_messages" => "otra conversación",
        _ when name.StartsWith("mcp_") => "un servidor MCP",
        _ => name,
    };

    private static Effort ParseEffort(string effort) => effort.ToLowerInvariant() switch
    {
        "low" => Effort.Low,
        "medium" => Effort.Medium,
        "max" => Effort.Max,
        _ => Effort.High,
    };
}
