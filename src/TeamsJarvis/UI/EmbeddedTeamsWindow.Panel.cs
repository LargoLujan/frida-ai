using System.Windows;
using Microsoft.Extensions.Logging;
using TeamsJarvis.Core;

namespace TeamsJarvis.UI;

// Panel de respuestas sugeridas: cabecera/agente, tarjeta, recientes, gasto, tono y copiar.
public partial class EmbeddedTeamsWindow
{
    // ---- Cabecera, agente en uso y gráfica de gasto ---------------------------

    private void InitAccountAndAgent()
    {
        BrandLogo.Source = FridaIcon.CreateImageSource();
        ThemeMenuItem.Content = ThemeManager.IsDark ? "☀  Modo claro" : "🌙  Modo oscuro";
        BarkMenuItem.Content = BarkPlayer.Enabled ? "🔊  Ladridos: activados" : "🔊  Ladridos: desactivados";
        var name = string.IsNullOrWhiteSpace(_options.Profile.Name) ? "Tu cuenta" : _options.Profile.Name;
        AccountName.Text = name;
        AccountInitial.Text = name.Trim().Length > 0 ? name.Trim()[..1].ToUpperInvariant() : "J";
        AccountRole.Text = string.IsNullOrWhiteSpace(_options.Profile.Role)
            ? "Sesión de Teams · Azure/Fabric (az CLI)" : _options.Profile.Role;
        AgentNameText.Text = FriendlyAgent(_options.Provider, _options.Model);
    }

    private static string FriendlyAgent(string provider, string model)
    {
        var p = provider.ToLowerInvariant() switch
        {
            "openai" => "OpenAI", "gemini" => "Gemini", "ollama" => "Ollama (local)",
            "simulation" => "Simulación", _ => "Claude",
        };
        return $"{p} · {model}";
    }

    private void PulseAgent(bool on)
    {
        if (on)
        {
            AgentNameText.Text = FriendlyAgent(_options.Provider, _options.Model);
            var anim = new System.Windows.Media.Animation.DoubleAnimation(0.3, 1.0,
                new Duration(TimeSpan.FromSeconds(0.7)))
            {
                AutoReverse = true,
                RepeatBehavior = System.Windows.Media.Animation.RepeatBehavior.Forever,
            };
            AgentPulse.BeginAnimation(OpacityProperty, anim);
        }
        else
        {
            AgentPulse.BeginAnimation(OpacityProperty, null);
            AgentPulse.Opacity = 0.3;
        }
    }

    // ---- Recientes y gasto: el asistente los cuenta en su bocadillo ----------

    private void OnRecientes(object sender, RoutedEventArgs e) => MascotSay(BuildRecentSummary(), sticky: true);

    private void OnGasto(object sender, RoutedEventArgs e) => MascotSay(BuildCostSummary(), sticky: true);

    private string BuildRecentSummary()
    {
        if (string.IsNullOrWhiteSpace(_currentChatId))
            return "Abre un chat en Teams y te cuento sus sugerencias recientes.";
        var recientes = _store.GetSuggestions(limit: 6, chatId: _currentChatId);
        if (recientes.Count == 0)
            return $"Aún no hay sugerencias recientes en «{_currentChatName}». Pulsa «Sugerir respuesta» cuando quieras.";
        var sb = new System.Text.StringBuilder($"Recientes en «{_currentChatName}»:\n");
        foreach (var s in recientes)
            sb.AppendLine($"• {s.CreatedAt.ToLocalTime():dd/MM HH:mm} · {ContextBuilder.Truncate(s.SuggestedReply.Replace("\n", " "), 80)}");
        return sb.ToString().TrimEnd();
    }

    private string BuildCostSummary()
    {
        var monthStart = new DateTimeOffset(DateTimeOffset.Now.Year, DateTimeOffset.Now.Month, 1, 0, 0, 0, DateTimeOffset.Now.Offset);
        var rows = _store.GetCostByModelSince(monthStart);
        if (rows.Count == 0)
            return "Este mes aún no has gastado nada de API. El triaje/RAG local (Ollama) es gratis.";
        var total = rows.Where(r => !CostEstimator.IsLocal(r.Model)).Sum(r => r.CostUsd);
        var sb = new System.Text.StringBuilder($"Gasto de API este mes: ${total:0.00}. Por agente:\n");
        foreach (var r in rows)
            sb.AppendLine($"• {r.Model}: {(CostEstimator.IsLocal(r.Model) ? "gratis" : $"${r.CostUsd:0.00}")}");
        sb.Append("(Lo local es gratis.)");
        return sb.ToString();
    }

    // ---- Tarjeta de sugerencia (la respuesta a copiar) ------------------------

