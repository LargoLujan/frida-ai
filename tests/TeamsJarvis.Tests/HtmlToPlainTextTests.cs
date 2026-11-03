using TeamsJarvis.Core;

namespace TeamsJarvis.Tests;

public class HtmlToPlainTextTests
{
    [Fact]
    public void StripsTags()
    {
        Assert.Equal("Hola mundo", TeamsService.HtmlToPlainText("<p>Hola <b>mundo</b></p>"));
    }

    [Fact]
    public void ConvertsBreaksToNewlines()
    {
        var result = TeamsService.HtmlToPlainText("línea1<br>línea2");
        Assert.Contains("línea1", result);
        Assert.Contains("línea2", result);
        Assert.Contains("\n", result);
    }

    [Fact]
    public void DecodesHtmlEntities()
    {
        Assert.Equal("a & b < c", TeamsService.HtmlToPlainText("a &amp; b &lt; c"));
    }

    [Fact]
    public void ReplacesImagesWithPlaceholder()
    {
        var result = TeamsService.HtmlToPlainText("mira esto <img src=\"x\"/> aquí");
        Assert.Contains("[imagen]", result);
        Assert.DoesNotContain("<img", result);
    }

    [Fact]
    public void CollapsesExcessiveNewlines()
    {
        var result = TeamsService.HtmlToPlainText("a</p><br><br><br></div>b");
        Assert.DoesNotContain("\n\n\n", result);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void HandlesEmpty(string input)
    {
        Assert.Equal("", TeamsService.HtmlToPlainText(input));
    }

    [Fact]
    public void HandlesPlainTextWithoutTags()
    {
        Assert.Equal("texto simple", TeamsService.HtmlToPlainText("texto simple"));
    }
}
