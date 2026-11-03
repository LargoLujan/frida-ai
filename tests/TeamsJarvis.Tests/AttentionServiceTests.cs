using TeamsJarvis.Core;

namespace TeamsJarvis.Tests;

public class AttentionServiceTests
{
    [Theory]
    [InlineData("Ana: ¿puedes revisar el pipeline?", true)]
    [InlineData("Luis: cuando puedas lo vemos?", true)]
    [InlineData("Marta: te dejo el informe listo", false)]
    public void DetectsQuestions(string trigger, bool expected)
    {
        Assert.Equal(expected, AttentionService.LooksLikeQuestion(trigger));
    }

    [Fact]
    public void CleanTriggerStripsMentionMarker()
    {
        var cleaned = AttentionService.CleanTrigger("🔔 TE MENCIONAN — Ana: revisa esto");
        Assert.Equal("Ana: revisa esto", cleaned);
    }

    [Fact]
    public void CleanTriggerLeavesNormalTriggerIntact()
    {
        var t = "Ana: revisa esto";
        Assert.Equal(t, AttentionService.CleanTrigger(t));
    }
}
