using System.Text.Json;

namespace TeamsJarvis.Core;

/// <summary>Payload de conversación leído del DOM de Teams (por la extensión o el WebView2).</summary>
public record BrowserPayload(string ChatId, string ChatName, bool Force, string Version, List<ChatMessageDto> Messages);

public static class BrowserPayloadParser
{
    /// <summary>Parseo tolerante (no lanza si un campo viene con otro tipo). Devuelve null si no es válido.</summary>
    public static BrowserPayload? Parse(string json)
    {
        JsonDocument doc;
        try { doc = JsonDocument.Parse(json); } catch { return null; }
        using (doc)
        {
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return null;

            var chatId = Str(root, "chatId");
            if (string.IsNullOrWhiteSpace(chatId)) return null;
            var chatName = Str(root, "chatName", "(chat)");
            var force = root.TryGetProperty("force", out var f) && f.ValueKind == JsonValueKind.True;
            var version = Str(root, "v", "?");

            var messages = new List<ChatMessageDto>();
            if (root.TryGetProperty("messages", out var arr) && arr.ValueKind == JsonValueKind.Array)
            {
                foreach (var m in arr.EnumerateArray())
                {
                    var text = Str(m, "text");
                    if (string.IsNullOrWhiteSpace(text)) continue;
                    var author = Str(m, "author", "(desconocido)");
                    var isMe = m.TryGetProperty("isMe", out var im) && im.ValueKind == JsonValueKind.True;
                    var mention = m.TryGetProperty("mention", out var mn) && mn.ValueKind == JsonValueKind.True;
                    var id = Str(m, "id", $"{chatId}:{messages.Count}:{text.GetHashCode()}");
                    var when = ParseTime(Str(m, "time"));
                    messages.Add(new ChatMessageDto(id, author, author, isMe, when, text, new List<ChatImageDto>(), mention));
                }
            }
            messages = messages.OrderBy(m => m.CreatedAt).ToList();
            return new BrowserPayload(chatId, chatName, force, version, messages);
        }
    }

    private static string Str(JsonElement el, string name, string fallback = "") =>
        el.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? fallback : fallback;

    private static DateTimeOffset ParseTime(string? raw) =>
        !string.IsNullOrWhiteSpace(raw) && DateTimeOffset.TryParse(raw, out var dt) ? dt : DateTimeOffset.Now;
}