    private void ShowSuggestion(Suggestion s)
    {
        // Cabecera de la tarjeta: primero el nombre del chat/grupo.
        CardChatName.Text = s.ChatName;
        CardSubtitle.Text = string.Equals(s.ChatName, s.ReplyTo, StringComparison.OrdinalIgnoreCase)
            ? $"{s.CreatedAt.ToLocalTime():HH:mm}"
            : $"responde a {s.ReplyTo} · {s.CreatedAt.ToLocalTime():HH:mm}";

        // Chip de confianza: verde ≥0,7 · ámbar ≥0,4 · rojo <0,4
        var color = s.Confidence >= 0.7 ? System.Windows.Media.Color.FromRgb(0x1E, 0x6B, 0x36)
                  : s.Confidence >= 0.4 ? System.Windows.Media.Color.FromRgb(0xB9, 0x6A, 0x00)
                  : System.Windows.Media.Color.FromRgb(0xC0, 0x39, 0x2B);
        ConfText.Text = $"{s.Confidence:P0}";
        ConfText.Foreground = new System.Windows.Media.SolidColorBrush(color);
        ConfChip.Visibility = Visibility.Visible;

        RecipientText.Text = string.Equals(s.ChatName, s.ReplyTo, StringComparison.OrdinalIgnoreCase)
            ? $"➡ Para {s.ReplyTo}"
            : $"➡ {s.ChatName}  ·  responde a {s.ReplyTo}";
        RecipientChip.Visibility = Visibility.Visible;

        SugText.Text = s.SuggestedReply;
        SugSources.Text = string.IsNullOrWhiteSpace(s.Sources) ? "(sin fuentes)" : s.Sources;
        MentionBanner.Visibility = s.TriggerSummary.StartsWith("🔔") ? Visibility.Visible : Visibility.Collapsed;
        SetCardExpanded(true); // una sugerencia nueva se muestra desplegada
        AnimateCardIn();
        MascotSay($"¡Listo! Te dejo abajo una respuesta para {s.ChatName}. Cópiala cuando quieras.", sticky: true);
    }

    /// <summary>
    /// Resultado SIN respuesta que copiar (triaje lo descartó, no hace falta responder, o error).
    /// El MOTIVO completo se cuenta en el bocadillo del asistente; la tarjeta no lo duplica.
    /// </summary>
    private void ShowOutcome(string text)
    {
        RecipientChip.Visibility = Visibility.Collapsed;
        MentionBanner.Visibility = Visibility.Collapsed;
        ConfChip.Visibility = Visibility.Collapsed;
        var isError = text.StartsWith("⛔");
        CardChatName.Text = isError ? "Sin respuesta (error)" : "Sin respuesta que copiar";
        CardChatName.Foreground = new System.Windows.Media.SolidColorBrush(isError
            ? System.Windows.Media.Color.FromRgb(0xC0, 0x39, 0x2B)
            : System.Windows.Media.Color.FromRgb(0xB9, 0x6A, 0x00));
        CardSubtitle.Text = _currentChatName;
        SugText.Text = "";          // el motivo va en el bocadillo del asistente, no aquí
        SugSources.Text = "";
        SetCardExpanded(false);     // no ocupa espacio con texto duplicado
        MascotSay(text, sticky: true);
    }

    private void SetCardExpanded(bool expanded)
    {
        SugBody.Visibility = expanded ? Visibility.Visible : Visibility.Collapsed;
        CardChevron.Text = expanded ? "▾" : "▸";
    }

    /// <summary>Animación de entrada de la tarjeta: fundido + ligero deslizamiento hacia arriba.</summary>
    private void AnimateCardIn()
    {
        var dur = new Duration(TimeSpan.FromSeconds(0.28));
        var ease = new System.Windows.Media.Animation.CubicEase
        {
            EasingMode = System.Windows.Media.Animation.EasingMode.EaseOut,
        };
        SugCard.BeginAnimation(OpacityProperty,
            new System.Windows.Media.Animation.DoubleAnimation(0.0, 1.0, dur));
        SugCardShift.BeginAnimation(System.Windows.Media.TranslateTransform.YProperty,
            new System.Windows.Media.Animation.DoubleAnimation(14, 0, dur) { EasingFunction = ease });
    }

    private void OnToggleCard(object sender, RoutedEventArgs e) =>
        SetCardExpanded(SugBody.Visibility != Visibility.Visible);

    private void OnCopy(object sender, RoutedEventArgs e)
    {
        if (!string.IsNullOrWhiteSpace(SugText.Text)) System.Windows.Clipboard.SetText(SugText.Text);
    }

    /// <summary>Reescribe la sugerencia actual en el tono elegido con el modelo local (coste 0).</summary>
    private async void OnRewriteTone(object sender, RoutedEventArgs e)
    {
        var original = SugText.Text?.Trim() ?? "";
        if (original.Length == 0) return;

        var tone = (ToneBox.SelectedItem as System.Windows.Controls.ComboBoxItem)?.Tag?.ToString() ?? "cordial";
        var instruction = tone switch
        {
            "formal" => "un tono formal y profesional, de usted",
            "breve" => "un tono muy breve y directo, sin rodeos",
            "tecnico" => "un tono técnico y preciso, con la terminología adecuada",
            _ => "un tono cordial y cercano pero profesional",
        };

        RewriteBtn.IsEnabled = false;
        var previousStatus = Status.Text;
        Status.Text = "Reescribiendo en local (gratis)…";
        try
        {
            var system = "Reescribe el mensaje del usuario en español manteniendo EXACTAMENTE el mismo significado " +
                         $"y los mismos datos, cambiando solo el estilo a {instruction}. Devuelve solo el mensaje reescrito, sin comillas ni explicaciones.";
            var rewritten = await _local.ChatAsync(system, original, 600);
            if (!string.IsNullOrWhiteSpace(rewritten))
            {
                SugText.Text = rewritten.Trim();
                Status.Text = $"Reescrito en tono {tone} (local, sin coste).";
            }
            else
            {
                Status.Text = "Reescritura no disponible: instala Ollama ('ollama pull llama3.2:3b').";
            }
        }
        catch (Exception ex)
        {
            Status.Text = "No se pudo reescribir: " + ex.Message;
            _logger.LogWarning(ex, "Fallo al reescribir el tono en local.");
        }
        finally
        {
            RewriteBtn.IsEnabled = true;
            if (Status.Text.StartsWith("Reescribiendo")) Status.Text = previousStatus;
        }
    }
}
