using System.IO;
using TeamsJarvis.Core;

namespace TeamsJarvis.Tests;

/// <summary>
/// Tests de integración de los almacenes SQLite sobre una DB TEMPORAL (AppPaths.OverrideDataDir),
/// para no tocar los datos reales. Verifican el cifrado en reposo y las consultas principales.
/// xUnit crea una instancia por test, así que cada test tiene su propia carpeta/DB.
/// </summary>
public sealed class StoreIntegrationTests : IDisposable
{
    private readonly string _dir;

    public StoreIntegrationTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "frida-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
        AppPaths.OverrideDataDir = _dir;
    }

    public void Dispose()
    {
        AppPaths.OverrideDataDir = null;
        try { Directory.Delete(_dir, recursive: true); } catch { }
    }

    [Fact]
    public void SuggestionStore_RoundTripsEncryptedContent()
    {
        var store = new SuggestionStore();
        var s = store.AddSuggestion("chat1", "Grupo Fabric", "Ana", "Ana: ¿estado del pipeline?",
            "Te confirmo en un momento.", 0.82, "fabric_api");

        var byId = store.GetSuggestionById(s.Id);
        Assert.NotNull(byId);
        Assert.Equal("Te confirmo en un momento.", byId!.SuggestedReply); // descifrado correcto
        Assert.Equal("Ana", byId.ReplyTo);
        Assert.Equal(0.82, byId.Confidence, 3);

        Assert.Single(store.GetSuggestions(chatId: "chat1"));
        Assert.Single(store.GetSuggestionsSince(DateTimeOffset.Now.AddMinutes(-5)));
        Assert.Empty(store.GetSuggestionsSince(DateTimeOffset.Now.AddMinutes(5)));
    }

    [Fact]
    public void SuggestionStore_UsageAndCostAggregate()
    {
        var store = new SuggestionStore();
        store.LogUsage("c", "claude", "claude-sonnet-5", new TokenUsage(1000, 200, 0, 0), 0.05);
        store.LogUsage("c", "ollama", "llama3.2:3b", new TokenUsage(500, 100, 0, 0), 0.0);

        var month = store.GetUsageSince(DateTimeOffset.Now.AddDays(-1));
        Assert.Equal(2, month.Calls);
        Assert.Equal(0.05, month.CostUsd, 4);

        var byModel = store.GetCostByModelSince(DateTimeOffset.Now.AddDays(-1));
        Assert.Equal(2, byModel.Count);
        Assert.Contains(byModel, m => m.Model == "claude-sonnet-5" && m.CostUsd > 0);
    }

    [Fact]
    public void SuggestionStore_MemoryRoundTrips()
    {
        var store = new SuggestionStore();
        Assert.Equal("", store.GetChatMemory("c"));
        store.SetChatMemory("c", "TEMAS:\nPipeline de ventas");
        Assert.Contains("Pipeline de ventas", store.GetChatMemory("c"));
    }

    [Fact]
    public void NotesStore_NotesAndRemindersLifecycle()
    {
        var store = new NotesStore();
        var id = store.UpsertNote(0, "Nota", "cuerpo secreto", "Fabric");
        Assert.Contains(store.GetNotes(), n => n.Id == id && n.Body == "cuerpo secreto"); // cifrado ok
        store.DeleteNote(id);
        Assert.Empty(store.GetNotes());

        var past = DateTimeOffset.Now.AddMinutes(-1);
        var rid = store.UpsertReminder(0, "Llamar a Ana", past, "Fabric");
        Assert.Single(store.GetDueUnnotified(DateTimeOffset.Now)); // vencido y sin avisar
        store.MarkNotified(rid);
        Assert.Empty(store.GetDueUnnotified(DateTimeOffset.Now)); // ya avisado
        store.SetReminderDone(rid, true);
        Assert.Empty(store.GetReminders(includeDone: false));
    }

    [Fact]
    public void ConversationGraphStore_IndexSearchAndChats()
    {
        var graph = new ConversationGraphStore();
        var msgs = new List<ChatMessageDto>
        {
            new("m1", "Ana", "a", false, DateTimeOffset.Now.AddMinutes(-3), "El pipeline de ventas falló", new()),
            new("m2", "Yo", "me", true, DateTimeOffset.Now.AddMinutes(-2), "Lo reviso ahora", new()),
        };
        graph.IndexMessages("chatX", "Grupo Fabric", msgs);

        Assert.Equal(2, graph.MessageCount());
        Assert.Single(graph.Search("pipeline", null, null)); // descifra y filtra por palabra
        Assert.Contains(graph.GetChats(), c => c.ChatId == "chatX" && c.MessageCount == 2);
        Assert.Equal(2, graph.GetRecentForChat("chatX").Count);
        Assert.Contains(graph.GetPeople(), p => p.Name == "Ana");
    }

    [Fact]
    public void KnowledgeStore_UpsertAndCosineSearch()
    {
        var store = new KnowledgeStore();
        var id1 = KnowledgeStore.MakeId("vault", "notas.md", "0");
        store.Upsert(id1, "vault", "notas.md", "notas", 0, "workspace de ventas", [1f, 0f, 0f], "nomic");
        store.Upsert(KnowledgeStore.MakeId("vault", "otras.md", "0"), "vault", "otras.md", "otras", 0,
            "recetas de cocina", [0f, 1f, 0f], "nomic");

        Assert.True(store.IsIndexed(id1, TextChunker.HashContent("workspace de ventas")));
        Assert.Equal(2, store.ChunkCount("vault"));

        // Consulta parecida al primer vector => primer resultado es "notas".
        var hits = store.Search([0.9f, 0.1f, 0f], topK: 1, kind: "vault");
        Assert.Single(hits);
        Assert.Equal("notas", hits[0].Title);
        Assert.Contains("ventas", hits[0].Text); // descifrado

        store.DeleteSource("vault", "notas.md");
        Assert.Equal(1, store.ChunkCount("vault"));
    }
}
