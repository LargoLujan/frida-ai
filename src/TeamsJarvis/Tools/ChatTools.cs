using System.Text;
using System.Text.Json;
using TeamsJarvis.Core;

namespace TeamsJarvis.Tools;

/// <summary>list_chats: lista todos los chats de Teams a los que el usuario tiene acceso.</summary>
public sealed class ListChatsTool(TeamsService teams, SuggestionStore store) : IAgentTool
{
    public string Name => "list_chats";
    public bool RequiresGraph => true;

    public string Description =>
        "Lista todas las conversaciones de Teams a las que el usuario tiene acceso (individuales y grupales), " +
        "con su chat_id y nombre. Úsala para descubrir conversaciones relacionadas con el tema actual " +
        "y luego leerlas con get_chat_messages.";

    public IReadOnlyDictionary<string, object> SchemaProperties => new Dictionary<string, object>();
    public IReadOnlyList<string> RequiredProperties => [];

    public async Task<string> ExecuteAsync(IReadOnlyDictionary<string, JsonElement> input, CancellationToken ct)
    {
        var chats = await teams.ListChatsAsync(ct);
        var monitored = store.GetMonitoredChats(enabledOnly: true).Select(c => c.ChatId).ToHashSet();
        var sb = new StringBuilder();
        sb.AppendLine($"El usuario tiene acceso a {chats.Count} chats:");
        foreach (var chat in chats)
            sb.AppendLine($"- {chat.DisplayName}{(monitored.Contains(chat.ChatId) ? " [monitorizado]" : "")} | chat_id: {chat.ChatId}");
        return sb.ToString();
    }
}

/// <summary>get_chat_messages: lee los últimos mensajes de cualquier chat accesible.</summary>
public sealed class GetChatMessagesTool(TeamsService teams) : IAgentTool
{
    public string Name => "get_chat_messages";
    public bool RequiresGraph => true;

    public string Description =>
        "Lee los últimos mensajes de una conversación de Teams (por chat_id). " +
        "Úsala para ampliar el historial del chat actual o para consultar otra conversación que pueda estar " +
        "relacionada con el tema (obtén los chat_id con list_chats o del resumen de otros chats).";

    public IReadOnlyDictionary<string, object> SchemaProperties => new Dictionary<string, object>
    {
        ["chat_id"] = new { type = "string", description = "Identificador del chat" },
        ["count"] = new { type = "integer", description = "Número de mensajes a leer (máx. 50, por defecto 20)" },
    };

    public IReadOnlyList<string> RequiredProperties => ["chat_id"];

    public async Task<string> ExecuteAsync(IReadOnlyDictionary<string, JsonElement> input, CancellationToken ct)
    {
        var chatId = input.GetString("chat_id");
        if (string.IsNullOrWhiteSpace(chatId)) return "Error: falta 'chat_id'.";
        var count = Math.Clamp(input.GetInt("count", 20), 1, 50);

        var messages = await teams.GetRecentMessagesAsync(chatId, count, includeImages: false, ct);
        if (messages.Count == 0) return "El chat no tiene mensajes recientes o no es accesible.";

        var sb = new StringBuilder();
        foreach (var m in messages)
            sb.AppendLine($"[{m.CreatedAt.ToLocalTime():dd/MM/yyyy HH:mm}] {(m.IsMe ? "YO" : m.AuthorName)}: {m.Text}");
        return sb.ToString();
    }
}
