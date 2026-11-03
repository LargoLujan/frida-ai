using System.Windows;
using Microsoft.Extensions.Logging;
using Microsoft.Web.WebView2.Core;
using TeamsJarvis.Core;

namespace TeamsJarvis.UI;

// Menú de cuenta: tema, reconstruir memoria, cierre de sesión y ventanas auxiliares.
public partial class EmbeddedTeamsWindow
{
    private void OnAccountMenu(object sender, RoutedEventArgs e) => AccountPopup.IsOpen = !AccountPopup.IsOpen;

    private void OnToggleTheme(object sender, RoutedEventArgs e)
    {
        AccountPopup.IsOpen = false;
        var dark = ThemeManager.Toggle();
        ThemeMenuItem.Content = dark ? "☀  Modo claro" : "🌙  Modo oscuro";
        _options.Theme = dark ? "dark" : "light";
        try { UserSettingsService.Save(_options, null); }
        catch (Exception ex) { _logger.LogDebug(ex, "No se pudo guardar el tema."); }
    }

    private void OnToggleBark(object sender, RoutedEventArgs e)
    {
        AccountPopup.IsOpen = false;
        BarkPlayer.Enabled = !BarkPlayer.Enabled;
        BarkMenuItem.Content = BarkPlayer.Enabled ? "🔊  Ladridos: activados" : "🔊  Ladridos: desactivados";
        _options.BarkEnabled = BarkPlayer.Enabled;
        try { UserSettingsService.Save(_options, null); } catch (Exception ex) { _logger.LogDebug(ex, "No se pudo guardar ladridos."); }
        if (BarkPlayer.Enabled) BarkPlayer.Bark(); // muestra cómo suena
    }

    private async void OnRebuildMemory(object sender, RoutedEventArgs e)
    {
        AccountPopup.IsOpen = false;
        if (System.Windows.MessageBox.Show(this,
                "Se reconstruirá la memoria de todos tus chats usando el modelo LOCAL (Ollama), sin gastar créditos de API. Puede tardar un poco.\n\n¿Continuar?",
                "Reconstruir memoria", System.Windows.MessageBoxButton.YesNo,
                System.Windows.MessageBoxImage.Question) != System.Windows.MessageBoxResult.Yes) return;

        Status.Text = "Reconstruyendo memoria en local (gratis)…";
        var progress = new Progress<string>(t => Status.Text = t);
        try
        {
            var n = await _backfill.BackfillAsync(overwrite: true, progress);
            Status.Text = n > 0 ? $"Memoria reconstruida para {n} chats (local, gratis)."
                                : "No se reconstruyó memoria (¿Ollama disponible? ¿hay chats indexados?).";
        }
        catch (Exception ex) { Status.Text = "Error reconstruyendo memoria: " + ex.Message; }
    }

    private async void OnSignOutTeams(object sender, RoutedEventArgs e)
    {
        AccountPopup.IsOpen = false;
        if (Web.CoreWebView2 is null) return;
        if (System.Windows.MessageBox.Show(this,
                "Se cerrará tu sesión de Teams en la app (se borran cookies y datos de sesión). Útil para cambiar de cuenta/empresa.\n\n¿Cerrar sesión de Teams?",
                "Cerrar sesión de Teams", System.Windows.MessageBoxButton.YesNo,
                System.Windows.MessageBoxImage.Warning) != System.Windows.MessageBoxResult.Yes) return;
        try
        {
            await Web.CoreWebView2.Profile.ClearBrowsingDataAsync(
                CoreWebView2BrowsingDataKinds.AllSite
                | CoreWebView2BrowsingDataKinds.DiskCache);
            Web.CoreWebView2.Navigate("https://teams.microsoft.com/");
            Status.Text = "Sesión de Teams cerrada. Inicia sesión con la cuenta que quieras.";
        }
        catch (Exception ex) { Status.Text = "No se pudo cerrar sesión de Teams: " + ex.Message; }
    }

    private void OnSignOutAzure(object sender, RoutedEventArgs e)
    {
        AccountPopup.IsOpen = false;
        if (System.Windows.MessageBox.Show(this,
                "Se cerrará tu sesión del Azure CLI (az logout), que la app usa para Fabric/Azure.\n\n¿Continuar?",
                "Cerrar sesión de Azure/Fabric", System.Windows.MessageBoxButton.YesNo,
                System.Windows.MessageBoxImage.Warning) != System.Windows.MessageBoxResult.Yes) return;
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("cmd.exe", "/c az logout")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
            });
            Status.Text = "Cerrando sesión de Azure/Fabric (az logout)…";
        }
        catch (Exception ex) { Status.Text = "No se pudo ejecutar 'az logout': " + ex.Message; }
    }

    private async void OnMemory(object sender, RoutedEventArgs e)
    {
        AccountPopup.IsOpen = false;
        await RefreshCurrentChatAsync();
        if (string.IsNullOrWhiteSpace(_currentChatId))
        {
            System.Windows.MessageBox.Show(this, "Abre un chat en Teams primero.", "Memoria",
                System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
            return;
        }
        new MemoryWindow(_store, _graph, _local, _currentChatId, _currentChatName) { Owner = this }.ShowDialog();
    }

    private async void OnHistory(object sender, RoutedEventArgs e)
    {
        AccountPopup.IsOpen = false;
        await RefreshCurrentChatAsync();
        if (string.IsNullOrWhiteSpace(_currentChatId)) return;
        new HistoryWindow(_store, _currentChatId, _currentChatName) { Owner = this }.ShowDialog();
    }

    private async void OnDiagnostics(object sender, RoutedEventArgs e)
    {
        AccountPopup.IsOpen = false;
        var window = new DiagnosticsWindow(_diagnostics) { Owner = this };
        window.Show();
        await window.RunAsync();
    }

    private void OnCosts(object sender, RoutedEventArgs e)
    {
        AccountPopup.IsOpen = false;
        MascotSay("Abriendo tus costes de API…");
        new CostWindow(_store, _optionsAccessor) { Owner = this }.Show();
    }

    private void OnSearch(object sender, RoutedEventArgs e) =>
        new GraphSearchWindow(_graph, _knowledge) { Owner = this }.Show();

    private void OnNotes(object sender, RoutedEventArgs e) =>
        new NotesWindow(_notes) { Owner = this }.Show();

    private void OnAttention(object sender, RoutedEventArgs e) =>
        new AttentionWindow(_store, _notes, _attention, _local) { Owner = this }.Show();

    private void OnSettings(object sender, RoutedEventArgs e)
    {
        AccountPopup.IsOpen = false;
        new SettingsWindow(_optionsAccessor) { Owner = this }.ShowDialog();
    }
}
