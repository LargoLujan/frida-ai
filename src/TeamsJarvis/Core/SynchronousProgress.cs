namespace TeamsJarvis.Core;

/// <summary>
/// <see cref="IProgress{T}"/> que entrega cada informe de forma SÍNCRONA y en orden: captura el
/// <see cref="SynchronizationContext"/> al crearse (como <see cref="Progress{T}"/>) pero usa
/// <c>Send</c> en vez de <c>Post</c>. En WPF eso es <c>Dispatcher.Invoke</c>: en el hilo de la UI
/// se ejecuta en línea y desde otro hilo espera a que la UI lo aplique.
///
/// Por qué no <see cref="Progress{T}"/>: encola cada informe y lo aplica más tarde, mientras que el
/// resto de avisos del análisis (sugerencia, triaje, resultado) se aplican al momento. Si el análisis
/// no cede el hilo entre el último informe y el resultado (simulación, o el "Redactando…" final de
/// Claude), el informe encolado llega DESPUÉS de pintar la respuesta y la pisa en el bocadillo.
/// </summary>
public sealed class SynchronousProgress<T> : IProgress<T>
{
    private readonly Action<T> _handler;
    private readonly SynchronizationContext? _context;

    public SynchronousProgress(Action<T> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        _handler = handler;
        _context = SynchronizationContext.Current;
    }

    public void Report(T value)
    {
        // Sin contexto (consola, tests) se llama directamente; nunca se manda al pool de hilos.
        if (_context is null) _handler(value);
        else _context.Send(_ => _handler(value), null);
    }
}
