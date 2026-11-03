using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace TeamsJarvis.UI;

public abstract class ObservableObject : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    protected bool Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        return true;
    }

    protected void Raise([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

/// <summary>Elemento de la lista de chats (panel izquierdo).</summary>
public sealed class ChatItemVm : ObservableObject
{
    private int _pendingCount;
    private bool _isAnalyzing;
    private string _lastActivity = "";

    private string _name = "";
    public required string ChatId { get; init; }
    public required string Name { get => _name; set => Set(ref _name, value); }

    public int PendingCount
    {
        get => _pendingCount;
        set { if (Set(ref _pendingCount, value)) Raise(nameof(HasPending)); }
    }

    public bool HasPending => _pendingCount > 0;

    public bool IsAnalyzing { get => _isAnalyzing; set => Set(ref _isAnalyzing, value); }

    public string LastActivity { get => _lastActivity; set => Set(ref _lastActivity, value); }
}

/// <summary>Burbuja de mensaje en la vista de conversación.</summary>
public sealed class MessageVm
{
    public required string AuthorName { get; init; }
    public required string TimeText { get; init; }
    public required string Text { get; init; }
    public required bool IsMe { get; init; }
    public bool HasImages { get; init; }
}
