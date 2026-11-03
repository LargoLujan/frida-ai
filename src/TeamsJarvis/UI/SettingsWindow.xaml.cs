using System.Diagnostics;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Extensions.Options;
using TeamsJarvis.Core;

namespace TeamsJarvis.UI;

public partial class SettingsWindow : Window
{
    private readonly JarvisOptions _current;
    private readonly bool _hadApiKey;

    public SettingsWindow(IOptions<JarvisOptions> options)
    {
        InitializeComponent();
        _current = options.Value;
        _hadApiKey = !string.IsNullOrWhiteSpace(_current.ApiKeyProtected);
        Icon = FridaIcon.CreateImageSource();
        LoadValues();
    }

    private void LoadValues()
    {
        SelectByTag(SourceModeBox, _current.SourceMode);
        SelectByTag(AuthModeBox, _current.AuthMode);
        UpdateAuthModeVisibility();
        UpdateSourceModeVisibility();
        TenantIdBox.Text = _current.TenantId;
        ClientIdBox.Text = _current.ClientId;

        SelectByTag(ProviderBox, _current.Provider);
        SelectByTag(TriageProviderBox, _current.TriageProvider);
        PopulateModels(_current.Provider);
        SetCombo(ModelBox, _current.Model);
        SetCombo(EffortBox, _current.Effort);
        if (_hadApiKey) ApiKeyBox.Password = "";       // no se muestra la clave; vacío = conservar
        TriageBox.IsChecked = _current.TriageEnabled;

        OllamaUrlBox.Text = _current.OllamaBaseUrl;
        EmbeddingBox.Text = _current.EmbeddingModel;
        RagBox.IsChecked = _current.RagEnabled;
        IndexConvBox.IsChecked = _current.IndexConversations;

        NameBox.Text = _current.Profile.Name;
        RoleBox.Text = _current.Profile.Role;
        SelectByTag(LanguageBox, _current.Profile.Language);
        ExtraBox.Text = _current.Profile.ExtraInstructions;

        ContextBox.Text = _current.MaxContextMessages.ToString();
        ImagesBox.Text = _current.MaxImages.ToString();
        StartupBox.IsChecked = StartupHelper.IsEnabled();

        AzBox.IsChecked = _current.Cli.Az.Enabled;
        FabBox.IsChecked = _current.Cli.Fab.Enabled;
        KnowledgeBox.Text = _current.KnowledgeDir;
        McpBox.Text = McpConfigParser.ToStandardJson(_current.McpServers);

        if (_hadApiKey) SaveStatus.Text = "Clave de API guardada (déjala vacía para conservarla).";
    }

