namespace TeamsJarvis.Core;

/// <summary>Configuración bindeada desde la sección "Jarvis" de appsettings.json.</summary>
public class JarvisOptions
{
    /// <summary>Fuente de mensajes: "browser" (Teams incrustado / WebView2) o "graph" (Microsoft Graph API).</summary>
    public string SourceMode { get; set; } = "browser";
    /// <summary>"azcli" (reutiliza la sesión de az login, sin Client ID) o "app" (registro propio).</summary>
    public string AuthMode { get; set; } = "azcli";
    public string TenantId { get; set; } = "";
    public string ClientId { get; set; } = "";
    /// <summary>Proveedor de IA: "claude" (activo), "openai"/"gemini" (roadmap).</summary>
    public string Provider { get; set; } = "claude";
    public string Model { get; set; } = "claude-opus-4-8";
    public string Effort { get; set; } = "high";
    /// <summary>Triaje: un modelo barato decide si merece respuesta antes de gastar el modelo potente + tools.</summary>
    public bool TriageEnabled { get; set; } = true;
    /// <summary>Proveedor del triaje (vacío = mismo que Provider). "ollama" = local gratis.</summary>
    public string TriageProvider { get; set; } = "";
    /// <summary>Modelo barato para el triaje. Vacío = uno por defecto según el proveedor.</summary>
    public string TriageModel { get; set; } = "";
    /// <summary>URL base de Ollama (LLM local). 127.0.0.1 (no "localhost") para evitar que
    /// .NET resuelva a IPv6 (::1) mientras Ollama escucha solo en IPv4 → "conexión denegada".</summary>
    public string OllamaBaseUrl { get; set; } = "http://127.0.0.1:11434";
    /// <summary>Modelo de embeddings local (Ollama) para búsqueda semántica y RAG.</summary>
    public string EmbeddingModel { get; set; } = "nomic-embed-text";
    /// <summary>RAG: recuperar del vault/conversaciones solo lo relevante (menos tokens al modelo caro).</summary>
    public bool RagEnabled { get; set; } = true;
    /// <summary>Nº de fragmentos relevantes a inyectar por consulta.</summary>
    public int RagTopK { get; set; } = 5;
    /// <summary>Indexar también los mensajes de las conversaciones para búsqueda semántica.</summary>
    public bool IndexConversations { get; set; } = true;
    /// <summary>API key del proveedor cifrada con DPAPI (nunca en texto plano). La escribe la UI.</summary>
    public string ApiKeyProtected { get; set; } = "";
    public int PollSeconds { get; set; } = 20;
    public int DebounceSeconds { get; set; } = 10;
    public int MaxContextMessages { get; set; } = 30;
    public int MaxImages { get; set; } = 5;
    public int MaxToolIterations { get; set; } = 15;
    /// <summary>Arrancar TeamsJarvis automáticamente al iniciar Windows.</summary>
    public bool StartWithWindows { get; set; } = false;
    /// <summary>Tema de la interfaz: "light" u "dark".</summary>
    public string Theme { get; set; } = "light";
    /// <summary>Ladridos suaves de la mascota al hablar y en notificaciones.</summary>
    public bool BarkEnabled { get; set; } = false;
    /// <summary>Rondas de scroll para cargar histórico al abrir un chat (0 = solo lo visible).</summary>
    public int HistoryRounds { get; set; } = 3;
    public ProfileOptions Profile { get; set; } = new();
    public string KnowledgeDir { get; set; } = "";
    public CliOptions Cli { get; set; } = new();
    public List<McpServerOptions> McpServers { get; set; } = new();
}

public class ProfileOptions
{
    public string Name { get; set; } = "";
    public string Role { get; set; } = "";
    public string Language { get; set; } = "es";
    public string ExtraInstructions { get; set; } = "";
}

public class CliOptions
{
    public CliToolOptions Az { get; set; } = new() { AllowedVerbs = ["list", "show"] };
    public CliToolOptions Fab { get; set; } = new() { AllowedVerbs = ["ls", "get", "exists", "desc", "api"] };
}

public class CliToolOptions
{
    public bool Enabled { get; set; } = true;
    public List<string> AllowedVerbs { get; set; } = new();
}

public class McpServerOptions
{
    public string Name { get; set; } = "";
    /// <summary>"stdio" o "http". Si se omite, se infiere: hay Url → "http", si no → "stdio".</summary>
    public string Type { get; set; } = "stdio";
    public string Command { get; set; } = "";
    public List<string> Arguments { get; set; } = new();
    public string Url { get; set; } = "";
    /// <summary>Variables de entorno para el proceso del servidor (formato estándar MCP "env").</summary>
    public Dictionary<string, string> Env { get; set; } = new();
}

/// <summary>Chat de Teams que el usuario ha marcado para monitorizar.</summary>
public record MonitoredChat(string ChatId, string DisplayName, bool Enabled);

