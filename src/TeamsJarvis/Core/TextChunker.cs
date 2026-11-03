using System.Security.Cryptography;
using System.Text;

namespace TeamsJarvis.Core;

/// <summary>Un fragmento de texto listo para indexar (con su posición dentro del documento).</summary>
public record TextChunk(int Index, string Text);

/// <summary>
/// Utilidades puras (sin dependencias, testables) para RAG: trocear documentos en
/// fragmentos solapados y calcular similitud de coseno entre vectores de embeddings.
/// </summary>
public static class TextChunker
{
    /// <summary>
    /// Trocea un texto en fragmentos de ~<paramref name="targetChars"/> caracteres, respetando
    /// límites de párrafo cuando es posible y con un pequeño solapamiento para no cortar ideas.
    /// </summary>
    public static List<TextChunk> Chunk(string text, int targetChars = 800, int overlapChars = 150)
    {
        var result = new List<TextChunk>();
        if (string.IsNullOrWhiteSpace(text)) return result;

        // Normalizar saltos de línea y dividir por párrafos (líneas en blanco).
        var normalized = text.Replace("\r\n", "\n").Replace('\r', '\n');
        var paragraphs = normalized.Split("\n\n", StringSplitOptions.RemoveEmptyEntries);

        var sb = new StringBuilder();
        var index = 0;
        foreach (var raw in paragraphs)
        {
            var para = raw.Trim();
            if (para.Length == 0) continue;

            // Un párrafo enorme se parte en trozos duros.
            if (para.Length > targetChars * 2)
            {
                FlushChunk(result, sb, ref index);
                foreach (var piece in HardSplit(para, targetChars))
                    result.Add(new TextChunk(index++, piece));
                continue;
            }

            if (sb.Length > 0 && sb.Length + para.Length + 2 > targetChars)
            {
                var chunk = sb.ToString().Trim();
                result.Add(new TextChunk(index++, chunk));
                // Arrancar el siguiente con el solapamiento del final del anterior.
                var tail = Tail(chunk, overlapChars);
                sb.Clear();
                if (tail.Length > 0) sb.Append(tail).Append("\n\n");
            }

            sb.Append(para).Append("\n\n");
        }
        FlushChunk(result, sb, ref index);
        return result;
    }

    private static void FlushChunk(List<TextChunk> result, StringBuilder sb, ref int index)
    {
        var chunk = sb.ToString().Trim();
        if (chunk.Length > 0) result.Add(new TextChunk(index++, chunk));
        sb.Clear();
    }

    private static IEnumerable<string> HardSplit(string text, int size)
    {
        for (var i = 0; i < text.Length; i += size)
            yield return text.Substring(i, Math.Min(size, text.Length - i));
    }

    private static string Tail(string text, int n) =>
        text.Length <= n ? text : text[^n..];

    /// <summary>Similitud de coseno entre dos vectores. 1 = idénticos, 0 = ortogonales.</summary>
    public static double Cosine(float[] a, float[] b)
    {
        if (a.Length == 0 || b.Length == 0 || a.Length != b.Length) return 0.0;
        double dot = 0, na = 0, nb = 0;
        for (var i = 0; i < a.Length; i++)
        {
            dot += a[i] * b[i];
            na += a[i] * a[i];
            nb += b[i] * b[i];
        }
        if (na == 0 || nb == 0) return 0.0;
        return dot / (Math.Sqrt(na) * Math.Sqrt(nb));
    }

    /// <summary>Hash estable del contenido (para saltar reindexado de lo que no cambió).</summary>
    public static string HashContent(string text)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(text));
        return Convert.ToHexString(bytes);
    }

    // ---- Serialización compacta de vectores (float[] ↔ byte[] para SQLite BLOB) ----

    public static byte[] ToBytes(float[] vector)
    {
        var bytes = new byte[vector.Length * sizeof(float)];
        Buffer.BlockCopy(vector, 0, bytes, 0, bytes.Length);
        return bytes;
    }

    public static float[] FromBytes(byte[] bytes)
    {
        var vector = new float[bytes.Length / sizeof(float)];
        Buffer.BlockCopy(bytes, 0, vector, 0, bytes.Length);
        return vector;
    }
}
