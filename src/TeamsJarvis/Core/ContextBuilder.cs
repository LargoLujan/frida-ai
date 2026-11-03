using System.Collections.Concurrent;
using System.Text;
using Microsoft.Extensions.Options;

namespace TeamsJarvis.Core;

/// <summary>
/// Construye el contexto que se entrega al agente: transcripción del chat con autores
/// y hora, imágenes recientes, y un resumen de la actividad de los demás chats
/// monitorizados (por si las conversaciones están relacionadas).
/// </summary>
public sealed class ContextBuilder
{
    private readonly TeamsService _teams;
    private readonly SuggestionStore _store;
    private readonly JarvisOptions _options;

    // Caché breve de los últimos mensajes por chat, para no golpear Graph una vez por chat
    // en cada análisis al construir el resumen de "otros chats".
    private static readonly TimeSpan DigestTtl = TimeSpan.FromSeconds(60);
    private readonly ConcurrentDictionary<string, (DateTimeOffset At, List<ChatMessageDto> Messages)> _digestCache = new();

    public ContextBuilder(TeamsService teams, SuggestionStore store, IOptions<JarvisOptions> options)
    {
        _teams = teams;
        _store = store;
        _options = options.Value;
    }

    public async Task<ChatContext> BuildAsync(string chatId, string chatName, CancellationToken ct = default)
    {
        var messages = await _teams.GetRecentMessagesAsync(
            chatId, _options.MaxContextMessages, includeImages: true, ct);

        // Limitar imágenes: solo las N más recientes.
        var totalImages = 0;
        for (var i = messages.Count - 1; i >= 0; i--)
        {
            var keep = Math.Max(0, _options.MaxImages - totalImages);
            if (messages[i].Images.Count > keep)
                messages[i] = messages[i] with { Images = messages[i].Images.TakeLast(keep).ToList() };
            totalImages += messages[i].Images.Count;
        }

        var digest = await BuildOtherChatsDigestAsync(chatId, ct);
        return new ChatContext(chatId, chatName, messages, digest);
    }

    /// <summary>Resumen compacto de los últimos mensajes en el resto de chats monitorizados.</summary>
    private async Task<string> BuildOtherChatsDigestAsync(string currentChatId, CancellationToken ct)
    {
        var others = _store.GetMonitoredChats(enabledOnly: true)
            .Where(c => c.ChatId != currentChatId)
            .Take(8)
            .ToList();
        if (others.Count == 0) return "";

        var sb = new StringBuilder();
        foreach (var chat in others)
        {
            try
            {
                var recent = await GetRecentCachedAsync(chat.ChatId, ct);
                if (recent.Count == 0) continue;
                sb.AppendLine($"### {chat.DisplayName} (chat_id: {chat.ChatId})");
                foreach (var m in recent)
                    sb.AppendLine($"- [{m.CreatedAt.ToLocalTime():dd/MM HH:mm}] {(m.IsMe ? "YO" : m.AuthorName)}: {Truncate(m.Text, 160)}");
            }
            catch
            {
                // Un chat inaccesible no debe impedir la sugerencia del chat actual.
            }
        }
        return sb.ToString();
    }

    private async Task<List<ChatMessageDto>> GetRecentCachedAsync(string chatId, CancellationToken ct)
    {
        if (_digestCache.TryGetValue(chatId, out var cached) && DateTimeOffset.UtcNow - cached.At < DigestTtl)
            return cached.Messages;

        var recent = await _teams.GetRecentMessagesAsync(chatId, 5, includeImages: false, ct);
        _digestCache[chatId] = (DateTimeOffset.UtcNow, recent);
        return recent;
    }

    /// <summary>Transcripción en texto plano del chat (para el mensaje user del agente).</summary>
    public static string RenderTranscript(ChatContext context)
    {
        var sb = new StringBuilder();
        foreach (var m in context.Messages)
        {
            sb.AppendLine($"[{m.CreatedAt.ToLocalTime():dd/MM/yyyy HH:mm}] {(m.IsMe ? "YO" : m.AuthorName)}:");
            sb.AppendLine(m.Text);
            if (m.Images.Count > 0)
                sb.AppendLine($"({m.Images.Count} imagen(es) adjunta(s) — se incluyen a continuación)");
            sb.AppendLine();
        }
        return sb.ToString();
    }

    public static string Truncate(string text, int max) =>
        text.Length <= max ? text : text[..max] + "…";
}
