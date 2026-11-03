using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace TeamsJarvis.Core;

/// <summary>
/// Genera embeddings con un modelo LOCAL vía Ollama (coste 0, corre en tu CPU).
/// Se usa para la búsqueda semántica y el RAG: en vez de mandar todo el vault al modelo
/// caro en cada mensaje, se recuperan solo los fragmentos relevantes.
/// Si Ollama no está disponible, degrada con gracia (devuelve null) sin tumbar la app.
/// </summary>
public sealed class EmbeddingService
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromMinutes(2) };
    private readonly JarvisOptions _options;
    private readonly ILogger<EmbeddingService> _logger;
    private bool _warned;

    public EmbeddingService(IOptions<JarvisOptions> options, ILogger<EmbeddingService> logger)
    {
        _options = options.Value;
        _logger = logger;
    }

    public string Model => string.IsNullOrWhiteSpace(_options.EmbeddingModel) ? "nomic-embed-text" : _options.EmbeddingModel;
    private string BaseUrl => _options.OllamaBaseUrl.TrimEnd('/');

    /// <summary>Embedding de un texto. Devuelve null si Ollama no responde (fallback silencioso).</summary>
    // Límite prudente de entrada: nomic-embed-text (y similares) tienen ~2048 tokens de contexto.
    // Recortamos a ~6000 caracteres para no provocar un 500 "input length exceeds context length".
    private const int MaxInputChars = 6000;

    public async Task<float[]?> EmbedAsync(string text, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        if (text.Length > MaxInputChars) text = text[..MaxInputChars];
        try
        {
            var body = new JsonObject
            {
                ["model"] = Model,
                ["prompt"] = text,
            };
            using var req = new HttpRequestMessage(HttpMethod.Post, $"{BaseUrl}/api/embeddings")
            {
                Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json"),
            };
            using var resp = await Http.SendAsync(req, ct);
            var raw = await resp.Content.ReadAsStringAsync(ct);
            if (!resp.IsSuccessStatusCode)
            {
                WarnOnce($"Ollama embeddings {(int)resp.StatusCode}: {ContextBuilder.Truncate(raw, 200)}");
                return null;
            }

            var arr = JsonNode.Parse(raw)?["embedding"]?.AsArray();
            if (arr is null || arr.Count == 0) return null;
            var vector = new float[arr.Count];
            for (var i = 0; i < arr.Count; i++) vector[i] = (float)(arr[i]?.GetValue<double>() ?? 0);
            return vector;
        }
        catch (Exception ex)
        {
            WarnOnce($"No se pudo generar embedding (¿Ollama abierto y '{Model}' descargado? 'ollama pull {Model}'): {ex.Message}");
            return null;
        }
    }

    /// <summary>Comprueba que el modelo de embeddings responde (para el diagnóstico).</summary>
    public async Task<bool> IsAvailableAsync(CancellationToken ct = default) =>
        await EmbedAsync("ping", ct) is not null;

    private void WarnOnce(string message)
    {
        if (_warned) return;
        _warned = true;
        _logger.LogWarning("Embeddings desactivados: {Message}", message);
    }
}
