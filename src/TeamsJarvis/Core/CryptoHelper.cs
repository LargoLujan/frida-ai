using System.Security.Cryptography;
using System.Text;

namespace TeamsJarvis.Core;

/// <summary>
/// Cifrado en reposo con DPAPI de Windows (ámbito usuario actual): el contenido de las
/// conversaciones solo puede descifrarse con la sesión de Windows del propietario.
/// </summary>
public static class CryptoHelper
{
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("TeamsJarvis.v1");
    private const string Prefix = "enc:v1:";

    public static string Protect(string plaintext)
    {
        if (string.IsNullOrEmpty(plaintext)) return plaintext;
        var bytes = ProtectedData.Protect(
            Encoding.UTF8.GetBytes(plaintext), Entropy, DataProtectionScope.CurrentUser);
        return Prefix + Convert.ToBase64String(bytes);
    }

    public static string Unprotect(string stored)
    {
        if (string.IsNullOrEmpty(stored) || !stored.StartsWith(Prefix, StringComparison.Ordinal))
            return stored; // compatibilidad con datos antiguos sin cifrar
        try
        {
            var bytes = ProtectedData.Unprotect(
                Convert.FromBase64String(stored[Prefix.Length..]), Entropy, DataProtectionScope.CurrentUser);
            return Encoding.UTF8.GetString(bytes);
        }
        catch (CryptographicException)
        {
            return "(no se pudo descifrar: los datos pertenecen a otro usuario de Windows)";
        }
    }
}