    private void OnSave(object sender, RoutedEventArgs e)
    {
        // Validar MCP JSON antes de nada. Acepta el formato estándar {"mcpServers": {...}},
        // un mapa suelto, o el array nativo (McpConfigParser es tolerante).
        List<McpServerOptions> mcp;
        try
        {
            mcp = McpConfigParser.Parse(McpBox.Text);
            McpError.Visibility = Visibility.Collapsed;
        }
        catch (Exception ex)
        {
            McpError.Text = $"El JSON de servidores MCP no es válido: {ex.Message}";
            McpError.Visibility = Visibility.Visible;
            return;
        }

        var edited = new JarvisOptions
        {
            SourceMode = (SourceModeBox.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "graph",
            AuthMode = (AuthModeBox.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "azcli",
            TenantId = TenantIdBox.Text.Trim(),
            ClientId = ClientIdBox.Text.Trim(),
            Provider = (ProviderBox.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "claude",
            Model = GetCombo(ModelBox, "claude-opus-4-8"),
            Effort = GetCombo(EffortBox, "high"),
            TriageEnabled = TriageBox.IsChecked == true,
            TriageProvider = (TriageProviderBox.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "",
            TriageModel = _current.TriageModel,
            OllamaBaseUrl = string.IsNullOrWhiteSpace(OllamaUrlBox.Text) ? "http://127.0.0.1:11434" : OllamaUrlBox.Text.Trim(),
            EmbeddingModel = string.IsNullOrWhiteSpace(EmbeddingBox.Text) ? "nomic-embed-text" : EmbeddingBox.Text.Trim(),
            RagEnabled = RagBox.IsChecked == true,
            RagTopK = _current.RagTopK,
            IndexConversations = IndexConvBox.IsChecked == true,
            ApiKeyProtected = _current.ApiKeyProtected, // se recalcula en Save si hay clave nueva
            PollSeconds = _current.PollSeconds,
            DebounceSeconds = _current.DebounceSeconds,
            MaxContextMessages = ParseInt(ContextBox.Text, 30),
            MaxImages = ParseInt(ImagesBox.Text, 5),
            MaxToolIterations = _current.MaxToolIterations,
            StartWithWindows = StartupBox.IsChecked == true,
            Theme = _current.Theme,
            BarkEnabled = _current.BarkEnabled,
            HistoryRounds = _current.HistoryRounds,
            KnowledgeDir = KnowledgeBox.Text.Trim(),
            Profile = new ProfileOptions
            {
                Name = NameBox.Text.Trim(),
                Role = RoleBox.Text.Trim(),
                Language = (LanguageBox.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "es",
                ExtraInstructions = ExtraBox.Text.Trim(),
            },
            Cli = new CliOptions
            {
                Az = new CliToolOptions { Enabled = AzBox.IsChecked == true, AllowedVerbs = _current.Cli.Az.AllowedVerbs },
                Fab = new CliToolOptions { Enabled = FabBox.IsChecked == true, AllowedVerbs = _current.Cli.Fab.AllowedVerbs },
            },
            McpServers = mcp,
        };

        // Arranque con Windows (registro Run del usuario).
        StartupHelper.SetEnabled(StartupBox.IsChecked == true);

        // Clave de API: solo se reescribe si el usuario escribió una nueva.
        var newKey = ApiKeyBox.Password;
        UserSettingsService.Save(edited, string.IsNullOrEmpty(newKey) ? null : newKey);

        var restart = MessageBox.Show(this,
            "Configuración guardada.\n\n¿Reiniciar Jarvis ahora para aplicar los cambios?",
            "Frida.ai", MessageBoxButton.YesNo, MessageBoxImage.Question);

        if (restart == MessageBoxResult.Yes && Environment.ProcessPath is { } exe)
        {
            Process.Start(exe);
            Application.Current.Shutdown();
        }
        else
        {
            DialogResult = true;
            Close();
        }
    }

    private void OnCancel(object sender, RoutedEventArgs e) => Close();

    private void OnAuthModeChanged(object sender, SelectionChangedEventArgs e) => UpdateAuthModeVisibility();

    private void OnSourceModeChanged(object sender, SelectionChangedEventArgs e) => UpdateSourceModeVisibility();

    private void UpdateSourceModeVisibility()
    {
        var isBrowser = (SourceModeBox.SelectedItem as ComboBoxItem)?.Tag?.ToString() == "browser";
        if (BrowserHelp is not null) BrowserHelp.Visibility = isBrowser ? Visibility.Visible : Visibility.Collapsed;
        if (AuthMethodPanel is not null) AuthMethodPanel.Visibility = isBrowser ? Visibility.Collapsed : Visibility.Visible;
    }

    private void OnProviderChanged(object sender, SelectionChangedEventArgs e)
    {
        var provider = (ProviderBox.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "claude";
        var previous = ModelBox.Text;
        PopulateModels(provider);
        // Si el modelo actual no encaja con el proveedor, seleccionar el primero sugerido.
        if (!ModelBox.Items.Cast<ComboBoxItem>().Any(i => i.Content?.ToString() == previous))
            ModelBox.SelectedIndex = 0;
    }

    private void PopulateModels(string provider)
    {
        string[] models = provider.ToLowerInvariant() switch
        {
            "openai" => ["gpt-4o", "gpt-4o-mini", "gpt-4.1", "gpt-4.1-mini", "o4-mini"],
            "gemini" => ["gemini-2.0-flash", "gemini-1.5-pro", "gemini-1.5-flash"],
            "ollama" => ["llama3.2:3b", "qwen2.5:3b", "llama3.1:8b", "qwen2.5:7b", "gemma2:2b"],
            "simulation" => ["claude-opus-4-8", "claude-sonnet-5"],
            _ => ["claude-opus-4-8", "claude-sonnet-5", "claude-haiku-4-5"],
        };
        ModelBox.Items.Clear();
        foreach (var m in models) ModelBox.Items.Add(new ComboBoxItem { Content = m });
    }

    private void UpdateAuthModeVisibility()
    {
        // AppFields/AzCliHelp pueden ser null durante InitializeComponent; proteger.
        var isApp = (AuthModeBox.SelectedItem as ComboBoxItem)?.Tag?.ToString() == "app";
        if (AppFields is not null) AppFields.Visibility = isApp ? Visibility.Visible : Visibility.Collapsed;
        if (AzCliHelp is not null) AzCliHelp.Visibility = isApp ? Visibility.Collapsed : Visibility.Visible;
    }

    // ---- Helpers de controles -------------------------------------------------

    private static void SelectByTag(ComboBox combo, string tag)
    {
        foreach (ComboBoxItem item in combo.Items)
            if (string.Equals(item.Tag?.ToString(), tag, StringComparison.OrdinalIgnoreCase))
            {
                combo.SelectedItem = item;
                return;
            }
        combo.SelectedIndex = 0;
    }

    private static void SetCombo(ComboBox combo, string value)
    {
        foreach (ComboBoxItem item in combo.Items)
            if (string.Equals(item.Content?.ToString(), value, StringComparison.OrdinalIgnoreCase))
            {
                combo.SelectedItem = item;
                return;
            }
        if (combo.IsEditable) combo.Text = value;
        else combo.SelectedIndex = combo.Items.Count > 0 ? 0 : -1;
    }

    private static string GetCombo(ComboBox combo, string fallback)
    {
        if (combo.SelectedItem is ComboBoxItem item && item.Content is not null)
            return item.Content.ToString()!;
        return string.IsNullOrWhiteSpace(combo.Text) ? fallback : combo.Text.Trim();
    }

    private static int ParseInt(string text, int fallback) =>
        int.TryParse(text.Trim(), out var v) && v > 0 ? v : fallback;
}
