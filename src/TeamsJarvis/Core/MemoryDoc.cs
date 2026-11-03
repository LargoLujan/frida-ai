using System.Text;

namespace TeamsJarvis.Core;

/// <summary>Una sección de la ficha de memoria (título + contenido).</summary>
public sealed class MemorySection
{
    public string Title { get; set; } = "";
    public string Content { get; set; } = "";
}

/// <summary>
/// Parseo y serialización de la FICHA DE MEMORIA en secciones (TEMAS, DECISIONES Y ACUERDOS,
/// PENDIENTES Y TAREAS, PERSONAS, FECHAS E HITOS). Tolerante: si el texto no trae cabeceras
/// reconocibles, lo deja como una sola sección "Resumen". Puro y testeable.
/// </summary>
public static class MemoryDoc
{
    /// <summary>Cabeceras canónicas, en el orden en que se muestran.</summary>
    public static readonly string[] KnownSections =
    [
        "TEMAS",
        "DECISIONES Y ACUERDOS",
        "PENDIENTES Y TAREAS",
        "PERSONAS",
        "FECHAS E HITOS",
    ];

    public static List<MemorySection> Parse(string? memory)
    {
        var result = new List<MemorySection>();
        if (string.IsNullOrWhiteSpace(memory)) return result;

        var lines = memory.Replace("\r\n", "\n").Split('\n');
        MemorySection? current = null;
        foreach (var raw in lines)
        {
            var header = MatchHeader(raw);
            if (header is not null)
            {
                current = new MemorySection { Title = header };
                result.Add(current);
                // Contenido en la misma línea tras la cabecera (p. ej. "TEMAS: algo").
                var after = AfterHeader(raw);
                if (!string.IsNullOrWhiteSpace(after)) current.Content = after.Trim();
                continue;
            }
            if (current is null)
            {
                current = new MemorySection { Title = "Resumen" };
                result.Add(current);
            }
            current.Content = string.IsNullOrEmpty(current.Content) ? raw.TrimEnd() : current.Content + "\n" + raw.TrimEnd();
        }

        foreach (var s in result) s.Content = s.Content.Trim();
        return result;
    }

    public static string ToText(IEnumerable<MemorySection> sections)
    {
        var sb = new StringBuilder();
        foreach (var s in sections)
        {
            if (string.IsNullOrWhiteSpace(s.Content)) continue;
            sb.Append(s.Title.ToUpperInvariant()).Append(":\n").Append(s.Content.Trim()).Append("\n\n");
        }
        return sb.ToString().Trim();
    }

    /// <summary>Devuelve la cabecera canónica si la línea es una cabecera de sección conocida.</summary>
    private static string? MatchHeader(string line)
    {
        var t = line.Trim();
        if (t.Length == 0) return null;
        // La cabecera es lo que hay ANTES del primer ':' (permite "TEMAS: contenido en la misma línea").
        var colon = t.IndexOf(':');
        var before = colon >= 0 ? t[..colon] : t;
        // Quita paréntesis explicativos: "PERSONAS (quién es quién)".
        var paren = before.IndexOf('(');
        if (paren > 0) before = before[..paren];
        var norm = before.Trim().ToUpperInvariant();
        foreach (var known in KnownSections)
            if (norm == known) return TitleCase(known);
        return null;
    }

    private static string AfterHeader(string line)
    {
        var idx = line.IndexOf(':');
        return idx >= 0 && idx < line.Length - 1 ? line[(idx + 1)..] : "";
    }

    private static string TitleCase(string upper) => upper switch
    {
        "TEMAS" => "Temas",
        "DECISIONES Y ACUERDOS" => "Decisiones y acuerdos",
        "PENDIENTES Y TAREAS" => "Pendientes y tareas",
        "PERSONAS" => "Personas",
        "FECHAS E HITOS" => "Fechas e hitos",
        _ => upper,
    };
}
