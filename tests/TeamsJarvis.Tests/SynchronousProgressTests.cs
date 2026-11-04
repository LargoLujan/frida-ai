using TeamsJarvis.Core;

namespace TeamsJarvis.Tests;

/// <summary>
/// El progreso del análisis debe llegar a la UI en orden con el resultado. Se usa un contexto que,
/// como el Dispatcher de WPF, aplica los Post más tarde (cola) y los Send en el acto.
/// </summary>
public class SynchronousProgressTests
{
    private const string LastReport = "Vale, esto sí merece respuesta. Lo analizo a fondo y redacto…";
    private const string Result = "¡Listo! Te dejo abajo una respuesta.";

    [Fact]
    public async Task ReportFromWorkerThread_IsDeliveredThroughSendBeforeReturning()
    {
        var ctx = new DeferringContext();
        var log = new List<string>();
        var progress = CreateUnder(ctx, () => new SynchronousProgress<string>(log.Add));

        await Task.Run(() => progress.Report("Leyendo el chat…"));

        Assert.Equal(["Leyendo el chat…"], log);
        Assert.Equal(1, ctx.Sends);
        Assert.Equal(0, ctx.Pending); // nada encolado para después
    }

    [Fact]
    public void ReportWithoutContext_RunsInline()
    {
        var log = new List<string>();
        var progress = CreateUnder(null, () => new SynchronousProgress<string>(log.Add));

        progress.Report("Leyendo el chat…");

        Assert.Equal(["Leyendo el chat…"], log);
    }

    // Secuencia del análisis cuando no cede el hilo (simulación): último informe y, en el acto, el
    // resultado (el aviso de sugerencia usa Dispatcher.Invoke). Después la UI procesa su cola.

    [Fact]
    public void LastReport_DoesNotOverwriteTheResultShownRightAfter()
    {
        var ctx = new DeferringContext();
        var bubble = "";
        var progress = CreateUnder(ctx, () => new SynchronousProgress<string>(t => bubble = t));

        progress.Report(LastReport);
        bubble = Result;
        ctx.Drain();

        Assert.Equal(Result, bubble);
    }

    [Fact]
    public void ProgressOfT_OverwritesTheResult_WhichWasTheBug()
    {
        // Progress<T> usa Post: el último informe se aplica DESPUÉS del resultado y lo pisa.
        var ctx = new DeferringContext();
        var bubble = "";
        var progress = CreateUnder<IProgress<string>>(ctx, () => new Progress<string>(t => bubble = t));

        progress.Report(LastReport);
        bubble = Result;
        ctx.Drain();

        Assert.Equal(LastReport, bubble);
    }

    /// <summary>Construye el objeto con <paramref name="ctx"/> como contexto actual (lo captura) y lo restaura.</summary>
    private static T CreateUnder<T>(SynchronizationContext? ctx, Func<T> factory)
    {
        var previous = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(ctx);
        try { return factory(); }
        finally { SynchronizationContext.SetSynchronizationContext(previous); }
    }

    /// <summary>Post se encola hasta <see cref="Drain"/>; Send se ejecuta en el acto (como el Dispatcher).</summary>
    private sealed class DeferringContext : SynchronizationContext
    {
        private readonly Queue<(SendOrPostCallback Callback, object? State)> _posted = new();
        private int _sends;

        public int Sends => Volatile.Read(ref _sends);

        public int Pending
        {
            get { lock (_posted) return _posted.Count; }
        }

        public override void Post(SendOrPostCallback d, object? state)
        {
            lock (_posted) _posted.Enqueue((d, state));
        }

        public override void Send(SendOrPostCallback d, object? state)
        {
            Interlocked.Increment(ref _sends);
            d(state);
        }

        public void Drain()
        {
            while (true)
            {
                (SendOrPostCallback Callback, object? State) work;
                lock (_posted)
                {
                    if (!_posted.TryDequeue(out work)) return;
                }
                work.Callback(work.State);
            }
        }
    }
}
