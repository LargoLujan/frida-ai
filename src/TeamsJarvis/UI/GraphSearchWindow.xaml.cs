using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using TeamsJarvis.Core;

namespace TeamsJarvis.UI;

public partial class GraphSearchWindow : Window
{
    private readonly ConversationGraphStore _graph;
    private readonly KnowledgeIndexer _knowledge;
    private string? _personFilter;
    private string? _chatFilter;

    public GraphSearchWindow(ConversationGraphStore graph, KnowledgeIndexer knowledge)
    {
        InitializeComponent();
        _graph = graph;
        _knowledge = knowledge;
        Icon = FridaIcon.CreateImageSource();
        PeopleList.ItemsSource = _graph.GetPeople();
        RunSearch();
    }

    private void OnSearch(object sender, RoutedEventArgs e) => Dispatch();

    private void OnSearchKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) Dispatch();
    }

    private void OnModeChanged(object sender, RoutedEventArgs e) => Dispatch();

    private void Dispatch()
    {
        if (SemanticBox.IsChecked == true) _ = RunSemanticAsync();
        else RunSearch();
    }

    private void OnPersonSelected(object sender, SelectionChangedEventArgs e)
    {
        if (PeopleList.SelectedItem is not GraphPerson person) return;
        _personFilter = person.PersonId;
        RelHeader.Text = $"Relaciones · {person.Name}";
        ChatsOfPerson.ItemsSource = _graph.GetChatsForPerson(person.PersonId);
        CoParticipants.ItemsSource = _graph.GetCoParticipants(person.PersonId);
        Dispatch();
    }

    private void OnClearPerson(object sender, RoutedEventArgs e)
    {
        _personFilter = null;
        PeopleList.SelectedItem = null;
        RelHeader.Text = "Relaciones";
        ChatsOfPerson.ItemsSource = null;
        CoParticipants.ItemsSource = null;
        Dispatch();
    }

    private void OnChatChip(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string chatId })
        {
            _chatFilter = _chatFilter == chatId ? null : chatId;
            Dispatch();
        }
    }

    private void RunSearch()
    {
        var results = _graph.Search(SearchBox.Text, _personFilter, _chatFilter, limit: 200)
            .Select(ResultVm.FromMessage)
            .ToList();
        ResultsList.ItemsSource = results;

        var filters = new List<string>();
        if (_personFilter is not null && PeopleList.SelectedItem is GraphPerson p) filters.Add($"persona: {p.Name}");
        if (_chatFilter is not null) filters.Add("chat filtrado");
        var filterText = filters.Count > 0 ? $" ({string.Join(", ", filters)})" : "";
        ResultsHeader.Text = $"Resultados: {results.Count}{filterText} · {_graph.MessageCount()} mensajes indexados";
    }

    private async Task RunSemanticAsync()
    {
        var query = SearchBox.Text?.Trim() ?? "";
        if (query.Length == 0)
        {
            ResultsHeader.Text = "Búsqueda semántica: escribe qué buscas por significado.";
            ResultsList.ItemsSource = null;
            return;
        }

        ResultsHeader.Text = "Buscando por significado…";
        var hits = await _knowledge.SemanticSearchAsync(query, topK: 60, kind: "chat");
        if (_chatFilter is not null) hits = hits.Where(h => h.SourceRef == _chatFilter).ToList();

        if (hits.Count == 0)
        {
            ResultsList.ItemsSource = null;
            ResultsHeader.Text = await _knowledge.EmbeddingsAvailableAsync()
                ? "Sin coincidencias semánticas. ¿Ya se han indexado las conversaciones?"
                : "Búsqueda semántica no disponible: instala Ollama y ejecuta 'ollama pull nomic-embed-text'.";
            return;
        }

        ResultsList.ItemsSource = hits.Select(ResultVm.FromHit).ToList();
        ResultsHeader.Text = $"Resultados por significado: {hits.Count} (los más parecidos primero)";
    }

    private sealed class ResultVm
    {
        public string AuthorName { get; init; } = "";
        public string ChatName { get; init; } = "";
        public string TimeText { get; init; } = "";
        public string Text { get; init; } = "";
        public string ScoreText { get; init; } = "";
        public Visibility ScoreVisibility => string.IsNullOrEmpty(ScoreText) ? Visibility.Collapsed : Visibility.Visible;

        public static ResultVm FromMessage(StoredMessage m) => new()
        {
            AuthorName = m.IsMe ? "Yo" : m.AuthorName,
            ChatName = m.ChatName,
            TimeText = m.CreatedAt.ToLocalTime().ToString("dd/MM/yyyy HH:mm"),
            Text = m.Text,
        };

        public static ResultVm FromHit(KnowledgeHit h)
        {
            // El título de un mensaje viene empaquetado: autor · chat · fechaISO.
            var parts = h.Title.Split(KnowledgeIndexer.MetaSep);
            var author = parts.Length > 0 ? parts[0] : "";
            var chat = parts.Length > 1 ? parts[1] : h.Title;
            var time = parts.Length > 2 && DateTimeOffset.TryParse(parts[2], out var t)
                ? t.ToLocalTime().ToString("dd/MM/yyyy HH:mm") : "";
            return new ResultVm
            {
                AuthorName = author,
                ChatName = chat,
                TimeText = time,
                Text = h.Text,
                ScoreText = $"{h.Score:P0}",
            };
        }
    }
}
