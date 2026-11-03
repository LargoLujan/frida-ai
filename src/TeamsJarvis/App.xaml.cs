using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TeamsJarvis.Core;
using TeamsJarvis.Tools;
using TeamsJarvis.UI;

namespace TeamsJarvis;

public partial class App : System.Windows.Application
{
    private IHost? _host;
    private TrayIconController? _tray;

    protected override async void OnStartup(System.Windows.StartupEventArgs e)
    {
        base.OnStartup(e);

        // Manejador global: ningún crash debe ser silencioso (queda en logs\crash.log).
        DispatcherUnhandledException += (_, ex) => { LogCrash(ex.Exception); ex.Handled = false; };
        AppDomain.CurrentDomain.UnhandledException += (_, ex) => LogCrash(ex.ExceptionObject as Exception);

        var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
        {
            ContentRootPath = AppContext.BaseDirectory,
        });

        // Los ajustes del usuario (editados desde la ventana de Configuración) se superponen
        // a appsettings.json, para que la app sea configurable sin tocar archivos a mano.
        builder.Configuration.AddJsonFile(AppPaths.UserSettingsPath, optional: true, reloadOnChange: false);

        builder.Services.Configure<JarvisOptions>(builder.Configuration.GetSection("Jarvis"));
        builder.Logging.AddProvider(new FileLoggerProvider());

        // Si hay API key cifrada en los ajustes, exponerla como variable de entorno del proceso.
        var jarvisOptions = builder.Configuration.GetSection("Jarvis").Get<JarvisOptions>() ?? new JarvisOptions();
        UserSettingsService.ApplyApiKeyToEnvironment(jarvisOptions);
        UI.ThemeManager.Apply(jarvisOptions.Theme);
        UI.BarkPlayer.Enabled = jarvisOptions.BarkEnabled;

        // Núcleo
        builder.Services.AddSingleton<GraphAuthService>();
        builder.Services.AddSingleton<TeamsService>();
        builder.Services.AddSingleton<SuggestionStore>();
        builder.Services.AddSingleton<ConversationGraphStore>();
        builder.Services.AddSingleton<ContextBuilder>();
        builder.Services.AddSingleton<McpToolBridge>();

        // Búsqueda semántica / RAG con embeddings LOCALES (Ollama, coste 0).
        builder.Services.AddSingleton<EmbeddingService>();
        builder.Services.AddSingleton<KnowledgeStore>();
        builder.Services.AddSingleton<KnowledgeIndexer>();

        // Notas de proyecto + recordatorios programables (avisos por toast).
        builder.Services.AddSingleton<NotesStore>();
        builder.Services.AddSingleton<ReminderService>();
        builder.Services.AddHostedService(sp => sp.GetRequiredService<ReminderService>());

        // "Requiere tu atención hoy" (agregación sin coste) + LLM local para resúmenes gratis.
        builder.Services.AddSingleton<AttentionService>();
        builder.Services.AddSingleton<LocalLlm>();
        builder.Services.AddSingleton<MemoryBackfillService>();

        // Agentes de IA: se registran los tres y se selecciona por configuración.
        builder.Services.AddSingleton<AgentPromptBuilder>();
        builder.Services.AddSingleton<ClaudeAgent>();
        builder.Services.AddSingleton<OpenAiAgent>();
        builder.Services.AddSingleton<GeminiAgent>();
        builder.Services.AddSingleton<OllamaAgent>();
        builder.Services.AddSingleton<SimulatedAgent>();
        builder.Services.AddSingleton<AgentSelector>();

        // Tools nativas del agente
        builder.Services.AddSingleton<IAgentTool, SharePointSearchTool>();
        builder.Services.AddSingleton<IAgentTool, SharePointReadTool>();
        builder.Services.AddSingleton<IAgentTool, FabricApiTool>();
        builder.Services.AddSingleton<IAgentTool, RunCliTool>();
        builder.Services.AddSingleton<IAgentTool, ListChatsTool>();
        builder.Services.AddSingleton<IAgentTool, GetChatMessagesTool>();
        builder.Services.AddSingleton<IAgentTool, AttachmentReadTool>();

