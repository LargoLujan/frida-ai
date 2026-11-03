using System.Windows;
using System.Windows.Controls;
using TeamsJarvis.Core;

namespace TeamsJarvis.UI;

public partial class AttentionWindow : Window
{
    private readonly SuggestionStore _suggestions;
    private readonly NotesStore _notes;
    private readonly AttentionService _attention;
    private readonly LocalLlm _local;

    public AttentionWindow(SuggestionStore suggestions, NotesStore notes, AttentionService attention, LocalLlm local)
    {
        InitializeComponent();
        _suggestions = suggestions;
        _notes = notes;
        _attention = attention;
        _local = local;
        Icon = FridaIcon.CreateImageSource();
        Load();
    }

    private void Load()
    {
        var report = _attention.BuildToday();
        HeaderText.Text = report.Total == 0
            ? "Todo al día ✅"
            : $"Requiere tu atención hoy · {report.Total}";

        Bind(MentionsList, MentionHeader, MentionEmpty, report.Mentions, "🔔 Te mencionan");
        Bind(QuestionsList, QuestionHeader, QuestionEmpty, report.Questions, "❓ Preguntas pendientes");
        Bind(RemindersList, ReminderHeader, ReminderEmpty, report.Reminders, "⏰ Recordatorios de hoy");
    }

    private static void Bind(ItemsControl list, TextBlock header, TextBlock empty,
        List<AttentionItem> items, string title)
    {
        header.Text = $"{title} · {items.Count}";
        list.ItemsSource = items.Select(i => new ItemVm(i)).ToList();
        var has = items.Count > 0;
        list.Visibility = has ? Visibility.Visible : Visibility.Collapsed;
        empty.Visibility = has ? Visibility.Collapsed : Visibility.Visible;
    }

    private void OnRefresh(object sender, RoutedEventArgs e) => Load();

    private void OnCopy(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: long id } && _suggestions.GetSuggestionById(id) is { } s
            && !string.IsNullOrWhiteSpace(s.SuggestedReply))
            System.Windows.Clipboard.SetText(s.SuggestedReply);
    }

    private void OnMakeReminder(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: long id }) return;
        var s = _suggestions.GetSuggestionById(id);
        if (s is null) return;
        // Captura de tarea sin coste: recordatorio para dentro de 1 hora con el asunto.
        var text = $"Responder a {s.ReplyTo} ({s.ChatName})";
        _notes.UpsertReminder(0, text, DateTimeOffset.Now.AddHours(1), s.ChatName);
        MessageBox.Show(this, "Recordatorio creado para dentro de 1 hora.\nAjústalo en 📝 Notas → Recordatorios.",
            "Frida.ai", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void OnDone(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: long id })
        {
            _notes.SetReminderDone(id, true);
            Load();
        }
    }

    private async void OnSummarize(object sender, RoutedEventArgs e)
    {
        SummaryBox.Visibility = Visibility.Visible;
        SummaryText.Text = "Generando resumen local (gratis)…";
        var report = _attention.BuildToday();
        var summary = await _local.SummarizeDayAsync(report);
        SummaryText.Text = summary
            ?? "No hay modelo local disponible para el resumen. Instala Ollama y ejecuta "
               + "'ollama pull llama3.2:3b' para tener resúmenes gratis.";
    }

    private sealed class ItemVm(AttentionItem i)
    {
        public string ChatName => string.IsNullOrWhiteSpace(i.ChatName) ? "—" : i.ChatName;
        public string Detail => i.Detail;
        public long RefId => i.RefId;
        public string WhenText => i.Kind == "reminder"
            ? i.When.ToLocalTime().ToString("HH:mm")
            : i.When.ToLocalTime().ToString("HH:mm");
        public Visibility CopyVisibility => i.Kind is "mention" or "question" ? Visibility.Visible : Visibility.Collapsed;
        public Visibility ReminderVisibility => i.Kind is "mention" or "question" ? Visibility.Visible : Visibility.Collapsed;
        public Visibility DoneVisibility => i.Kind == "reminder" ? Visibility.Visible : Visibility.Collapsed;
    }
}