/// <summary>Mensaje de chat normalizado para construir la transcripción.</summary>
public record ChatMessageDto(
    string Id,
    string AuthorName,
    string AuthorId,
    bool IsMe,
    DateTimeOffset CreatedAt,
    string Text,
    List<ChatImageDto> Images,
    /// <summary>El mensaje menciona/nombra al usuario (para priorizar la atención).</summary>
    bool IsMention = false);

/// <summary>Imagen descargada de un mensaje (hostedContents).</summary>
public record ChatImageDto(string MediaType, string Base64Data);

/// <summary>Contexto completo que se entrega al agente.</summary>
public record ChatContext(
    string ChatId,
    string ChatName,
    List<ChatMessageDto> Messages,
    /// <summary>Resumen breve de la actividad reciente en los demás chats monitorizados.</summary>
    string OtherChatsDigest,
    /// <summary>Fragmentos del vault recuperados por RAG relevantes a los mensajes nuevos.</summary>
    string RetrievedKnowledge = "");

/// <summary>Veredicto del agente sobre un lote de mensajes nuevos.</summary>
public record AgentVerdict(
    bool ShouldReply,
    string SuggestedReply,
    double Confidence,
    string Sources,
    string Reasoning,
    /// <summary>Resumen acumulado de la conversación que se persiste (cifrado) y se reinyecta la próxima vez.</summary>
    string ConversationMemory);

/// <summary>Sugerencia persistida y mostrada en la UI.</summary>
public record Suggestion(
    long Id,
    string ChatId,
    string ChatName,
    DateTimeOffset CreatedAt,
    /// <summary>Nombre del interlocutor cuyo mensaje motivó la sugerencia.</summary>
    string ReplyTo,
    string TriggerSummary,
    string SuggestedReply,
    double Confidence,
    string Sources);

/// <summary>Persona (interlocutor) en el grafo de conversaciones.</summary>
public record GraphPerson(string PersonId, string Name, int MessageCount, int ChatCount);

/// <summary>Vínculo persona↔chat en el grafo (en qué chats participa y cuánto).</summary>
public record GraphChatLink(string ChatId, string ChatName, int MessageCount, DateTimeOffset? LastAt);

/// <summary>Mensaje almacenado (texto descifrado) devuelto por una búsqueda.</summary>
public record StoredMessage(
    string Id, string ChatId, string ChatName, string AuthorName, bool IsMe,
    DateTimeOffset CreatedAt, string Text);

/// <summary>Fragmento recuperado por búsqueda semántica (RAG). Score = similitud de coseno.</summary>
public record KnowledgeHit(string Kind, string SourceRef, string Title, string Text, double Score);

/// <summary>Nota de proyecto (cuerpo cifrado en reposo).</summary>
public record ProjectNote(
    long Id, string Title, string Body, string Project, DateTimeOffset UpdatedAt);

/// <summary>Recordatorio programable con aviso (toast) al llegar la hora.</summary>
public record Reminder(
    long Id, string Text, DateTimeOffset DueAt, string Project, bool Done, bool Notified);

/// <summary>Un asunto que requiere atención (mención, pregunta o recordatorio).</summary>
public record AttentionItem(
    string Kind, string ChatName, string Title, string Detail,
    DateTimeOffset When, long RefId, double Confidence);

/// <summary>Panel "Requiere tu atención hoy": lo pendiente agrupado, sin coste de IA.</summary>
public record AttentionReport(
    List<AttentionItem> Mentions,
    List<AttentionItem> Questions,
    List<AttentionItem> Reminders)
{
    public int Total => Mentions.Count + Questions.Count + Reminders.Count;
}

/// <summary>Resumen agregado de uso de la API para el panel de costes.</summary>
public record UsageSummary(
    int Calls,
    double CostUsd,
    long InputTokens,
    long OutputTokens,
    long CacheReadTokens,
    long CacheWriteTokens);

public static class AppPaths
{
    /// <summary>
    /// Si se establece, sustituye la carpeta de datos por defecto. Pensado para los tests
    /// (DB temporal) sin tocar los datos reales del usuario.
    /// </summary>
    public static string? OverrideDataDir { get; set; }

    public static string DataDir
    {
        get
        {
            var dir = OverrideDataDir ?? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "TeamsJarvis");
            Directory.CreateDirectory(dir);
            return dir;
        }
    }

    public static string DbPath => Path.Combine(DataDir, "jarvis.db");
    public static string AuthRecordPath => Path.Combine(DataDir, "auth_record.json");
    public static string UserSettingsPath => Path.Combine(DataDir, "settings.json");
    public static string LogsDir
    {
        get
        {
            var dir = Path.Combine(DataDir, "logs");
            Directory.CreateDirectory(dir);
            return dir;
        }
    }

    public static string DefaultKnowledgeDir
    {
        get
        {
            var dir = Path.Combine(DataDir, "knowledge");
            Directory.CreateDirectory(dir);
            return dir;
        }
    }

    public static string DownloadsDir
    {
        get
        {
            var dir = Path.Combine(DataDir, "downloads");
            Directory.CreateDirectory(dir);
            return dir;
        }
    }
}
