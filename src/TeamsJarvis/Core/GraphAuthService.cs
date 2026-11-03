using Azure.Core;
using Azure.Identity;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Graph;

namespace TeamsJarvis.Core;

/// <summary>
/// Autenticación con Microsoft Graph. Dos modos:
///  - "azcli": reutiliza la sesión de 'az login' (AzureCliCredential) para Graph. Sin Client ID.
///  - "app":   registro de aplicación propio (InteractiveBrowserCredential, login en navegador).
/// Los tokens de Fabric/ARM siempre salen de la sesión del az CLI.
/// </summary>
public sealed class GraphAuthService
{
    // Scopes delegados (modo "app"): consentimiento de solo lectura de Teams/SharePoint.
    public static readonly string[] AppScopes =
        ["Chat.Read", "User.Read", "Sites.Read.All", "Files.Read.All"];

    // Modo "azcli": az emite tokens por recurso (.default), no por scope individual.
    private static readonly string[] GraphDefaultScopes = ["https://graph.microsoft.com/.default"];

    private readonly JarvisOptions _options;
    private readonly ILogger<GraphAuthService> _logger;
    private readonly SemaphoreSlim _initGate = new(1, 1);

    private TokenCredential? _graphCredential;
    private string[] _graphScopes = GraphDefaultScopes;
    private GraphServiceClient? _graph;
    private AzureCliCredential? _cliCredential;
    private string? _myUserId;
    private string? _myDisplayName;

    public GraphAuthService(IOptions<JarvisOptions> options, ILogger<GraphAuthService> logger)
    {
        _options = options.Value;
        _logger = logger;
    }

    public string MyUserId => _myUserId ?? throw new InvalidOperationException("No autenticado todavía.");
    public string MyDisplayName => _myDisplayName ?? "";
    public bool IsAzCliMode => !_options.AuthMode.Equals("app", StringComparison.OrdinalIgnoreCase);

    /// <summary>Garantiza sesión iniciada y devuelve el cliente Graph.</summary>
    public async Task<GraphServiceClient> GetGraphClientAsync(CancellationToken ct = default)
    {
        if (_graph is not null) return _graph;

        await _initGate.WaitAsync(ct);
        try
        {
            if (_graph is not null) return _graph;

            if (IsAzCliMode)
                BuildAzCliCredential();
            else
                await BuildInteractiveCredentialAsync(ct);

            _graph = new GraphServiceClient(_graphCredential, _graphScopes);

            var me = await _graph.Me.GetAsync(cancellationToken: ct)
                     ?? throw new InvalidOperationException("Graph /me devolvió null.");
            _myUserId = me.Id;
            _myDisplayName = me.DisplayName ?? "";
            _logger.LogInformation("Sesión Graph iniciada ({Mode}) como {Name}.",
                IsAzCliMode ? "az cli" : "app", _myDisplayName);

            return _graph;
        }
        finally
        {
            _initGate.Release();
        }
    }

    private void BuildAzCliCredential()
    {
        _graphCredential = new AzureCliCredential(new AzureCliCredentialOptions
        {
            TenantId = string.IsNullOrWhiteSpace(_options.TenantId) ? null : _options.TenantId,
        });
        _graphScopes = GraphDefaultScopes;
    }

    private async Task BuildInteractiveCredentialAsync(CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(_options.TenantId) || string.IsNullOrWhiteSpace(_options.ClientId))
            throw new InvalidOperationException(
                "El modo 'app' requiere Tenant ID y Client ID. Configúralos, o usa el modo 'Reutilizar Azure CLI'.");

        var credOptions = new InteractiveBrowserCredentialOptions
        {
            TenantId = _options.TenantId,
            ClientId = _options.ClientId,
            RedirectUri = new Uri("http://localhost"),
            TokenCachePersistenceOptions = new TokenCachePersistenceOptions { Name = "TeamsJarvis" },
        };

        if (File.Exists(AppPaths.AuthRecordPath))
        {
            try
            {
                await using var stream = File.OpenRead(AppPaths.AuthRecordPath);
                credOptions.AuthenticationRecord = await AuthenticationRecord.DeserializeAsync(stream, ct);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "No se pudo leer el AuthenticationRecord; se pedirá login interactivo.");
            }
        }

        var credential = new InteractiveBrowserCredential(credOptions);

        if (credOptions.AuthenticationRecord is null)
        {
            _logger.LogInformation("Modo app: abriendo navegador para iniciar sesión...");
            var record = await credential.AuthenticateAsync(new TokenRequestContext(AppScopes), ct);
            await using var stream = File.Create(AppPaths.AuthRecordPath);
            await record.SerializeAsync(stream, ct);
        }

        _graphCredential = credential;
        _graphScopes = AppScopes;
    }

    /// <summary>Token de acceso Graph (para llamadas HTTP manuales, p. ej. hostedContents).</summary>
    public async Task<string> GetGraphTokenAsync(CancellationToken ct = default)
    {
        await GetGraphClientAsync(ct);
        var token = await _graphCredential!.GetTokenAsync(new TokenRequestContext(_graphScopes), ct);
        return token.Token;
    }

    /// <summary>Token para un recurso arbitrario (Fabric, ARM) reutilizando la sesión del az CLI.</summary>
    public async Task<string> GetCliTokenAsync(string resourceScope, CancellationToken ct = default)
    {
        _cliCredential ??= new AzureCliCredential(new AzureCliCredentialOptions
        {
            TenantId = string.IsNullOrWhiteSpace(_options.TenantId) ? null : _options.TenantId,
        });
        var token = await _cliCredential.GetTokenAsync(new TokenRequestContext([resourceScope]), ct);
        return token.Token;
    }
}
