using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace TeamsJarvis.Core;

/// <summary>
/// Núcleo de análisis reutilizable por cualquier fuente de mensajes (Graph o navegador):
/// indexa el grafo, gestiona el cursor de "último procesado", aplica triaje barato y, si
/// procede, lanza el análisis completo y guarda la sugerencia. Registra el coste de cada llamada.
/// </summary>
public sealed class AnalysisPipeline
{
    private readonly AgentSelector _agents;
    private readonly SuggestionStore _store;
    private readonly ConversationGraphStore _graph;
    private readonly KnowledgeIndexer _knowledge;
    private readonly JarvisOptions _options;
    private readonly ILogger<AnalysisPipeline> _logger;

    public AnalysisPipeline(AgentSelector agents, SuggestionStore store, ConversationGraphStore graph,
        KnowledgeIndexer knowledge, IOptions<JarvisOptions> options, ILogger<AnalysisPipeline> logger)
    {
        _agents = agents;
        _store = store;
        _graph = graph;
        _knowledge = knowledge;
        _options = options.Value;
        _logger = logger;
    }

    /// <summary>Se dispara al empezar/terminar un análisis (indicador de la UI).</summary>
    public event Action<string, bool>? AnalysisRunningChanged;

    /// <summary>
    /// Resultado del análisis SIN sugerencia (triaje lo descartó, el agente no ve necesario
    /// responder, no hay mensajes, o hubo un error/créditos). La UI lo muestra en el panel para
    /// que el usuario nunca se quede sin feedback al pulsar el botón.
    /// </summary>
    public event Action<string, string>? OutcomeReported;

    /// <summary>Decisión del triaje: (chatId, ¿merece respuesta?, motivo). La UI muestra el banderín.</summary>
    public event Action<string, bool, string>? TriageDecided;

