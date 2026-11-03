using Microsoft.Toolkit.Uwp.Notifications;
using TeamsJarvis.Core;

namespace TeamsJarvis.UI;

/// <summary>
/// Aviso toast de Windows cuando llega una sugerencia y la ventana no está visible.
/// El botón Copiar deja la respuesta en el portapapeles sin abrir la app.
/// </summary>
public static class ToastService
{
    private static SuggestionStore? _store;
    private static NotesStore? _notes;
    private static Action? _openMainWindow;

    public static void Initialize(SuggestionStore store, NotesStore notes, Action openMainWindow)
    {
        _store = store;
        _notes = notes;
        _openMainWindow = openMainWindow;

        ToastNotificationManagerCompat.OnActivated += toastArgs =>
        {
            var args = ToastArguments.Parse(toastArgs.Argument);
            var action = args.TryGetValue("action", out var a) ? a : "open";
            args.TryGetValue("id", out var idText);
            long.TryParse(idText, out var id);

            System.Windows.Application.Current?.Dispatcher.Invoke(() =>
            {
                switch (action)
                {
                    case "copy" when _store?.GetSuggestionById(id) is { } suggestion:
                        System.Windows.Clipboard.SetText(suggestion.SuggestedReply);
                        break;
                    case "reminder_done" when id > 0:
                        _notes?.SetReminderDone(id, true);
                        break;
                    default:
                        _openMainWindow?.Invoke();
                        break;
                }
            });
        };
    }

    /// <summary>Aviso de un recordatorio vencido, con botón "Hecho".</summary>
    public static void ShowReminder(Reminder reminder)
    {
        BarkPlayer.Bark();
        try
        {
            var builder = new ToastContentBuilder()
                .AddArgument("action", "open")
                .AddText("⏰ Recordatorio")
                .AddText(reminder.Text);
            if (!string.IsNullOrWhiteSpace(reminder.Project))
                builder.AddText($"Proyecto: {reminder.Project}");
            builder
                .AddButton(new ToastButton()
                    .SetContent("Hecho")
                    .AddArgument("action", "reminder_done")
                    .AddArgument("id", reminder.Id.ToString()))
                .AddButton(new ToastButton().SetContent("Abrir").AddArgument("action", "open"))
                .Show();
        }
        catch
        {
            // Un fallo de notificación nunca debe tumbar la app.
        }
    }

    /// <summary>
    /// Notificación enriquecida de una sugerencia: destino (chat/persona), texto completo de la
    /// respuesta sugerida e imagen del mensaje si la hay. Botones Copiar (al portapapeles) y Abrir.
    /// La app NUNCA envía: solo copia; tú pegas en Teams.
    /// </summary>
    public static void ShowSuggestion(Suggestion suggestion, string? imagePath = null)
    {
        BarkPlayer.Bark(); // ladrido suave al notificar (si está activado)
        try
        {
            var id = suggestion.Id.ToString();
            var destino = string.Equals(suggestion.ChatName, suggestion.ReplyTo, StringComparison.OrdinalIgnoreCase)
                ? $"Para {suggestion.ReplyTo}"
                : $"{suggestion.ChatName} · responder a {suggestion.ReplyTo}";

            var builder = new ToastContentBuilder()
                .AddArgument("action", "open")
                .AddArgument("id", id)
                .AddText("💡 Respuesta sugerida")
                .AddText($"➡ {destino}")
                .AddText(ContextBuilder.Truncate(suggestion.SuggestedReply, 380));

            if (!string.IsNullOrWhiteSpace(imagePath) && File.Exists(imagePath))
                builder.AddInlineImage(new Uri(imagePath));

            builder
                .AddButton(new ToastButton()
                    .SetContent("Copiar respuesta")
                    .AddArgument("action", "copy")
                    .AddArgument("id", id))
                .AddButton(new ToastButton()
                    .SetContent("Abrir")
                    .AddArgument("action", "open"))
                .Show();
        }
        catch
        {
            // Las notificaciones nunca deben tumbar el monitor.
        }
    }

    /// <summary>
    /// Notificación enriquecida de un mensaje entrante (sin sugerencia): previsualiza el texto
    /// completo y la imagen si la trae. Acción "Generar sugerencia" (bajo demanda) y "Abrir".
    /// </summary>
    public static void ShowIncoming(string chatName, string author, string text, string? imagePath, bool mention)
    {
        try
        {
            var builder = new ToastContentBuilder()
                .AddArgument("action", "open")
                .AddText($"{(mention ? "🔔 Te mencionan · " : "💬 ")}{chatName}")
                .AddText($"{author}:")
                .AddText(ContextBuilder.Truncate(text, 380));

            if (!string.IsNullOrWhiteSpace(imagePath) && File.Exists(imagePath))
                builder.AddInlineImage(new Uri(imagePath));

            builder.AddButton(new ToastButton().SetContent("Abrir").AddArgument("action", "open")).Show();
        }
        catch
        {
            // Nunca tumbar la app por una notificación.
        }
    }

    public static void Cleanup()
    {
        try { ToastNotificationManagerCompat.Uninstall(); } catch { }
    }
}
