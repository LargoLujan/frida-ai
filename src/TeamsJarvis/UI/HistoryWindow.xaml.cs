using System.Windows;
using System.Windows.Controls;
using TeamsJarvis.Core;

namespace TeamsJarvis.UI;

public partial class HistoryWindow : Window
{
    public HistoryWindow(SuggestionStore store, string chatId, string chatName)
    {
        InitializeComponent();
        Title = $"Sugerencias anteriores — {chatName}";
        var items = store.GetSuggestions(100, chatId)
            .Select(s => new HistoryItemVm(s))
            .ToList();
        HistoryList.ItemsSource = items;
        if (items.Count > 0) HistoryList.SelectedIndex = 0;
    }

    private void OnSelected(object sender, SelectionChangedEventArgs e)
    {
        if (HistoryList.SelectedItem is not HistoryItemVm item) return;
        DetailHeader.Text = $"Para responder a {item.Suggestion.ReplyTo} · {item.Suggestion.CreatedAt.ToLocalTime():dd/MM/yyyy HH:mm} · confianza {item.Suggestion.Confidence:P0}";
        DetailText.Text = item.Suggestion.SuggestedReply
            + (string.IsNullOrWhiteSpace(item.Suggestion.Sources) ? "" : $"\n\n--- Fuentes ---\n{item.Suggestion.Sources}");
    }

    private void OnCopy(object sender, RoutedEventArgs e)
    {
        if (HistoryList.SelectedItem is HistoryItemVm item)
            Clipboard.SetText(item.Suggestion.SuggestedReply);
    }

    private sealed class HistoryItemVm(Suggestion suggestion)
    {
        public Suggestion Suggestion { get; } = suggestion;
        public string Header => $"{Suggestion.CreatedAt.ToLocalTime():dd/MM HH:mm} → {Suggestion.ReplyTo}";
        public string Preview => Suggestion.SuggestedReply;
    }
}
