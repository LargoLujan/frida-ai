using System.IO;
using System.Text.Json;
using System.Threading;
using System.Windows;
using Microsoft.Extensions.Logging;
using Microsoft.Web.WebView2.Core;
using TeamsJarvis.Core;

namespace TeamsJarvis.UI;

// WebView2, lector del DOM de Teams, navegación, permisos de llamada y descargas.
public partial class EmbeddedTeamsWindow
{
    private static string LoadReaderJs()
    {
        using var s = typeof(EmbeddedTeamsWindow).Assembly
            .GetManifestResourceStream("TeamsJarvis.Resources.teamsReader.js")!;
        using var r = new StreamReader(s);
        return r.ReadToEnd();
    }

    private async Task InitAsync()
    {
        Status.Text = "Iniciando Teams integrado...";
        try
        {
            var udf = Path.Combine(AppPaths.DataDir, "webview");
            Directory.CreateDirectory(udf);
            var opts = new CoreWebView2EnvironmentOptions("--autoplay-policy=no-user-gesture-required");
            _env = await CoreWebView2Environment.CreateAsync(null, udf, opts);
            await Web.EnsureCoreWebView2Async(_env);

            var core = Web.CoreWebView2;
            core.Settings.IsStatusBarEnabled = false;
            AllowCallPermissions(core);
            core.NewWindowRequested += OnNewWindow;
            core.DownloadStarting += OnDownloadStarting;

            // Inyecta mi nombre (para detectar menciones) + el lector.
            var boot = $"window.__tjMe = {JsonSerializer.Serialize(_options.Profile.Name ?? "")};\n" + _bootstrap;
            await core.AddScriptToExecuteOnDocumentCreatedAsync(boot);
            core.WebMessageReceived += OnWebMessage;

            // Entramos por teams.microsoft.com (flujo normal de login + redirección), no por el
            // deep-link a cloud.microsoft: así se completa la autenticación y se evita el
            // "Vuelva a conectarse" que aparece cuando la sesión no llega a establecerse.
            core.Navigate("https://teams.microsoft.com/");
            Status.Text = "Inicia sesión en Teams si te lo pide. Las sugerencias aparecen a la derecha.";
        }
        catch (Exception ex)
        {
            Status.Text = "No se pudo iniciar WebView2 (¿runtime de Edge WebView2?): " + ex.Message;
            _logger.LogError(ex, "Fallo iniciando WebView2.");
        }
    }

    private async void OnWebMessage(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        string json;
        try { json = e.TryGetWebMessageAsString(); }
        catch { return; }

        var p = BrowserPayloadParser.Parse(json);
        if (p is null) return;

        // Actualiza SIEMPRE el chat actual (aunque el aviso no traiga mensajes: es tracking de
        // navegación), para que Memoria/Histórico cojan al instante el chat donde estás.
        _currentChatId = p.ChatId;
        _currentChatName = p.ChatName;
        if (p.Messages.Count == 0) return;

        var men = p.Messages.Any(m => !m.IsMe && m.IsMention) ? " 🔔" : "";

        // SOLO el botón (force) llama a la IA (triaje + análisis). Las lecturas automáticas
        // NO gastan créditos: solo indexan los mensajes para la búsqueda (gratis).
        if (!p.Force)
        {
            try { _graph.IndexMessages(p.ChatId, p.ChatName, p.Messages); }
            catch (Exception ex) { _logger.LogDebug(ex, "No se pudo indexar (auto)."); }
            Status.Text = $"{p.ChatName}: {p.Messages.Count} mensajes{men} — pulsa «Sugerir respuesta» para analizar.";
            return;
        }

        Status.Text = $"{p.ChatName}: analizando {p.Messages.Count} mensajes{men}…";
        try
        {
            await _pipeline.RunAsync(p.ChatId, p.ChatName, p.Messages,
                () => Task.FromResult(new ChatContext(p.ChatId, p.ChatName, p.Messages, "")),
                applyDebounce: false, CancellationToken.None, force: true, progress: _progress);
        }
        catch (Exception ex) { _logger.LogWarning(ex, "Error analizando (WebView2)."); }
    }

    /// <summary>"Sugerir respuesta": lee el chat abierto (con histórico opcional) y fuerza el análisis.</summary>
    private async void OnAnalyze(object sender, RoutedEventArgs e)
    {
        if (Web.CoreWebView2 is null) return;
        var rounds = LoadHistoryBox.IsChecked == true ? Math.Max(1, _options.HistoryRounds) : 0;
        Status.Text = rounds > 0 ? "Cargando histórico y leyendo el chat..." : "Leyendo el chat...";
        MascotSay(rounds > 0 ? "Cargando el histórico y leyendo el chat…" : "Leyendo el chat…", keepTalking: true);
        try { await Web.CoreWebView2.ExecuteScriptAsync($"window.__tjRead(true, {rounds})"); }
        catch (Exception ex) { Status.Text = "Error al leer: " + ex.Message; }
    }

