using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using TeamsJarvis.Core;

namespace TeamsJarvis.UI;

public partial class ChatSelectionWindow : Window
{
    private readonly TeamsService _teams;
    private readonly SuggestionStore _store;
    private readonly ObservableCollection<SelectableChatVm> _all = new();

    public ChatSelectionWindow(TeamsService teams, SuggestionStore store)
    {
        InitializeComponent();
        _teams = teams;
        _store = store;
        ChatsList.ItemsSource = _all;
        Loaded += async (_, _) => await LoadAsync();
    }

    private async Task LoadAsync()
    {
        StatusText.Text = "Cargando chats de Teams...";
        try
        {
            var chats = await _teams.ListChatsAsync();
            var monitored = _store.GetMonitoredChats()
                .Where(c => c.Enabled)
                .Select(c => c.ChatId)
                .ToHashSet();

            _all.Clear();
            foreach (var chat in chats)
                _all.Add(new SelectableChatVm(chat.ChatId, chat.DisplayName) { Enabled = monitored.Contains(chat.ChatId) });

            StatusText.Text = $"{chats.Count} chats disponibles";
        }
        catch (Exception ex)
        {
            StatusText.Text = "Error cargando chats";
            MessageBox.Show(this, $"No se pudieron cargar los chats:\n{ex.Message}", "Frida.ai",
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void OnFilterChanged(object sender, TextChangedEventArgs e)
    {
        var filter = FilterBox.Text.Trim();
        ChatsList.ItemsSource = string.IsNullOrEmpty(filter)
            ? _all
            : _all.Where(c => c.Name.Contains(filter, StringComparison.OrdinalIgnoreCase)).ToList();
    }

    private void OnSave(object sender, RoutedEventArgs e)
    {
        foreach (var chat in _all)
            _store.UpsertMonitoredChat(new MonitoredChat(chat.ChatId, chat.Name, chat.Enabled));
        DialogResult = true;
        Close();
    }

    private void OnCancel(object sender, RoutedEventArgs e) => Close();

    public sealed class SelectableChatVm(string chatId, string name)
    {
        public string ChatId { get; } = chatId;
        public string Name { get; } = name;
        public bool Enabled { get; set; }
    }
}
