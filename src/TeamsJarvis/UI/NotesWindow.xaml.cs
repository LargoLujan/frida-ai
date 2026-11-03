using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using TeamsJarvis.Core;

namespace TeamsJarvis.UI;

public partial class NotesWindow : Window
{
    private readonly NotesStore _store;
    private long _currentNoteId;

    public NotesWindow(NotesStore store)
    {
        InitializeComponent();
        _store = store;
        Icon = FridaIcon.CreateImageSource();
        FillTimeOptions();
        RemDate.SelectedDate = DateTime.Today;
        RemTime.Text = DateTime.Now.AddHours(1).ToString("HH:00");
        ReloadNotes();
        ReloadReminders();
    }

    // ---- Notas ---------------------------------------------------------------

    private void ReloadNotes()
    {
        NotesList.ItemsSource = _store.GetNotes().Select(n => new NoteVm(n)).ToList();
    }

    private void OnNoteSelected(object sender, SelectionChangedEventArgs e)
    {
        if (NotesList.SelectedItem is not NoteVm vm) return;
        _currentNoteId = vm.Id;
        NoteTitle.Text = vm.Title;
        NoteProject.Text = vm.Project;
        NoteBody.Text = vm.Body;
    }

    private void OnNewNote(object sender, RoutedEventArgs e)
    {
        _currentNoteId = 0;
        NotesList.SelectedItem = null;
        NoteTitle.Text = "";
        NoteProject.Text = "";
        NoteBody.Text = "";
        NoteTitle.Focus();
    }

    private void OnSaveNote(object sender, RoutedEventArgs e)
    {
        var title = string.IsNullOrWhiteSpace(NoteTitle.Text) ? "(sin título)" : NoteTitle.Text.Trim();
        _currentNoteId = _store.UpsertNote(_currentNoteId, title, NoteBody.Text, NoteProject.Text.Trim());
        ReloadNotes();
    }

    private void OnDeleteNote(object sender, RoutedEventArgs e)
    {
        if (_currentNoteId <= 0) return;
        if (MessageBox.Show(this, "¿Borrar esta nota?", "Frida.ai",
                MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        _store.DeleteNote(_currentNoteId);
        OnNewNote(sender, e);
        ReloadNotes();
    }

    // ---- Recordatorios -------------------------------------------------------

    private void FillTimeOptions()
    {
        for (var h = 7; h <= 21; h++)
        {
            RemTime.Items.Add($"{h:00}:00");
            RemTime.Items.Add($"{h:00}:30");
        }
    }

    private void ReloadReminders()
    {
        var reminders = _store.GetReminders().Select(r => new ReminderVm(r)).ToList();
        RemList.ItemsSource = reminders;
        var pending = reminders.Count(r => !r.Done);
        RemHeader.Text = $"Recordatorios · {pending} pendiente(s)";
    }

    private void OnAddReminder(object sender, RoutedEventArgs e)
    {
        var text = RemText.Text.Trim();
        if (text.Length == 0) { RemText.Focus(); return; }
        if (!TryBuildDue(out var due))
        {
            MessageBox.Show(this, "Revisa la fecha y la hora (HH:mm).", "Frida.ai",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        _store.UpsertReminder(0, text, due, RemProject.Text.Trim());
        RemText.Text = "";
        RemProject.Text = "";
        ReloadReminders();
    }

    private bool TryBuildDue(out DateTimeOffset due)
    {
        due = default;
        var date = RemDate.SelectedDate ?? DateTime.Today;
        if (!TimeSpan.TryParse(RemTime.Text?.Trim(), out var time)) return false;
        due = new DateTimeOffset(date.Date + time, DateTimeOffset.Now.Offset);
        return true;
    }

    private void OnToggleDone(object sender, RoutedEventArgs e)
    {
        if (sender is CheckBox { Tag: long id } cb)
        {
            _store.SetReminderDone(id, cb.IsChecked == true);
            ReloadReminders();
        }
    }

    private void OnDeleteReminder(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: long id })
        {
            _store.DeleteReminder(id);
            ReloadReminders();
        }
    }

    private void OnExportIcs(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: long id }) return;
        var reminder = _store.GetReminderById(id);
        if (reminder is null) return;
        try
        {
            var path = IcsExporter.WriteToTemp(reminder);
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"No se pudo abrir el .ics: {ex.Message}", "Frida.ai",
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    // ---- View models ---------------------------------------------------------

    private sealed class NoteVm(ProjectNote n)
    {
        public long Id => n.Id;
        public string Title => n.Title;
        public string Body => n.Body;
        public string Project => string.IsNullOrWhiteSpace(n.Project) ? "sin proyecto" : n.Project;
        public string UpdatedText => n.UpdatedAt.ToLocalTime().ToString("dd/MM/yyyy HH:mm");
    }

    private sealed class ReminderVm(Reminder r)
    {
        public long Id => r.Id;
        public string Text => r.Text;
        public bool Done => r.Done;
        public string DueText => r.DueAt.ToLocalTime().ToString("dddd dd/MM · HH:mm");
        public string ProjectSuffix => string.IsNullOrWhiteSpace(r.Project) ? "" : $" · {r.Project}";
        public TextDecorationCollection? Strike => r.Done ? TextDecorations.Strikethrough : null;
        public Brush Fg => r.Done ? Brushes.Gray : Brushes.Black;
        public Brush DueFg => !r.Done && r.DueAt < DateTimeOffset.Now
            ? new SolidColorBrush(Color.FromRgb(0xC0, 0x39, 0x2B)) // vencido: rojo
            : new SolidColorBrush(Color.FromRgb(0x88, 0x88, 0x88));
    }
}
