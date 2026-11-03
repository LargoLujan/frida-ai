using System.Collections.Concurrent;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace TeamsJarvis.Core;

/// <summary>
/// Bucle de fondo: sondea los chats monitorizados, detecta mensajes nuevos de otros,
/// aplica un pequeño debounce (por si el interlocutor sigue escribiendo) y lanza el
/// agente. Cada chat se procesa en paralelo, pero nunca dos análisis del mismo chat.
/// </summary>
public sealed class TeamsMonitorService : BackgroundService
{
    private readonly TeamsService _teams;
    private readonly ContextBuilder _contextBuilder;
    private readonly AnalysisPipeline _pipeline;
    private readonly SuggestionStore _store;
    private readonly GraphAuthService _auth;
    private readonly Tools.McpToolBridge _mcpBridge;
    private readonly JarvisOptions _options;
    private readonly ILogger<TeamsMonitorService> _logger;

    private readonly ConcurrentDictionary<string, Task> _inFlight = new();
    private volatile bool _paused;

    public TeamsMonitorService(
        TeamsService teams, ContextBuilder contextBuilder, AnalysisPipeline pipeline,
        SuggestionStore store, GraphAuthService auth, Tools.McpToolBridge mcpBridge,
        IOptions<JarvisOptions> options, ILogger<TeamsMonitorService> logger)
    {
        _teams = teams;
        _contextBuilder = contextBuilder;
        _pipeline = pipeline;
        _store = store;
        _auth = auth;
        _mcpBridge = mcpBridge;
        _options = options.Value;
        _logger = logger;
    }

    public bool Paused
    {
        get => _paused;
        set
        {
            _paused = value;
            _logger.LogInformation("Monitorización {State}.", value ? "PAUSADA" : "reanudada");
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // En modo "browser" la fuente de mensajes es el Teams incrustado (WebView2); no se sondea Graph.
        if (_options.SourceMode.Equals("browser", StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogInformation("Fuente = navegador: el monitor de Graph queda inactivo (los mensajes llegan por la extensión).");
            try { await _mcpBridge.InitializeAsync(stoppingToken); } catch (Exception ex) { _logger.LogWarning(ex, "MCP init."); }
            return;
        }

        // Autenticación (abre navegador la primera vez) y conexión a MCP, sin bloquear el arranque de la UI.
        try
        {
            await _auth.GetGraphClientAsync(stoppingToken);
            await _mcpBridge.InitializeAsync(stoppingToken);
        }
        catch (OperationCanceledException) { return; }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Fallo inicializando autenticación/MCP. El monitor lo reintentará.");
        }

        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(Math.Max(5, _options.PollSeconds)));
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            if (_paused) continue;

            List<MonitoredChat> chats;
            try
            {
                chats = _store.GetMonitoredChats(enabledOnly: true);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error leyendo chats monitorizados.");
                continue;
            }

            foreach (var chat in chats)
            {
                if (_inFlight.TryGetValue(chat.ChatId, out var running) && !running.IsCompleted)
                    continue;

                _inFlight[chat.ChatId] = ProcessChatSafeAsync(chat, stoppingToken);
            }
        }
    }

    private async Task ProcessChatSafeAsync(MonitoredChat chat, CancellationToken ct)
    {
        try
        {
            await ProcessChatAsync(chat, ct);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error procesando el chat '{Chat}'.", chat.DisplayName);
        }
    }

    private async Task ProcessChatAsync(MonitoredChat chat, CancellationToken ct)
    {
        var recent = await _teams.GetRecentMessagesAsync(chat.ChatId, _options.MaxContextMessages,
            includeImages: false, ct);
        if (recent.Count == 0) return;

        // El pipeline (compartido con la fuente de navegador) hace triaje, análisis y guardado.
        // El contexto completo (transcripción con imágenes + digest) se construye solo si procede.
        await _pipeline.RunAsync(chat.ChatId, chat.DisplayName, recent,
            () => _contextBuilder.BuildAsync(chat.ChatId, chat.DisplayName, ct),
            applyDebounce: true, ct);
    }
}
