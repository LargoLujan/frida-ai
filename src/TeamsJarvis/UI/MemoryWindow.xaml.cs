using System.Text;
using System.Windows;
using TeamsJarvis.Core;

namespace TeamsJarvis.UI;

public partial class MemoryWindow : Window
{
    private readonly SuggestionStore _store;
    private readonly ConversationGraphStore _graph;
    private readonly LocalLlm _local;
    private readonly string _chatId;
    private readonly string _chatName;

    public MemoryWindow(SuggestionStore store, ConversationGraphStore graph, LocalLlm local, string chatId, string chatName)
    {
        InitializeComponent();
        _store = store;
        _graph = graph;
        _local = local;
        _chatId = chatId;
        _chatName = chatName;
        Icon = FridaIcon.CreateImageSource();
        HeaderText.Text = $"Memoria — {chatName}";
        Load(_store.GetChatMemory(chatId));
    }

    private void Load(string memory)
    {
        var sections = MemoryDoc.Parse(memory);
        if (sections.Count == 0)
            sections = MemoryDoc.KnownSections
                .Select(s => new MemorySection { Title = ToTitle(s), Content = "" }).ToList();
        SectionsList.ItemsSource = sections;
    }

    private List<MemorySection> CurrentSections() =>
        (SectionsList.ItemsSource as List<MemorySection>) ?? new();

    private void OnSave(object sender, RoutedEventArgs e)
    {
        // Fuerza el commit de la edición en curso (el binding es LostFocus).
        (System.Windows.Input.FocusManager.GetFocusedElement(this) as System.Windows.Controls.TextBox)?
            .GetBindingExpression(System.Windows.Controls.TextBox.TextProperty)?.UpdateSource();

        var text = MemoryDoc.ToText(CurrentSections());
        _store.SetChatMemory(_chatId, text);
        StatusText.Text = "Memoria guardada.";
    }

    private async void OnRebuild(object sender, RoutedEventArgs e)
    {
        StatusText.Text = "Reconstruyendo en local (gratis)…";
        var transcript = BuildTranscript();
        if (transcript.Length < 40)
        {
            StatusText.Text = "No hay suficientes mensajes indexados de este chat para reconstruir.";
            return;
        }
        var summary = await _local.SummarizeConversationMemoryAsync(_chatName, transcript);
        if (string.IsNullOrWhiteSpace(summary))
        {
            StatusText.Text = "Modelo local no disponible (instala Ollama y 'ollama pull llama3.2:3b').";
            return;
        }
        _store.SetChatMemory(_chatId, summary.Trim());
        Load(summary);
        StatusText.Text = "Memoria reconstruida en local.";
    }

    private string BuildTranscript()
    {
        var sb = new StringBuilder();
        foreach (var m in _graph.GetRecentForChat(_chatId, 60))
            sb.AppendLine($"[{m.CreatedAt.ToLocalTime():dd/MM HH:mm}] {(m.IsMe ? "Yo" : m.AuthorName)}: {ContextBuilder.Truncate(m.Text, 300)}");
        return sb.ToString();
    }

    private static string ToTitle(string upper) => upper switch
    {
        "TEMAS" => "Temas",
        "DECISIONES Y ACUERDOS" => "Decisiones y acuerdos",
        "PENDIENTES Y TAREAS" => "Pendientes y tareas",
        "PERSONAS" => "Personas",
        "FECHAS E HITOS" => "Fechas e hitos",
        _ => upper,
    };
}
