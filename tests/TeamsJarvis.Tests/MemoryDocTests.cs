using TeamsJarvis.Core;

namespace TeamsJarvis.Tests;

public class MemoryDocTests
{
    [Fact]
    public void EmptyMemoryHasNoSections()
    {
        Assert.Empty(MemoryDoc.Parse(""));
        Assert.Empty(MemoryDoc.Parse("   "));
    }

    [Fact]
    public void ParsesKnownSections()
    {
        var mem = "TEMAS:\nPipeline de ventas\nDECISIONES Y ACUERDOS:\nSe pospone la carga\nPENDIENTES Y TAREAS:\nPreparar slides";
        var sections = MemoryDoc.Parse(mem);
        Assert.Equal(3, sections.Count);
        Assert.Equal("Temas", sections[0].Title);
        Assert.Contains("Pipeline de ventas", sections[0].Content);
        Assert.Equal("Decisiones y acuerdos", sections[1].Title);
        Assert.Equal("Pendientes y tareas", sections[2].Title);
        Assert.Contains("slides", sections[2].Content);
    }

    [Fact]
    public void HeaderWithParentheticalIsRecognized()
    {
        var sections = MemoryDoc.Parse("PERSONAS (quién es quién):\nAna: jefa de proyecto");
        Assert.Single(sections);
        Assert.Equal("Personas", sections[0].Title);
        Assert.Contains("Ana", sections[0].Content);
    }

    [Fact]
    public void InlineContentAfterHeaderIsKept()
    {
        var sections = MemoryDoc.Parse("TEMAS: revisión del informe mensual");
        Assert.Single(sections);
        Assert.Equal("revisión del informe mensual", sections[0].Content);
    }

    [Fact]
    public void UnstructuredMemoryBecomesSummarySection()
    {
        var sections = MemoryDoc.Parse("Hablamos de varias cosas sueltas sin formato.");
        Assert.Single(sections);
        Assert.Equal("Resumen", sections[0].Title);
    }

    [Fact]
    public void RoundTripPreservesContent()
    {
        var mem = "TEMAS:\nAlgo\n\nPENDIENTES Y TAREAS:\nOtra cosa";
        var text = MemoryDoc.ToText(MemoryDoc.Parse(mem));
        var reparsed = MemoryDoc.Parse(text);
        Assert.Equal(2, reparsed.Count);
        Assert.Equal("Algo", reparsed[0].Content);
        Assert.Equal("Otra cosa", reparsed[1].Content);
    }

    [Fact]
    public void ToTextSkipsEmptySections()
    {
        var sections = new List<MemorySection>
        {
            new() { Title = "Temas", Content = "algo" },
            new() { Title = "Personas", Content = "" },
        };
        var text = MemoryDoc.ToText(sections);
        Assert.Contains("TEMAS:", text);
        Assert.DoesNotContain("PERSONAS", text);
    }
}
