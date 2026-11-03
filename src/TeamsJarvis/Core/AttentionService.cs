namespace TeamsJarvis.Core;

/// <summary>
/// Construye el panel "Requiere tu atención hoy" a partir de datos YA almacenados
/// (sugerencias y recordatorios). Es 100% local y SIN coste de IA: solo agrega y clasifica.
/// </summary>
public sealed class AttentionService
{
    private const string MentionMarker = "TE MENCIONAN";

    private readonly SuggestionStore _suggestions;
    private readonly NotesStore _notes;

    public AttentionService(SuggestionStore suggestions, NotesStore notes)
    {
        _suggestions = suggestions;
        _notes = notes;
    }

    /// <summary>Agrega lo pendiente del día (menciones, preguntas y recordatorios de hoy).</summary>
    public AttentionReport BuildToday()
    {
        var startOfDay = new DateTimeOffset(DateTime.Today, DateTimeOffset.Now.Offset);

        var mentions = new List<AttentionItem>();
        var questions = new List<AttentionItem>();

        foreach (var s in _suggestions.GetSuggestionsSince(startOfDay))
        {
            var isMention = s.TriggerSummary.Contains(MentionMarker, StringComparison.OrdinalIgnoreCase);
            var isQuestion = LooksLikeQuestion(s.TriggerSummary);
            var detail = CleanTrigger(s.TriggerSummary);

            if (isMention)
                mentions.Add(new AttentionItem("mention", s.ChatName, s.ReplyTo, detail, s.CreatedAt, s.Id, s.Confidence));
            else if (isQuestion)
                questions.Add(new AttentionItem("question", s.ChatName, s.ReplyTo, detail, s.CreatedAt, s.Id, s.Confidence));
        }

        var endOfDay = startOfDay.AddDays(1);
        var reminders = _notes.GetReminders(includeDone: false)
            .Where(r => r.DueAt < endOfDay) // vencidos o de hoy
            .OrderBy(r => r.DueAt)
            .Select(r => new AttentionItem("reminder", r.Project, r.Text,
                r.DueAt < DateTimeOffset.Now ? "Vencido" : "Hoy", r.DueAt, r.Id, 1.0))
            .ToList();

        // Agrupadas por chat (mismo interlocutor/grupo junto) y, dentro, por hora.
        return new AttentionReport(
            mentions.OrderBy(m => m.ChatName).ThenBy(m => m.When).ToList(),
            questions.OrderBy(q => q.ChatName).ThenBy(q => q.When).ToList(),
            reminders);
    }

    internal static bool LooksLikeQuestion(string trigger) =>
        trigger.Contains('?') || trigger.Contains('¿');

    /// <summary>Quita el marcador de mención del texto para mostrarlo limpio.</summary>
    internal static string CleanTrigger(string trigger)
    {
        var idx = trigger.IndexOf('—');
        return idx >= 0 && trigger.Contains(MentionMarker, StringComparison.OrdinalIgnoreCase)
            ? trigger[(idx + 1)..].Trim()
            : trigger.Trim();
    }
}
