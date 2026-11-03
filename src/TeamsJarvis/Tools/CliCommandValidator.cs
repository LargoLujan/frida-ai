using System.Text.RegularExpressions;
using TeamsJarvis.Core;

namespace TeamsJarvis.Tools;

/// <summary>
/// Validación de comandos de CLI (solo lectura). Aislada del tool para poder testearla:
/// allowlist de ejecutables y verbos, rechazo de operadores de shell y subcomandos peligrosos.
/// </summary>
public static partial class CliCommandValidator
{
    // Solo caracteres inocuos: letras, dígitos, espacios y puntuación habitual de argumentos.
    [GeneratedRegex(@"^[\w\s\.\-_/=:@,'""\?\&\%\[\]\{\}\$\p{L}]+$")]
    private static partial Regex SafeCharsRegex();

    // Operadores de shell que permitirían encadenar comandos: rechazo explícito.
    [GeneratedRegex(@"[|;><`]|&&|\|\||\$\(")]
    private static partial Regex ShellOperatorRegex();

    // Subcomandos que expondrían credenciales o cambiarían la sesión: prohibidos siempre.
    private static readonly string[] Blocklist = ["get-access-token", "logout", "login"];

    /// <summary>
    /// Valida el comando. Devuelve null si es válido (y rellena executable/arguments),
    /// o un mensaje de error legible si se rechaza.
    /// </summary>
    public static string? Validate(string command, CliOptions cli, out string executable, out string arguments)
    {
        executable = "";
        arguments = "";

        if (string.IsNullOrWhiteSpace(command))
            return "comando vacío.";

        command = command.Trim();

        if (ShellOperatorRegex().IsMatch(command))
            return "contiene operadores de shell (| & ; > < ` $() ...), que no están permitidos.";

        if (!SafeCharsRegex().IsMatch(command))
            return "contiene caracteres no permitidos.";

        var parts = command.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length < 2) return "comando incompleto.";

        executable = parts[0].ToLowerInvariant();
        arguments = parts[1];
        var lower = arguments.ToLowerInvariant();

        if (Blocklist.Any(b => lower.Contains(b)))
            return "ese subcomando está prohibido por seguridad.";

        var (enabled, verbs) = executable switch
        {
            "az" => (cli.Az.Enabled, cli.Az.AllowedVerbs),
            "fab" => (cli.Fab.Enabled, cli.Fab.AllowedVerbs),
            _ => (false, new List<string>()),
        };

        if (!enabled) return $"el ejecutable '{executable}' no está permitido.";

        // El comando debe contener alguno de los verbos de lectura permitidos como palabra suelta.
        var words = lower.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (!verbs.Any(v => words.Contains(v.ToLowerInvariant())))
            return $"solo se permiten verbos de lectura: {string.Join(", ", verbs)}.";

        return null;
    }
}