    /// <summary>
    /// Procesa los mensajes recientes de un chat. <paramref name="contextFactory"/> construye el
    /// contexto completo solo si hay algo nuevo que analizar (evita trabajo caro en vano).
    /// </summary>
    public async Task RunAsync(
        string chatId, string chatName, List<ChatMessageDto> recent,
        Func<Task<ChatContext>> contextFactory, bool applyDebounce, CancellationToken ct, bool force = false,
        IProgress<string>? progress = null)
    {
        if (recent.Count == 0) return;

        try { _graph.IndexMessages(chatId, chatName, recent); }
        catch (Exception ex) { _logger.LogWarning(ex, "No se pudieron indexar mensajes de '{Chat}'.", chatName); }

        List<ChatMessageDto> newMessages;
        List<ChatMessageDto> newFromOthers;

        if (force)
        {
            // Simulación / "Analizar ahora": analiza el último tramo entrante sin depender del cursor.
            var lastMe = recent.FindLastIndex(m => m.IsMe);
            newMessages = lastMe >= 0 && lastMe < recent.Count - 1
                ? recent.Skip(lastMe + 1).ToList()
                : recent.Where(m => !m.IsMe).TakeLast(1).ToList();
            if (newMessages.Count == 0) newMessages = recent.TakeLast(1).ToList();
            newFromOthers = newMessages.Where(m => !m.IsMe).ToList();
            if (newFromOthers.Count == 0)
            {
                _logger.LogInformation("Analizar ahora en '{Chat}': no hay mensajes entrantes que analizar.", chatName);
                OutcomeReported?.Invoke(chatId, "No hay mensajes entrantes nuevos que analizar en este chat.");
                return;
            }
        }
        else
        {
            var lastProcessed = _store.GetLastProcessed(chatId);
            if (lastProcessed is null)
            {
                _store.SetLastProcessed(chatId, recent[^1].CreatedAt);
                return; // primer contacto: fijar punto de partida sin analizar lo antiguo
            }

            newMessages = recent.Where(m => m.CreatedAt > lastProcessed.Value).ToList();
            newFromOthers = newMessages.Where(m => !m.IsMe).ToList();

            if (newFromOthers.Count == 0)
            {
                if (newMessages.Count > 0) _store.SetLastProcessed(chatId, newMessages[^1].CreatedAt);
                return;
            }

            var newestAt = newMessages[^1].CreatedAt;
            if (applyDebounce &&
                DateTimeOffset.UtcNow - newestAt.ToUniversalTime() < TimeSpan.FromSeconds(_options.DebounceSeconds))
                return; // el interlocutor puede seguir escribiendo
        }

        _store.SetLastProcessed(chatId, recent[^1].CreatedAt);

        var mentioned = newFromOthers.Any(m => m.IsMention);
        _logger.LogInformation("Chat '{Chat}': {Count} mensajes nuevos de {Authors}{Men}. Analizando...",
            chatName, newFromOthers.Count, string.Join(", ", newFromOthers.Select(m => m.AuthorName).Distinct()),
            mentioned ? " (TE MENCIONAN)" : "");

        AnalysisRunningChanged?.Invoke(chatId, true);
        try
        {
            var who = string.Join(", ", newFromOthers.Select(m => m.AuthorName).Distinct());
            progress?.Report($"Leo lo que ha escrito {who} en «{chatName}»…");
            var context = await contextFactory();

            // RAG: recuperar del vault solo lo relevante a los mensajes nuevos (embeddings locales, gratis).
            if (_options.RagEnabled)
            {
                progress?.Report("Busco en tu conocimiento algo que encaje (RAG local, gratis)…");
                var query = string.Join("\n", newFromOthers.TakeLast(4).Select(m => m.Text));
                var hits = await _knowledge.RetrieveAsync(query, kind: "vault", ct: ct);
                if (hits.Count > 0)
                    context = context with { RetrievedKnowledge = RenderHits(hits) };
            }

            var triageProvider = string.IsNullOrWhiteSpace(_options.TriageProvider) ? _options.Provider : _options.TriageProvider;
            var triageModel = _options.TriageModel is { Length: > 0 } tm ? tm : DefaultTriageModel(triageProvider);

            // Triaje (barato): se ejecuta también al pulsar el botón; solo se salta si te mencionan.
            if (mentioned)
            {
                TriageDecided?.Invoke(chatId, true, "Te mencionan directamente, así que merece respuesta.");
            }
            else if (_options.TriageEnabled)
            {
                progress?.Report($"El triaje ({FriendlyModel(triageModel)}) decide si de verdad hace falta que respondas…");
                bool worth; string reason;
                try
                {
                    var (w, r, triageUsage) = await _agents.For(triageProvider).TriageAsync(context, newFromOthers, ct);
                    worth = w; reason = r;
                    _store.LogUsage(chatId, triageProvider, triageModel, triageUsage,
                        CostEstimator.EstimateUsd(triageModel, triageUsage));
                }
                catch (Exception ex) { ReportError(chatId, ex); return; }

                var why = string.IsNullOrWhiteSpace(reason) ? (worth ? "Parece que te piden algo." : "No parece pedirte nada.") : reason;
                TriageDecided?.Invoke(chatId, worth, why);

                if (!worth)
                {
                    _logger.LogInformation("Triaje descartó los mensajes de '{Chat}': {Why}", chatName, why);
                    OutcomeReported?.Invoke(chatId,
                        $"🟡 No hace falta que respondas: {why} (Si quieres, contesta a mano en Teams.)");
                    return;
                }
            }

            progress?.Report("Vale, esto sí merece respuesta. Lo analizo a fondo y redacto…");
            var memory = _store.GetChatMemory(chatId);
            AgentVerdict verdict;
            TokenUsage usage;
            try
            {
                (verdict, usage) = await _agents.For(_options.Provider).AnalyzeAsync(context, memory, newMessages, progress, ct);
            }
            catch (Exception ex) { ReportError(chatId, ex); return; }
            _store.LogUsage(chatId, _options.Provider, _options.Model, usage, CostEstimator.EstimateUsd(_options.Model, usage));

            if (!string.IsNullOrWhiteSpace(verdict.ConversationMemory))
                _store.SetChatMemory(chatId, verdict.ConversationMemory);

            if (verdict.ShouldReply && !string.IsNullOrWhiteSpace(verdict.SuggestedReply))
            {
                var replyTo = newFromOthers[^1].AuthorName;
                var trigger = (mentioned ? "🔔 TE MENCIONAN — " : "") + string.Join(" | ", newFromOthers.TakeLast(3)
                    .Select(m => $"{m.AuthorName}: {ContextBuilder.Truncate(m.Text, 120)}"));
                _store.AddSuggestion(chatId, chatName, replyTo, trigger,
                    verdict.SuggestedReply, verdict.Confidence, verdict.Sources);
                _logger.LogInformation("Sugerencia creada para '{Chat}' (confianza {C:P0}).", chatName, verdict.Confidence);
            }
            else
            {
                _logger.LogInformation("Sin sugerencia en '{Chat}': {R}", chatName, verdict.Reasoning);
                var reason = string.IsNullOrWhiteSpace(verdict.Reasoning)
                    ? "El agente no ve necesaria una respuesta tuya." : verdict.Reasoning;
                OutcomeReported?.Invoke(chatId, $"🟡 No hace falta responder: {reason}");
            }
        }
        finally
        {
            AnalysisRunningChanged?.Invoke(chatId, false);
        }
    }

