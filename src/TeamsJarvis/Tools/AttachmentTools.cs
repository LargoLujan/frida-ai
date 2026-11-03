using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using TeamsJarvis.Core;

namespace TeamsJarvis.Tools;

/// <summary>
/// read_attachment: lee el contenido de los adjuntos que el usuario ha descargado de Teams
/// (carpeta local de descargas). Sin nombre, lista los archivos disponibles. Con nombre,
/// extrae el texto (txt/csv/md/json, docx, xlsx). Todo local: no toca el tenant ni la red.
/// </summary>
public sealed class AttachmentReadTool(ILogger<AttachmentReadTool> logger) : IAgentTool
{
    private const int MaxChars = 60_000;

    public string Name => "read_attachment";

    public string Description =>
        "Lee el contenido de un archivo adjunto que el usuario descargó de Teams (está en su carpeta local de descargas). " +
        "Llama primero SIN 'name' para listar los adjuntos disponibles; luego con 'name' para leer uno. " +
        "Soporta texto (txt, csv, md, json, log…), docx y xlsx. Útil cuando la conversación se refiere a un archivo compartido.";

    public IReadOnlyDictionary<string, object> SchemaProperties => new Dictionary<string, object>
    {
        ["name"] = new { type = "string", description = "Nombre del archivo a leer. Omítelo para listar los adjuntos disponibles." },
    };

    public IReadOnlyList<string> RequiredProperties => [];

    public Task<string> ExecuteAsync(IReadOnlyDictionary<string, JsonElement> input, CancellationToken ct)
    {
        var dir = AppPaths.DownloadsDir;
        var name = input.GetString("name");

        if (string.IsNullOrWhiteSpace(name))
            return Task.FromResult(ListAttachments(dir));

        // Resolver el archivo de forma segura: solo dentro de la carpeta de descargas.
        var safeName = Path.GetFileName(name);
        var path = Path.Combine(dir, safeName);
        if (!File.Exists(path))
            return Task.FromResult($"No existe el adjunto '{safeName}'. Llama a read_attachment sin 'name' para ver los disponibles.");

        var ext = Path.GetExtension(path).ToLowerInvariant();
        if (!DocumentExtractor.IsSupported(ext))
            return Task.FromResult($"El formato '{ext}' de '{safeName}' no está soportado (soportados: texto, docx, xlsx).");

        try
        {
            var text = DocumentExtractor.ExtractFromFile(path);
            logger.LogInformation("read_attachment('{Name}') → {Chars} caracteres.", safeName, text.Length);
            if (text.Length > MaxChars)
                text = text[..MaxChars] + $"\n\n[TRUNCADO: {text.Length} caracteres en total]";
            return Task.FromResult($"Contenido de {safeName}:\n\n{text}");
        }
        catch (Exception ex)
        {
            return Task.FromResult($"No se pudo leer '{safeName}': {ex.Message}");
        }
    }

    private static string ListAttachments(string dir)
    {
        var files = new DirectoryInfo(dir).GetFiles()
            .Where(f => DocumentExtractor.IsSupported(f.Extension))
            .OrderByDescending(f => f.LastWriteTimeUtc)
            .Take(30)
            .ToList();

        if (files.Count == 0)
            return "No hay adjuntos descargados legibles. Descarga el archivo en Teams para poder leerlo.";

        var sb = new StringBuilder("Adjuntos disponibles (usa 'name' para leer uno):\n");
        foreach (var f in files)
            sb.AppendLine($"- {f.Name} ({f.Length / 1024} KB, {f.LastWriteTime:dd/MM/yyyy HH:mm})");
        return sb.ToString();
    }
}
