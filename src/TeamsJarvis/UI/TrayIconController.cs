using System.Drawing;
using System.Windows.Forms;
using TeamsJarvis.Core;

namespace TeamsJarvis.UI;

/// <summary>
/// Icono en la bandeja del sistema: la app vive aquí aunque se cierre la ventana.
/// </summary>
public sealed class TrayIconController : IDisposable
{
    private readonly NotifyIcon _icon;
    private readonly TeamsMonitorService _monitor;
    private readonly ToolStripMenuItem _pauseItem;

    public TrayIconController(EmbeddedTeamsWindow mainWindow, TeamsMonitorService monitor)
    {
        _monitor = monitor;

        var menu = new ContextMenuStrip();
        menu.Items.Add("Abrir TeamsJarvis", null, (_, _) => mainWindow.ShowAndActivate());
        menu.Items.Add(new ToolStripSeparator());
        _pauseItem = new ToolStripMenuItem("Pausar monitorización", null, (_, _) => TogglePause());
        menu.Items.Add(_pauseItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Salir", null, (_, _) => mainWindow.ExitApplication());

        _icon = new NotifyIcon
        {
            Icon = FridaIcon.CreateIcon(),
            Text = "TeamsJarvis — asistente de Teams",
            Visible = true,
            ContextMenuStrip = menu,
        };
        _icon.DoubleClick += (_, _) => mainWindow.ShowAndActivate();
    }

    private void TogglePause()
    {
        _monitor.Paused = !_monitor.Paused;
        _pauseItem.Checked = _monitor.Paused;
        _icon.Text = _monitor.Paused
            ? "TeamsJarvis — en pausa"
            : "TeamsJarvis — asistente de Teams";
    }

    public void Dispose()
    {
        _icon.Visible = false;
        _icon.Dispose();
    }
}
