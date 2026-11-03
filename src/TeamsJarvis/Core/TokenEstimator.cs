namespace TeamsJarvis.Core;

/// <summary>
/// Estimación aproximada de tokens sin llamar a ninguna API (para simulación de costes).
/// Heurística: ~3,5 caracteres por token en texto español + JSON. Una imagen de visión
/// cuesta del orden de ~1200 tokens a resolución reducida.
/// </summary>
public static class TokenEstimator
{
    private const double CharsPerToken = 3.5;
    public const int TokensPerImage = 1200;

    public static int Text(string? text) =>
        string.IsNullOrEmpty(text) ? 0 : (int)Math.Ceiling(text.Length / CharsPerToken);

    public static int Images(int count) => count * TokensPerImage;
}
