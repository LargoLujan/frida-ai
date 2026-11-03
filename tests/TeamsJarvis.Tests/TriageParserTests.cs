using TeamsJarvis.Core;

namespace TeamsJarvis.Tests;

public class TriageParserTests
{
    [Theory]
    [InlineData("""{"reply": true}""", true)]
    [InlineData("""{"reply": false}""", false)]
    [InlineData("Claro: {\"reply\": true}", true)]
    [InlineData("{\"reply\": false} — no necesita respuesta", false)]
    public void ParsesReplyDecision(string text, bool expected)
    {
        Assert.Equal(expected, VerdictParser.ShouldReplyTriage(text));
    }

    [Theory]
    [InlineData("")]
    [InlineData("respuesta rara sin json")]
    public void DefaultsToReplyWhenUnparseable(string text)
    {
        // Ante la duda, mejor analizar (no perder un mensaje que sí requería respuesta).
        Assert.True(VerdictParser.ShouldReplyTriage(text));
    }
}
