using System.Windows;

namespace TeamsJarvis.UI;

public partial class DiagnosticsWindow : Window
{
    private readonly DiagnosticsRunner _runner;

    public DiagnosticsWindow(DiagnosticsRunner runner)
    {
        InitializeComponent();
        _runner = runner;
    }

    public async Task RunAsync()
    {
        Results.Text = "";
        try
        {
            await foreach (var line in _runner.RunAsync())
                Results.Text += line + Environment.NewLine;
            Header.Text = "Diagnóstico completado";
        }
        catch (Exception ex)
        {
            Results.Text += $"❌ Error inesperado: {ex.Message}";
            Header.Text = "Diagnóstico con errores";
        }
    }
}
