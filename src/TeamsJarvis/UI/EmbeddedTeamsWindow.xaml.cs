using System.IO;
using System.Text.Json;
using System.Threading;
using System.Windows;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Web.WebView2.Core;
using TeamsJarvis.Core;

namespace TeamsJarvis.UI;

/// <summary>
/// Ventana principal de Frida.ai: fusiona el Teams web incrustado (WebView2, sesión persistente)
/// a la izquierda con el panel de respuestas sugeridas a la derecha. No usa la API del tenant.
///
/// La clase está partida en varios archivos parciales por responsabilidad:
///   - EmbeddedTeamsWindow.xaml.cs   → campos, constructor, ciclo de vida y controles de ventana.
///   - EmbeddedTeamsWindow.WebView.cs → WebView2, lector del DOM, navegación, descargas y permisos.
///   - EmbeddedTeamsWindow.Panel.cs   → panel de sugerencias, animaciones, gasto y reescritura de tono.
///   - EmbeddedTeamsWindow.Account.cs → menú de cuenta, tema, cierre de sesión y ventanas auxiliares.
/// </summary>
public partial class EmbeddedTeamsWindow : Window
{
    private readonly AnalysisPipeline _pipeline;
    private readonly JarvisOptions _options;
    private readonly IOptions<JarvisOptions> _optionsAccessor;
    private readonly SuggestionStore _store;
    private readonly ConversationGraphStore _graph;
    private readonly KnowledgeIndexer _knowledge;
    private readonly NotesStore _notes;
    private readonly AttentionService _attention;
    private readonly LocalLlm _local;
    private readonly MemoryBackfillService _backfill;
    private readonly DiagnosticsRunner _diagnostics;
    private readonly ILogger<EmbeddedTeamsWindow> _logger;
    private readonly string _bootstrap;
    private CoreWebView2Environment? _env;
    private string? _currentChatId;
    private string _currentChatName = "";
    private bool _reallyExit;
    private readonly IProgress<string> _progress;

    public EmbeddedTeamsWindow(AnalysisPipeline pipeline, SuggestionStore store, ConversationGraphStore graph,
        KnowledgeIndexer knowledge, NotesStore notes, AttentionService attention, LocalLlm local,
        MemoryBackfillService backfill, DiagnosticsRunner diagnostics, IOptions<JarvisOptions> options,
        ILogger<EmbeddedTeamsWindow> logger)
    {
        InitializeComponent();
        _pipeline = pipeline;
        _store = store;
        _graph = graph;
        _knowledge = knowledge;
        _notes = notes;
        _attention = attention;
        _local = local;
        _backfill = backfill;
        _diagnostics = diagnostics;
        _optionsAccessor = options;
        _options = options.Value;
        _logger = logger;
        Icon = FridaIcon.CreateImageSource();
        _bootstrap = LoadReaderJs();

        InitAccountAndAgent();
        InitMascot();

        _store.SuggestionAdded += s => Dispatcher.Invoke(() =>
        {
            ShowSuggestion(s);
            // Si la ventana está en la bandeja o sin foco, avisa con una notificación enriquecida.
            if (!IsActive || !IsVisible || WindowState == WindowState.Minimized)
                ToastService.ShowSuggestion(s);
        });

        // Progreso en vivo del análisis: cada mensaje sale del bocadillo y la mascota "habla".
        // Síncrono (no Progress<T>, que encola): así llega en orden con los demás avisos del análisis y
        // el último "Lo analizo…" no pisa la respuesta ya pintada.
        _progress = new SynchronousProgress<string>(text => MascotSay(text, keepTalking: true));
        _pipeline.AnalysisRunningChanged += (_, running) => Dispatcher.Invoke(() =>
        {
            if (running) MascotSay("Déjame mirar la conversación…", keepTalking: true);
            else MascotDone();
            PulseAgent(running);
        });
        // Decisión del triaje: la mascota saca la patita con banderín verde (sí) o rojo (no) y lo cuenta.
        _pipeline.TriageDecided += (_, pass, reason) => Dispatcher.Invoke(() =>
        {
            MascotShowFlag(pass);
            MascotSay(pass ? $"¡Sí! {reason}" : $"Hmm… {reason}", keepTalking: pass);
        });
        // Resultado sin sugerencia (triaje lo descarta, no hace falta responder, error o créditos):
        // se muestra SIEMPRE en el panel para no dejarte sin feedback al pulsar el botón.
        _pipeline.OutcomeReported += (_, text) => Dispatcher.Invoke(() => ShowOutcome(text));

        Loaded += async (_, _) => await InitAsync();
    }

    // ---- Ciclo de vida de la ventana ------------------------------------------

    /// <summary>Cerrar = minimizar a la bandeja; la app sigue funcionando.</summary>
    private void OnClosing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        if (_reallyExit) return;
        e.Cancel = true;
        Hide();
    }

    public void ShowAndActivate()
    {
        Show();
        if (WindowState == WindowState.Minimized) WindowState = WindowState.Maximized;
        Activate();
    }

    public void ExitApplication()
    {
        _reallyExit = true;
        Close();
        System.Windows.Application.Current.Shutdown();
    }

    // ---- Controles de la barra de título propia (WindowChrome) ----------------

    private void OnMinimize(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void OnMaxRestore(object sender, RoutedEventArgs e) =>
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

    private void OnCloseWindow(object sender, RoutedEventArgs e) => Close(); // OnClosing la manda a la bandeja

    private void OnWindowStateChanged(object? sender, EventArgs e)
    {
        // WindowChrome recorta ~7px al maximizar: compensar con un margen.
        RootGrid.Margin = WindowState == WindowState.Maximized ? new Thickness(7) : new Thickness(0);
        // Glifo del botón maximizar/restaurar (Segoe MDL2 vía recurso, para evitar caracteres crudos).
        if (MaxRestoreBtn is not null)
            MaxRestoreBtn.Content = FindResource(
                WindowState == WindowState.Maximized ? "GlyphRestore" : "GlyphMaximize");
    }
}
