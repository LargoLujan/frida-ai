using TeamsJarvis.Core;
using TeamsJarvis.Tools;

namespace TeamsJarvis.UI;

/// <summary>Comprobaciones de salud: Graph, Anthropic, az CLI y servidores MCP.</summary>
public sealed class DiagnosticsRunner(
    GraphAuthService auth,
    AgentSelector agents,
    McpToolBridge mcpBridge,
    SuggestionStore store,
    EmbeddingService embeddings,
    KnowledgeStore knowledge,
    Microsoft.Extensions.Options.IOptions<JarvisOptions> options)
{
    public async IAsyncEnumerable<string> RunAsync(
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
    {
        // 1. Fuente de mensajes
        if (options.Value.SourceMode.Equals("browser", StringComparison.OrdinalIgnoreCase))
        {
            yield return "ℹ Fuente = Teams incrustado (WebView2). Los mensajes se leen del propio Teams " +
                         "dentro de la app. No se usa Graph ni el tenant, y no se abre ningún puerto.";
        }
        else
        {
            string graphResult;
            try
            {
                await auth.GetGraphClientAsync(ct);
                var mode = auth.IsAzCliMode ? "vía az login" : "vía registro de app";
                graphResult = $"✅ Microsoft Graph ({mode}): sesión de {auth.MyDisplayName}";
            }
            catch (Exception ex)
            {
                var hint = auth.IsAzCliMode ? " (¿has hecho 'az login'? ¿tu tenant da permisos de Teams al Azure CLI?)" : "";
                graphResult = $"❌ Microsoft Graph{hint}: {ex.Message}";
            }
            yield return graphResult;
        }

        // 2. Proveedor de IA
        string aiResult;
        var provider = options.Value.Provider;
        try
        {
            var reply = await agents.For(provider).SelfTestAsync(ct);
            aiResult = $"✅ IA ({provider}, modelo {options.Value.Model}): respondió \"{reply.Trim()}\"";
        }
        catch (Exception ex) { aiResult = $"❌ IA ({provider}) — revisa la clave/modelo: {ex.Message}"; }
        yield return aiResult;

        // 2b. Triaje (puede ser local/Ollama, gratis)
        var triageProvider = string.IsNullOrWhiteSpace(options.Value.TriageProvider) ? provider : options.Value.TriageProvider;
        if (options.Value.TriageEnabled && !triageProvider.Equals(provider, StringComparison.OrdinalIgnoreCase))
        {
            string tResult;
            try
            {
                await agents.For(triageProvider).SelfTestAsync(ct);
                tResult = $"✅ Triaje ({triageProvider}{(triageProvider.Equals("ollama", StringComparison.OrdinalIgnoreCase) ? ", local gratis" : "")}): OK";
            }
            catch (Exception ex) { tResult = $"❌ Triaje ({triageProvider}): {ex.Message}"; }
            yield return tResult;
        }

        // 2c. Embeddings locales / RAG (Ollama, coste 0)
        if (options.Value.RagEnabled)
        {
            string rag;
            try
            {
                var ok = await embeddings.IsAvailableAsync(ct);
                var chunks = knowledge.ChunkCount("vault");
                rag = ok
                    ? $"✅ RAG local (embeddings '{embeddings.Model}', gratis): {chunks} fragmentos del vault indexados"
                    : $"ℹ RAG: embeddings locales no disponibles — se usará el conocimiento completo. Instala Ollama y ejecuta 'ollama pull {embeddings.Model}'";
            }
            catch (Exception ex) { rag = $"ℹ RAG: {ex.Message}"; }
            yield return rag;
        }

        // 3. az CLI (token para ARM)
        string azResult;
        try
        {
            await auth.GetCliTokenAsync("https://management.azure.com/.default", ct);
            azResult = "✅ az CLI: sesión activa (tokens de Fabric/ARM disponibles)";
        }
        catch (Exception ex) { azResult = $"❌ az CLI (ejecuta 'az login'): {ex.Message}"; }
        yield return azResult;

        // 4. Servidores MCP
        await mcpBridge.InitializeAsync(ct);
        if (mcpBridge.ServerStatus.Count == 0)
        {
            yield return "ℹ MCP: no hay servidores configurados en appsettings.json";
        }
        else
        {
            foreach (var (name, status) in mcpBridge.ServerStatus)
                yield return status.StartsWith("OK") ? $"✅ MCP '{name}': {status}" : $"❌ MCP '{name}': {status}";
        }

        // 5. Estado local
        var monitored = store.GetMonitoredChats(enabledOnly: true).Count;
        yield return $"ℹ Chats monitorizados: {monitored}"
            + (monitored == 0 ? " — usa '⚙ Elegir chats' para empezar" : "");

        // 6. Coste de API acumulado (aparte de la suscripción)
        var month = store.GetUsageSince(new DateTimeOffset(DateTimeOffset.Now.Year, DateTimeOffset.Now.Month, 1, 0, 0, 0, DateTimeOffset.Now.Offset));
        yield return $"ℹ Coste de API este mes (aparte de tu suscripción): ${month.CostUsd:0.00} en {month.Calls} sugerencias — detalle en '💶 Costes'";
    }
}
