using System.Text.Json;
using System.Text.Json.Serialization;

namespace TeamsJarvis.Core;

/// <summary>
/// Persiste los ajustes editados por el usuario en %LOCALAPPDATA%\TeamsJarvis\settings.json,
/// que se superpone a appsettings.json al arrancar. Los secretos (API key) se guardan
/// cifrados con DPAPI, nunca en texto plano.
/// </summary>
public static class UserSettingsService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    /// <summary>Guarda los ajustes (con la API key en claro que se cifrará aquí, o null para conservar la existente).</summary>
    public static void Save(JarvisOptions options, string? plaintextApiKey)
    {
        if (plaintextApiKey is not null)
            options.ApiKeyProtected = string.IsNullOrWhiteSpace(plaintextApiKey)
                ? ""
                : CryptoHelper.Protect(plaintextApiKey);

        var root = new Dictionary<string, object?> { ["Jarvis"] = options };
        File.WriteAllText(AppPaths.UserSettingsPath, JsonSerializer.Serialize(root, JsonOptions));
    }

    /// <summary>Devuelve la API key en claro descifrada, o "" si no hay.</summary>
    public static string GetApiKey(JarvisOptions options) =>
        string.IsNullOrWhiteSpace(options.ApiKeyProtected) ? "" : CryptoHelper.Unprotect(options.ApiKeyProtected);

    /// <summary>
    /// Si hay API key cifrada en los ajustes, la descifra y la expone como variable de
    /// entorno del proceso para que el SDK del proveedor la tome automáticamente.
    /// </summary>
    public static void ApplyApiKeyToEnvironment(JarvisOptions options)
    {
        var key = GetApiKey(options);
        if (string.IsNullOrWhiteSpace(key)) return;

        var envVar = options.Provider.ToLowerInvariant() switch
        {
            "openai" => "OPENAI_API_KEY",
            "gemini" => "GEMINI_API_KEY",
            _ => "ANTHROPIC_API_KEY",
        };
        Environment.SetEnvironmentVariable(envVar, key);
    }

    /// <summary>
    /// True si falta configuración esencial para autenticarse (dispara el asistente de primer arranque).
    /// En modo "azcli" no hacen falta Tenant/Client (se usa la sesión de az login); en modo "app" sí.
    /// </summary>
    public static bool NeedsSetup(JarvisOptions options)
    {
        if (options.AuthMode.Equals("app", StringComparison.OrdinalIgnoreCase))
            return string.IsNullOrWhiteSpace(options.TenantId) || string.IsNullOrWhiteSpace(options.ClientId);
        return false; // modo azcli: no requiere configuración previa de auth
    }
}
