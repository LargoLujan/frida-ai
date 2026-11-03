using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TeamsJarvis.Core;

namespace TeamsJarvis.Tools;

/// <summary>
/// run_cli: ejecuta comandos de solo lectura de az / fab. La validación (allowlist,
/// operadores de shell, subcomandos peligrosos) vive en CliCommandValidator.
/// </summary>
public sealed class RunCliTool(IOptions<JarvisOptions> options, ILogger<RunCliTool> logger) : IAgentTool
{
    private const int MaxChars = 40_000;
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(90);

    public string Name => "run_cli";

    public string Description =>
        "Ejecuta un comando de SOLO LECTURA del CLI de Azure ('az ...') o de Microsoft Fabric ('fab ...'). " +
        "Ejemplos: 'az resource list --resource-group X -o json', 'az monitor activity-log list ...', " +
        "'fab ls', 'fab get /workspace/item'. Solo se permiten verbos de consulta (list/show/ls/get...); " +
        "cualquier comando que modifique algo será rechazado.";

    public IReadOnlyDictionary<string, object> SchemaProperties => new Dictionary<string, object>
    {
        ["command"] = new
        {
            type = "string",
            description = "Comando completo empezando por 'az' o 'fab', sin operadores de shell (| & ; > <).",
        },
    };

    public IReadOnlyList<string> RequiredProperties => ["command"];

    public async Task<string> ExecuteAsync(IReadOnlyDictionary<string, JsonElement> input, CancellationToken ct)
    {
        var command = input.GetString("command")?.Trim();
        if (string.IsNullOrWhiteSpace(command)) return "Error: falta 'command'.";

        var error = CliCommandValidator.Validate(command, options.Value.Cli, out var executable, out var arguments);
        if (error is not null) return $"Comando rechazado: {error}";

        logger.LogInformation("run_cli: {Command}", command);

        var psi = new ProcessStartInfo
        {
            FileName = "cmd.exe",
            Arguments = $"/d /c {executable} {arguments}",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };

        using var process = Process.Start(psi);
        if (process is null) return "No se pudo iniciar el proceso.";

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(Timeout);

        var stdoutTask = process.StandardOutput.ReadToEndAsync(timeoutCts.Token);
        var stderrTask = process.StandardError.ReadToEndAsync(timeoutCts.Token);
        try
        {
            await process.WaitForExitAsync(timeoutCts.Token);
        }
        catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested && !ct.IsCancellationRequested)
        {
            try { process.Kill(entireProcessTree: true); } catch { }
            return $"El comando superó el tiempo máximo ({Timeout.TotalSeconds:0} s) y fue cancelado.";
        }

        var stdout = await stdoutTask;
        var stderr = await stderrTask;
        var output = string.IsNullOrWhiteSpace(stdout)
            ? stderr
            : stdout + (string.IsNullOrWhiteSpace(stderr) ? "" : "\n[stderr]\n" + stderr);
        if (string.IsNullOrWhiteSpace(output)) output = $"(sin salida, código de salida {process.ExitCode})";
        return output.Length > MaxChars ? output[..MaxChars] + "\n[TRUNCADO]" : output;
    }
}
