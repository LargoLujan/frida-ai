using System.Windows;
using Microsoft.Extensions.Options;
using TeamsJarvis.Core;

namespace TeamsJarvis.UI;

public partial class CostWindow : Window
{
    public CostWindow(SuggestionStore store, IOptions<JarvisOptions> options)
    {
        InitializeComponent();
        Icon = FridaIcon.CreateImageSource();
        Load(store, options.Value);
    }

    private void Load(SuggestionStore store, JarvisOptions options)
    {
        var now = DateTimeOffset.Now;
        var startOfDay = new DateTimeOffset(now.Year, now.Month, now.Day, 0, 0, 0, now.Offset);
        var startOfMonth = new DateTimeOffset(now.Year, now.Month, 1, 0, 0, 0, now.Offset);

        var today = store.GetUsageSince(startOfDay);
        var month = store.GetUsageSince(startOfMonth);
        var all = store.GetUsageSince(null);

        TodayCost.Text = Money(today.CostUsd);
        TodayCalls.Text = $"{today.Calls} sugerencia(s)";
        MonthCost.Text = Money(month.CostUsd);
        MonthCalls.Text = $"{month.Calls} sugerencia(s)";

        // Proyección: coste del mes escalado a los días del mes.
        var daysElapsed = Math.Max(1, (now - startOfMonth).TotalDays);
        var daysInMonth = DateTime.DaysInMonth(now.Year, now.Month);
        Projection.Text = Money(month.CostUsd / daysElapsed * daysInMonth);

        TotalsText.Text = $"Total histórico: {Money(all.CostUsd)} en {all.Calls} sugerencias · " +
            $"{Tokens(all.InputTokens)} entrada, {Tokens(all.OutputTokens)} salida, " +
            $"{Tokens(all.CacheReadTokens)} leídos de caché.";

        var byModel = store.GetCostByModelSince(startOfMonth)
            .Select(m => new ModelRow(m.Model, m.Calls, Money(m.CostUsd)))
            .ToList();
        ModelGrid.ItemsSource = byModel;

        var pricing = CostEstimator.GetPricing(options.Model);
        var known = CostEstimator.IsKnownModel(options.Model) ? "" : " (precio estimado; modelo no reconocido)";
        RateNote.Text = $"Precios usados para {options.Model}: ${pricing.InputPerMTok:0.##} entrada / " +
            $"${pricing.OutputPerMTok:0.##} salida por 1M tokens{known}. En USD.";
    }

    private static string Money(double usd) => $"${usd:0.00}";

    private static string Tokens(long t) => t >= 1_000_000
        ? $"{t / 1_000_000.0:0.#}M"
        : t >= 1_000 ? $"{t / 1_000.0:0.#}k" : t.ToString();

    private void OnClose(object sender, RoutedEventArgs e) => Close();

    private sealed record ModelRow(string Model, int Calls, string CostText);
}
