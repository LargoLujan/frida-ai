using TeamsJarvis.Core;

namespace TeamsJarvis.Tests;

public class VerdictParserTests
{
    [Fact]
    public void ParsesCleanJson()
    {
        var json = """
            {"should_reply": true, "suggested_reply": "Hola, lo reviso.",
             "confidence": 0.85, "sources": "fabric_api /workspaces",
             "reasoning": "preguntan por un pipeline", "conversation_memory": "resumen"}
            """;

        var ok = VerdictParser.TryParse(json, out var verdict);

        Assert.True(ok);
        Assert.True(verdict.ShouldReply);
        Assert.Equal("Hola, lo reviso.", verdict.SuggestedReply);
        Assert.Equal(0.85, verdict.Confidence, precision: 3);
        Assert.Equal("fabric_api /workspaces", verdict.Sources);
        Assert.Equal("resumen", verdict.ConversationMemory);
    }

    [Fact]
    public void ExtractsJsonSurroundedByText()
    {
        // El agente a veces envuelve el JSON en explicación pese a las instrucciones.
        var text = "Aquí está mi veredicto:\n{\"should_reply\": false, \"suggested_reply\": \"\", \"confidence\": 0}\nEspero que ayude.";

        var ok = VerdictParser.TryParse(text, out var verdict);

        Assert.True(ok);
        Assert.False(verdict.ShouldReply);
    }

    [Fact]
    public void ShouldReplyDefaultsFalseWhenMissing()
    {
        var ok = VerdictParser.TryParse("""{"suggested_reply": "x"}""", out var verdict);

        Assert.True(ok);
        Assert.False(verdict.ShouldReply); // ausente ⇒ no molestar
    }

    [Fact]
    public void DegradesGracefullyOnNonJson()
    {
        var text = "No he podido determinar una respuesta clara.";

        var ok = VerdictParser.TryParse(text, out var verdict);

        Assert.False(ok);
        Assert.True(verdict.ShouldReply);          // hay texto ⇒ se muestra como sugerencia
        Assert.Equal(text, verdict.SuggestedReply);
        Assert.Equal(0.2, verdict.Confidence, precision: 3); // baja confianza
    }

    [Fact]
    public void DegradesToNoReplyOnEmpty()
    {
        var ok = VerdictParser.TryParse("   ", out var verdict);

        Assert.False(ok);
        Assert.False(verdict.ShouldReply);
    }

    [Fact]
    public void HandlesMissingConfidenceAsZero()
    {
        var ok = VerdictParser.TryParse("""{"should_reply": true, "suggested_reply": "x"}""", out var verdict);

        Assert.True(ok);
        Assert.Equal(0, verdict.Confidence);
    }
}
