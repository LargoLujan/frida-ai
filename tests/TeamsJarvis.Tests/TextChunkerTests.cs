using TeamsJarvis.Core;

namespace TeamsJarvis.Tests;

public class TextChunkerTests
{
    [Fact]
    public void EmptyTextProducesNoChunks()
    {
        Assert.Empty(TextChunker.Chunk(""));
        Assert.Empty(TextChunker.Chunk("   \n\n  "));
    }

    [Fact]
    public void ShortTextIsOneChunk()
    {
        var chunks = TextChunker.Chunk("Una nota breve sobre Fabric.");
        Assert.Single(chunks);
        Assert.Equal(0, chunks[0].Index);
        Assert.Contains("Fabric", chunks[0].Text);
    }

    [Fact]
    public void LongTextIsSplitIntoSequentialChunks()
    {
        var paragraph = string.Join("\n\n", Enumerable.Range(0, 40).Select(i => $"Párrafo número {i} con contenido de relleno suficiente."));
        var chunks = TextChunker.Chunk(paragraph, targetChars: 200, overlapChars: 40);

        Assert.True(chunks.Count > 1);
        // Índices consecutivos empezando en 0.
        for (var i = 0; i < chunks.Count; i++) Assert.Equal(i, chunks[i].Index);
        // Ningún fragmento se dispara muy por encima del objetivo.
        Assert.All(chunks, c => Assert.True(c.Text.Length <= 200 * 2));
    }

    [Fact]
    public void HugeParagraphIsHardSplit()
    {
        var giant = new string('a', 5000);
        var chunks = TextChunker.Chunk(giant, targetChars: 800);
        Assert.True(chunks.Count >= 5);
    }

    [Fact]
    public void CosineIdenticalIsOne()
    {
        var v = new[] { 1f, 2f, 3f, 4f };
        Assert.Equal(1.0, TextChunker.Cosine(v, v), precision: 6);
    }

    [Fact]
    public void CosineOrthogonalIsZero()
    {
        Assert.Equal(0.0, TextChunker.Cosine([1f, 0f], [0f, 1f]), precision: 6);
    }

    [Fact]
    public void CosineRanksSimilarHigher()
    {
        var query = new[] { 1f, 1f, 0f };
        var similar = new[] { 1f, 0.9f, 0.1f };
        var different = new[] { 0f, 0.1f, 1f };
        Assert.True(TextChunker.Cosine(query, similar) > TextChunker.Cosine(query, different));
    }

    [Fact]
    public void CosineHandlesMismatchedOrEmpty()
    {
        Assert.Equal(0.0, TextChunker.Cosine([1f, 2f], [1f, 2f, 3f]));
        Assert.Equal(0.0, TextChunker.Cosine([], [1f]));
        Assert.Equal(0.0, TextChunker.Cosine([0f, 0f], [1f, 1f])); // vector nulo
    }

    [Fact]
    public void VectorByteRoundTripPreservesValues()
    {
        var v = new[] { 0.1f, -2.5f, 3.14159f, 0f, 100f };
        var back = TextChunker.FromBytes(TextChunker.ToBytes(v));
        Assert.Equal(v, back);
    }

    [Fact]
    public void HashIsStableAndContentSensitive()
    {
        Assert.Equal(TextChunker.HashContent("hola"), TextChunker.HashContent("hola"));
        Assert.NotEqual(TextChunker.HashContent("hola"), TextChunker.HashContent("hola "));
    }
}