    /// <summary>Reporta un error del proveedor de IA de forma legible (incluye créditos agotados).</summary>
    private void ReportError(string chatId, Exception ex)
    {
        _logger.LogWarning(ex, "Fallo del proveedor de IA durante el análisis de '{Chat}'.", chatId);
        OutcomeReported?.Invoke(chatId, FriendlyError(ex));
    }

    private static string FriendlyError(Exception ex)
    {
        var msg = ((ex.Message ?? "") + " " + (ex.InnerException?.Message ?? "")).ToLowerInvariant();
        if (msg.Contains("credit") || msg.Contains("billing") || msg.Contains("quota")
            || msg.Contains("insufficient") || msg.Contains("payment") || msg.Contains(" 402"))
            return "⛔ CRÉDITOS DE API AGOTADOS (o problema de facturación). No se pueden generar más respuestas " +
                   "hasta recargar saldo en la consola del proveedor (p. ej. Anthropic). El triaje/redacción local (Ollama) sí sigue disponible.";
        if (msg.Contains("rate") || msg.Contains(" 429") || msg.Contains("overloaded") || msg.Contains(" 529"))
            return "⛔ Límite de peticiones de la API alcanzado. Espera unos segundos y vuelve a pulsar el botón.";
        if (msg.Contains("401") || msg.Contains("authentication") || msg.Contains("api key") || msg.Contains("x-api-key"))
            return "⛔ La clave de API no es válida o falta. Revísala en Configuración → IA & coste.";
        return "⛔ Error del proveedor de IA: " + ContextBuilder.Truncate(ex.Message ?? "desconocido", 200);
    }

    private static string RenderHits(List<KnowledgeHit> hits)
    {
        var sb = new System.Text.StringBuilder();
        foreach (var h in hits)
            sb.AppendLine($"--- {h.Title} (relevancia {h.Score:P0}) ---").AppendLine(h.Text).AppendLine();
        return sb.ToString();
    }

    /// <summary>Nombre legible de un modelo para los mensajes de la mascota.</summary>
    private static string FriendlyModel(string model) => model switch
    {
        "claude-haiku-4-5" => "Claude Haiku",
        "claude-sonnet-5" => "Claude Sonnet",
        "claude-opus-4-8" => "Claude Opus",
        "llama3.2:3b" => "Llama local",
        "gpt-4o-mini" => "GPT-4o mini",
        "gemini-2.0-flash" => "Gemini Flash",
        "sim-triage" => "simulación",
        _ => model,
    };

    private static string DefaultTriageModel(string provider) => provider.ToLowerInvariant() switch
    {
        "openai" => "gpt-4o-mini",
        "gemini" => "gemini-2.0-flash",
        "ollama" => "llama3.2:3b",
        "simulation" => "sim-triage",
        _ => "claude-haiku-4-5",
    };
}
