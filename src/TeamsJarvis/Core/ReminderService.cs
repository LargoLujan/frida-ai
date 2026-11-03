using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace TeamsJarvis.Core;

/// <summary>
/// Comprueba cada minuto si hay recordatorios vencidos y lanza un aviso (toast).
/// Marca cada uno como avisado para no repetirlo. El aviso concreto lo hace un delegado
/// que registra la UI (para no acoplar el núcleo a las notificaciones de Windows).
/// </summary>
public sealed class ReminderService : BackgroundService
{
    private readonly NotesStore _notes;
    private readonly ILogger<ReminderService> _logger;

    /// <summary>Lo fija la UI: cómo mostrar el aviso de un recordatorio vencido.</summary>
    public Action<Reminder>? Notify { get; set; }

    public ReminderService(NotesStore notes, ILogger<ReminderService> logger)
    {
        _notes = notes;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Pequeña espera para que la UI registre su delegado de aviso.
        try { await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken); } catch { return; }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                foreach (var due in _notes.GetDueUnnotified(DateTimeOffset.Now))
                {
                    Notify?.Invoke(due);
                    _notes.MarkNotified(due.Id);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Fallo comprobando recordatorios.");
            }

            try { await Task.Delay(TimeSpan.FromSeconds(60), stoppingToken); }
            catch (OperationCanceledException) { break; }
        }
    }
}
