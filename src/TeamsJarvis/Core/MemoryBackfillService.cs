using System.Text;
using Microsoft.Extensions.Logging;

namespace TeamsJarvis.Core;

/// <summary>
/// Reconstruye la FICHA DE MEMORIA de cada chat historificado usando el modelo LOCAL (Ollama),
/// SIN gastar créditos de la API. A partir de ahí, la memoria se actualiza sola cuando el usuario
/// pide sugerencias. Pensado para ejecutarse en segundo plano al arrancar y bajo demanda.
/// </summary>
public sealed class MemoryBackfillService
{
    private readonly ConversationGraphStore _graph;
    private readonly SuggestionStore _store;
    private readonly LocalLlm _local;
    private readonly ILogger<MemoryBackfillService> _logger;
    private int _running;

    public MemoryBackfillService(ConversationGraphStore graph, SuggestionStore store,
        LocalLlm local, ILogger<MemoryBackfillService> logger)
    {
        _graph = graph;
        _store = store;
        _local = local;
        _logger = logger;
    }

    /// <summary>
    /// Rellena la memoria de los chats. Con <paramref name="overwrite"/>=false solo rellena los que
    /// aún no tienen memoria. Devuelve cuántas fichas se generaron. No se ejecuta dos veces a la vez.
    /// </summary>
    public async Task<int> BackfillAsync(bool overwrite = false, IProgress<string>? progress = null, CancellationToken ct = default)
    {
        if (Interlocked.Exchange(ref _running, 1) == 1) return 0; // ya hay un backfill en curso
        try
        {
            var chats = _graph.GetChats();
            var done = 0;
            foreach (var (chatId, chatName, count) in chats)
            {
                ct.ThrowIfCancellationRequested();
                if (count < 3) continue; // demasiado corto para una ficha útil
                if (!overwrite && !string.IsNullOrWhiteSpace(_store.GetChatMemory(chatId))) continue;

                progress?.Report($"Reconstruyendo memoria: {chatName}…");
                var transcript = BuildTranscript(chatId);
                if (transcript.Length < 40) continue;

                var summary = await _local.SummarizeConversationMemoryAsync(chatName, transcript, ct);
                if (summary is null)
                {
                    _logger.LogInformation("Backfill de memoria detenido: modelo local no disponible.");
                    return done; // Ollama no está: parar sin insistir
                }
                _store.SetChatMemory(chatId, summary.Trim());
                done++;
            }
            if (done > 0) _logger.LogInformation("Memoria reconstruida en local para {N} chats.", done);
            return done;
        }
        finally
        {
            Interlocked.Exchange(ref _running, 0);
        }
    }

    private string BuildTranscript(string chatId)
    {
        var sb = new StringBuilder();
        foreach (var m in _graph.GetRecentForChat(chatId, 60))
            sb.AppendLine($"[{m.CreatedAt.ToLocalTime():dd/MM HH:mm}] {(m.IsMe ? "Yo" : m.AuthorName)}: {ContextBuilder.Truncate(m.Text, 300)}");
        return sb.ToString();
    }
}