    private async void OnToggleAuto(object sender, RoutedEventArgs e)
    {
        if (Web.CoreWebView2 is null) return;
        var on = AutoToggle.IsChecked == true;
        await Web.CoreWebView2.ExecuteScriptAsync($"window.__tjAuto = {(on ? "true" : "false")};");
        Status.Text = on
            ? "Auto-leer: guarda los mensajes para búsqueda (gratis). Las sugerencias, solo con el botón."
            : "Auto-leer desactivado. Usa «Sugerir respuesta» cuando quieras.";
    }

    private void OnReload(object sender, RoutedEventArgs e) => Web.CoreWebView2?.Reload();

    /// <summary>Lee del DOM el chat abierto AHORA MISMO (por si el tracking no ha llegado aún).</summary>
    private async Task RefreshCurrentChatAsync()
    {
        if (Web.CoreWebView2 is null) return;
        try
        {
            // __tjCurrentChat() devuelve una cadena JSON; ExecuteScriptAsync la envuelve otra vez en JSON.
            var raw = await Web.CoreWebView2.ExecuteScriptAsync("window.__tjCurrentChat && window.__tjCurrentChat()");
            var inner = JsonSerializer.Deserialize<string>(raw);
            if (string.IsNullOrWhiteSpace(inner)) return;
            var id = GetProp(inner, "chatId");
            if (!string.IsNullOrWhiteSpace(id))
            {
                _currentChatId = id;
                _currentChatName = GetProp(inner, "chatName");
            }
        }
        catch (Exception ex) { _logger.LogDebug(ex, "No se pudo leer el chat actual del DOM."); }
    }

    private static string GetProp(string json, string name)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            return doc.RootElement.TryGetProperty(name, out var v) ? v.GetString() ?? "" : "";
        }
        catch { return ""; }
    }

    /// <summary>
    /// Arregla el error "Vuelva a conectarse a Teams": limpia caché, almacenamiento de caché y
    /// service workers (la causa habitual del cuelgue) SIN borrar cookies, así no pierdes la sesión.
    /// Luego recarga Teams por su URL normal.
    /// </summary>
    private async void OnReconnectTeams(object sender, RoutedEventArgs e)
    {
        var core = Web.CoreWebView2;
        if (core is null) return;
        Status.Text = "Reconectando Teams (limpiando caché, sin cerrar sesión)…";
        try
        {
            await core.Profile.ClearBrowsingDataAsync(
                CoreWebView2BrowsingDataKinds.DiskCache
                | CoreWebView2BrowsingDataKinds.CacheStorage
                | CoreWebView2BrowsingDataKinds.ServiceWorkers);
            core.Navigate("https://teams.microsoft.com/");
            Status.Text = "Teams recargado. Si sigue fallando, prueba '🔌 Reconectar' de nuevo o reinicia la app.";
        }
        catch (Exception ex)
        {
            Status.Text = "No se pudo reconectar: " + ex.Message;
            _logger.LogWarning(ex, "Reconectar Teams falló.");
        }
    }

    /// <summary>
    /// Concede cámara, micrófono y compartir pantalla (y demás permisos que pida Teams) para que
    /// las videollamadas funcionen dentro del WebView2. El micro/cámara/altavoz concretos se eligen
    /// dentro de Teams (Configuración → Dispositivos), como en Teams normal.
    /// </summary>
    private void AllowCallPermissions(CoreWebView2 core)
    {
        core.PermissionRequested += (_, e) =>
        {
            // Cámara, micrófono, compartir pantalla, notificaciones… todo lo que Teams necesita.
            e.State = CoreWebView2PermissionState.Allow;
        };
    }

    private async void OnNewWindow(object? sender, CoreWebView2NewWindowRequestedEventArgs e)
    {
        var deferral = e.GetDeferral();
        try
        {
            var wv = new Microsoft.Web.WebView2.Wpf.WebView2();
            // Ventana grande: sirve tanto para el login como para una llamada emergente de Teams.
            var win = new Window { Title = "Frida.ai", Width = 1000, Height = 720, Content = wv, Icon = FridaIcon.CreateImageSource() };
            win.Show();
            await wv.EnsureCoreWebView2Async(_env);
            // La ventana emergente también necesita permisos de cámara/micro/pantalla para llamadas.
            AllowCallPermissions(wv.CoreWebView2);
            wv.CoreWebView2.NewWindowRequested += OnNewWindow;
            e.NewWindow = wv.CoreWebView2;
        }
        catch (Exception ex) { _logger.LogWarning(ex, "No se pudo abrir la ventana emergente."); }
        finally { deferral.Complete(); }
    }

    private void OnDownloadStarting(object? sender, CoreWebView2DownloadStartingEventArgs e)
    {
        try
        {
            var dir = Path.Combine(AppPaths.DataDir, "downloads");
            Directory.CreateDirectory(dir);
            e.ResultFilePath = Path.Combine(dir, Path.GetFileName(e.ResultFilePath));
            Status.Text = "Adjunto descargado: " + Path.GetFileName(e.ResultFilePath);
        }
        catch { }
    }
}