        // Pipeline de análisis compartido por las dos fuentes de mensajes.
        builder.Services.AddSingleton<AnalysisPipeline>();

        // Monitor de Graph en segundo plano (accesible también como singleton para la UI).
        builder.Services.AddSingleton<TeamsMonitorService>();
        builder.Services.AddHostedService(sp => sp.GetRequiredService<TeamsMonitorService>());

        // UI — la ventana principal es el Teams incrustado (fusión Teams + Frida).
        builder.Services.AddSingleton<DiagnosticsRunner>();
        builder.Services.AddSingleton<EmbeddedTeamsWindow>();

        _host = builder.Build();
        await _host.StartAsync();

        var mainWindow = _host.Services.GetRequiredService<EmbeddedTeamsWindow>();
        var monitor = _host.Services.GetRequiredService<TeamsMonitorService>();
        var store = _host.Services.GetRequiredService<SuggestionStore>();
        var notes = _host.Services.GetRequiredService<NotesStore>();

        ToastService.Initialize(store, notes, () => mainWindow.ShowAndActivate());

        // Avisos de recordatorios vencidos: toast de Windows.
        _host.Services.GetRequiredService<ReminderService>().Notify =
            r => Dispatcher.Invoke(() => ToastService.ShowReminder(r));

        _tray = new TrayIconController(mainWindow, monitor);

        mainWindow.Show();

        // RAG local: si el usuario lo tiene activado y los embeddings responden (Ollama), activarlo
        // e indexar el vault en segundo plano. Si no, se sigue con el volcado completo del vault.
        _ = InitializeRagAsync(jarvisOptions);

        // Primer arranque sin configurar: abrir el asistente de configuración guiado.
        if (UserSettingsService.NeedsSetup(jarvisOptions))
        {
            var settings = new SettingsWindow(_host.Services.GetRequiredService<IOptions<JarvisOptions>>())
            {
                Owner = mainWindow,
            };
            settings.ShowDialog();
        }
    }

    private async Task InitializeRagAsync(JarvisOptions options)
    {
        if (_host is null || (!options.RagEnabled && !options.IndexConversations)) return;
        try
        {
            var embeddings = _host.Services.GetRequiredService<EmbeddingService>();
            if (!await embeddings.IsAvailableAsync())
                return; // Ollama/embeddings no disponibles: se mantiene el volcado completo del vault.

            var indexer = _host.Services.GetRequiredService<KnowledgeIndexer>();

            // RAG en el prompt: activar y volcar solo lo relevante por consulta (menos tokens).
            if (options.RagEnabled)
            {
                _host.Services.GetRequiredService<AgentPromptBuilder>().RagActive = true;
                await indexer.IndexVaultAsync();
            }

            // Búsqueda semántica sobre conversaciones (incremental, en segundo plano).
            if (options.IndexConversations)
                await indexer.IndexConversationsAsync();

            // Reconstruir en LOCAL (gratis) la memoria de los chats que aún no la tengan.
            await _host.Services.GetRequiredService<MemoryBackfillService>().BackfillAsync();
        }
        catch (Exception ex)
        {
            _host.Services.GetService<ILogger<App>>()?.LogWarning(ex, "No se pudo inicializar el RAG.");
        }
    }

    private static void LogCrash(Exception? ex)
    {
        try
        {
            var path = System.IO.Path.Combine(AppPaths.LogsDir, "crash.log");
            System.IO.File.AppendAllText(path, $"{DateTimeOffset.Now:O}  {ex}\n\n");
        }
        catch { }
    }

    protected override async void OnExit(System.Windows.ExitEventArgs e)
    {
        _tray?.Dispose();
        if (_host is not null)
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            try { await _host.StopAsync(cts.Token); } catch { }
            _host.Dispose();
        }
        base.OnExit(e);
    }
}
