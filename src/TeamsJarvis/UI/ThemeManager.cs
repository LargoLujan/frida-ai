using System.Windows.Media;

namespace TeamsJarvis.UI;

/// <summary>
/// Gestiona el tema claro/oscuro cambiando EN CALIENTE el color de las brochas "Theme*"
/// definidas en App.xaml. Como los controles las referencian con DynamicResource, el cambio
/// se aplica al instante sin recrear ventanas.
/// </summary>
public static class ThemeManager
{
    public static bool IsDark { get; private set; }

    public static void Apply(string? theme)
    {
        Apply(string.Equals(theme, "dark", System.StringComparison.OrdinalIgnoreCase));
    }

    public static void Apply(bool dark)
    {
        IsDark = dark;
        if (dark)
        {
            // Paleta oscura estilo Microsoft Teams (Fluent dark): neutros cálidos, casi negro.
            Set("ThemeTopBar", "#2B2A2A");     // barra oscura como Teams (no morada) en oscuro
            Set("ThemeBg", "#1F1F1F");
            Set("ThemeSurfaceAlt", "#242322");  // panel (como el fondo de chat de Teams)
            Set("ThemeSurface", "#2D2C2C");     // tarjetas
            Set("ThemeHeader", "#2D2C2C");
            Set("ThemeText", "#F3F2F1");        // texto principal claro
            Set("ThemeMuted", "#B7B5B3");       // texto secundario
            Set("ThemeBorder", "#3D3B39");
            Set("ThemeInput", "#333231");
            Set("ThemeChip", "#3B3A39");
        }
        else
        {
            Set("ThemeTopBar", "#5B5D9E");     // morado de marca en claro
            Set("ThemeBg", "#F4F5FA");
            Set("ThemeSurface", "#FFFFFF");
            Set("ThemeSurfaceAlt", "#F7F8FC");
            Set("ThemeHeader", "#FFFFFF");
            Set("ThemeText", "#22232A");
            Set("ThemeMuted", "#6A6A75");
            Set("ThemeBorder", "#E6E6F0");
            Set("ThemeInput", "#FBFBFE");
            Set("ThemeChip", "#EEF0FA");
        }
    }

    public static bool Toggle()
    {
        Apply(!IsDark);
        return IsDark;
    }

    private static void Set(string key, string hex)
    {
        // Se REEMPLAZA el recurso (no se muta): las brochas de los recursos pueden estar congeladas
        // (read-only) y mutar su Color lanzaría InvalidOperationException. DynamicResource recoge el
        // nuevo pincel al instante, así el cambio de tema sigue siendo en caliente.
        var app = System.Windows.Application.Current;
        if (app is null) return;
        app.Resources[key] = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
    }
}
