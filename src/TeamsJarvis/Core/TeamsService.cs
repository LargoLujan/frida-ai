using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Graph.Models;

namespace TeamsJarvis.Core;

/// <summary>Operaciones de Microsoft Teams vía Graph: chats, mensajes e imágenes.</summary>
public sealed partial class TeamsService
{
    private readonly GraphAuthService _auth;
    private readonly JarvisOptions _options;
    private readonly ILogger<TeamsService> _logger;
    private static readonly HttpClient Http = new();

    public TeamsService(GraphAuthService auth, IOptions<JarvisOptions> options, ILogger<TeamsService> logger)
    {
        _auth = auth;
        _options = options.Value;
        _logger = logger;
    }

    /// <summary>Lista los chats del usuario con un nombre legible (topic o miembros).</summary>
    public async Task<List<MonitoredChat>> ListChatsAsync(CancellationToken ct = default)
    {
        var graph = await _auth.GetGraphClientAsync(ct);
        var result = new List<MonitoredChat>();

        Microsoft.Graph.Models.ChatCollectionResponse? page;
        try
        {
            page = await graph.Me.Chats.GetAsync(rc =>
            {
                rc.QueryParameters.Top = 50;
                rc.QueryParameters.Expand = ["members"];
            }, ct);
        }
        catch (Microsoft.Graph.Models.ODataErrors.ODataError ex) when (ex.ResponseStatusCode == 403)
        {
            _logger.LogWarning(ex, "Graph /me/chats devolvió 403 (sin permiso Chat.Read).");
            throw new InvalidOperationException(
                _auth.IsAzCliMode
                    ? "Tu sesión de Azure CLI no tiene permiso para leer los chats de Teams (Chat.Read). " +
                      "Ve a Configuración → 'Registro de aplicación propio' y usa un registro con permiso Chat.Read."
                    : "El registro de aplicación no tiene concedido el permiso Chat.Read. Revisa los permisos " +
                      "de la app en Entra ID (Chat.Read delegado) y vuelve a iniciar sesión.");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error listando chats de Teams.");
            throw;
        }

        while (page is not null)
        {
            foreach (var chat in page.Value ?? [])
            {
                if (chat.Id is null) continue;
                var name = chat.Topic;
                if (string.IsNullOrWhiteSpace(name))
                {
                    var others = (chat.Members ?? [])
                        .OfType<AadUserConversationMember>()
                        .Where(m => m.UserId != _auth.MyUserId)
                        .Select(m => m.DisplayName)
                        .Where(n => !string.IsNullOrWhiteSpace(n))
                        .ToList();
                    name = others.Count > 0 ? string.Join(", ", others) : "(chat sin nombre)";
                }
                result.Add(new MonitoredChat(chat.Id, name!, false));
            }

            if (page.OdataNextLink is null) break;
            page = await graph.Me.Chats.WithUrl(page.OdataNextLink).GetAsync(cancellationToken: ct);
        }

        return result;
    }

    /// <summary>Últimos mensajes de un chat, normalizados y en orden cronológico ascendente.</summary>
    public async Task<List<ChatMessageDto>> GetRecentMessagesAsync(
        string chatId, int count, bool includeImages, CancellationToken ct = default)
    {
        var graph = await _auth.GetGraphClientAsync(ct);
        var page = await graph.Chats[chatId].Messages.GetAsync(rc =>
        {
            rc.QueryParameters.Top = Math.Min(count, 50);
        }, ct);

        var messages = new List<ChatMessageDto>();
        foreach (var msg in page?.Value ?? [])
        {
            if (msg.MessageType != ChatMessageType.Message) continue;
            if (msg.Id is null || msg.CreatedDateTime is null) continue;

            var authorId = msg.From?.User?.Id ?? "";
            var authorName = msg.From?.User?.DisplayName
                             ?? msg.From?.Application?.DisplayName
                             ?? "(desconocido)";
            var text = HtmlToPlainText(msg.Body?.Content ?? "");

            var images = new List<ChatImageDto>();
            if (includeImages && (msg.Body?.Content?.Contains("hostedContents", StringComparison.OrdinalIgnoreCase) ?? false))
            {
                images = await DownloadImagesAsync(chatId, msg.Id, ct);
                if (images.Count > 0 && string.IsNullOrWhiteSpace(text))
                    text = "(imagen adjunta)";
            }

            if (string.IsNullOrWhiteSpace(text) && images.Count == 0) continue;

            messages.Add(new ChatMessageDto(
                msg.Id, authorName, authorId, authorId == _auth.MyUserId,
                msg.CreatedDateTime.Value, text, images));
        }

        messages.Reverse(); // Graph devuelve descendente; queremos cronológico
        return messages;
    }

    /// <summary>Descarga las imágenes (hostedContents) de un mensaje.</summary>
    private async Task<List<ChatImageDto>> DownloadImagesAsync(string chatId, string messageId, CancellationToken ct)
    {
        var images = new List<ChatImageDto>();
        try
        {
            var graph = await _auth.GetGraphClientAsync(ct);
            var hosted = await graph.Chats[chatId].Messages[messageId].HostedContents.GetAsync(cancellationToken: ct);

            foreach (var hc in hosted?.Value ?? [])
            {
                if (hc.Id is null) continue;
                var contentType = hc.ContentType ?? "image/png";
                if (!contentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase)) continue;

                // El SDK no expone el $value de hostedContents directamente: HTTP manual.
                var url = $"https://graph.microsoft.com/v1.0/chats/{Uri.EscapeDataString(chatId)}/messages/{Uri.EscapeDataString(messageId)}/hostedContents/{Uri.EscapeDataString(hc.Id)}/$value";
                using var request = new HttpRequestMessage(HttpMethod.Get, url);
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", await _auth.GetGraphTokenAsync(ct));
                using var response = await Http.SendAsync(request, ct);
                if (!response.IsSuccessStatusCode) continue;

                var bytes = await response.Content.ReadAsByteArrayAsync(ct);
                var (finalBytes, finalType) = ImageHelper.EnsureWithinLimits(bytes, contentType);
                images.Add(new ChatImageDto(finalType, Convert.ToBase64String(finalBytes)));
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "No se pudieron descargar imágenes del mensaje {MessageId}.", messageId);
        }
        return images;
    }

    /// <summary>Convierte el HTML de un mensaje de Teams a texto plano legible.</summary>
    public static string HtmlToPlainText(string html)
    {
        if (string.IsNullOrWhiteSpace(html)) return "";
        var text = html;
        text = BrRegex().Replace(text, "\n");
        text = ImgRegex().Replace(text, "[imagen]");
        text = AttachmentRegex().Replace(text, "[adjunto]");
        text = TagRegex().Replace(text, "");
        text = System.Net.WebUtility.HtmlDecode(text);
        text = MultiNewlineRegex().Replace(text, "\n");
        return text.Trim();
    }

    [GeneratedRegex(@"<br\s*/?>|</p>|</div>", RegexOptions.IgnoreCase)]
    private static partial Regex BrRegex();

    [GeneratedRegex(@"<img[^>]*>", RegexOptions.IgnoreCase)]
    private static partial Regex ImgRegex();

    [GeneratedRegex(@"<attachment[^>]*>.*?</attachment>|<attachment[^>]*/>", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex AttachmentRegex();

    [GeneratedRegex("<[^>]+>")]
    private static partial Regex TagRegex();

    [GeneratedRegex(@"\n{3,}")]
    private static partial Regex MultiNewlineRegex();
}
