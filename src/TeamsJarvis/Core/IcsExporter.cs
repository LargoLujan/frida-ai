using System.Globalization;
using System.Text;

namespace TeamsJarvis.Core;

/// <summary>
/// Genera un archivo .ics (iCalendar) para un recordatorio. El usuario lo abre y su
/// calendario (Teams/Outlook) lo importa: sincroniza con el calendario SIN tocar el tenant
/// ni pedir permisos de Graph (no intrusivo, por seguridad).
/// </summary>
public static class IcsExporter
{
    /// <summary>Devuelve la ruta del .ics escrito en la carpeta temporal, listo para abrir.</summary>
    public static string WriteToTemp(Reminder reminder, int durationMinutes = 30)
    {
        var path = Path.Combine(Path.GetTempPath(), $"jarvis-recordatorio-{reminder.Id}.ics");
        File.WriteAllText(path, Build(reminder, durationMinutes), new UTF8Encoding(false));
        return path;
    }

    public static string Build(Reminder reminder, int durationMinutes = 30)
    {
        var startUtc = reminder.DueAt.ToUniversalTime();
        var endUtc = startUtc.AddMinutes(durationMinutes);
        var summary = Escape(reminder.Text);
        var project = string.IsNullOrWhiteSpace(reminder.Project) ? "Frida.ai" : Escape(reminder.Project);

        var sb = new StringBuilder();
        sb.Append("BEGIN:VCALENDAR\r\n");
        sb.Append("VERSION:2.0\r\n");
        sb.Append("PRODID:-//Frida.ai//Recordatorios//ES\r\n");
        sb.Append("CALSCALE:GREGORIAN\r\n");
        sb.Append("METHOD:PUBLISH\r\n");
        sb.Append("BEGIN:VEVENT\r\n");
        sb.Append($"UID:jarvis-reminder-{reminder.Id}@teamsjarvis\r\n");
        sb.Append($"DTSTAMP:{Stamp(DateTimeOffset.UtcNow)}\r\n");
        sb.Append($"DTSTART:{Stamp(startUtc)}\r\n");
        sb.Append($"DTEND:{Stamp(endUtc)}\r\n");
        sb.Append($"SUMMARY:{summary}\r\n");
        sb.Append($"CATEGORIES:{project}\r\n");
        // Aviso 10 minutos antes dentro del propio calendario.
        sb.Append("BEGIN:VALARM\r\nTRIGGER:-PT10M\r\nACTION:DISPLAY\r\n");
        sb.Append($"DESCRIPTION:{summary}\r\nEND:VALARM\r\n");
        sb.Append("END:VEVENT\r\n");
        sb.Append("END:VCALENDAR\r\n");
        return sb.ToString();
    }

    private static string Stamp(DateTimeOffset utc) =>
        utc.UtcDateTime.ToString("yyyyMMdd'T'HHmmss'Z'", CultureInfo.InvariantCulture);

    private static string Escape(string text) => text
        .Replace("\\", "\\\\").Replace(";", "\\;").Replace(",", "\\,")
        .Replace("\r\n", "\\n").Replace("\n", "\\n");
}
