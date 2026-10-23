using System.ComponentModel;

namespace ClaudeSessions;

/// <summary>One row in the environments panel. Path is null for the "All environments" row.</summary>
public sealed class ProjectItem : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    public ProjectItem(string name, string? path, string? detail = null, bool hidden = false)
    {
        Name = name;
        Path = path;
        Detail = detail;
        IsHidden = hidden;
    }

    public string Name { get; }
    public string? Path { get; }
    /// <summary>Folder hint shown in grey when two projects share a name.</summary>
    public string? Detail { get; }
    public bool HasDetail => Detail != null;
    public bool IsHidden { get; }
    public double RowOpacity => IsHidden ? 0.45 : 1.0;
    public string HideMenuText => IsHidden ? "Show environment again" : "Hide environment";
    public bool CanStart => Path != null;
    public string Tooltip => Path != null ? $"Show only sessions in {Path}" : "Show sessions from every environment";
    public string NewTooltip => $"New Claude session in {Name}";

    /// <summary>Null for "All environments" and until the icon is known.</summary>
    public ProjectIconLook? Look
    {
        get => look;
        set
        {
            if (look == value) return;
            look = value;
            Raise(nameof(Look));
            Raise(nameof(Icon));
            Raise(nameof(IconTooltip));
            Raise(nameof(CanUseGenerated));
            Raise(nameof(CanResetIcon));
        }
    }

    public System.Windows.Media.ImageSource? Icon => look?.Image;
    public string? IconTooltip => look?.Tooltip;
    public bool IsAll => Path == null;
    public bool CanUseGenerated => look != null && look.Source != IconSource.Generated;
    public bool CanResetIcon => look?.Overridden == true;

    public int Count
    {
        get => count;
        set
        {
            if (count == value) return;
            count = value;
            Raise(nameof(Count));
            Raise(nameof(CountText));
        }
    }

    public string CountText => count > 0 ? count.ToString() : "";

    public bool IsSelected
    {
        get => selected;
        set
        {
            if (selected == value) return;
            selected = value;
            Raise(nameof(IsSelected));
        }
    }

    /// <summary>True when a session folder is this project or inside it (subfolders, worktrees).</summary>
    public bool Contains(string cwd) => Path == null || IsInside(cwd, Path);

    public static bool IsInside(string cwd, string folder)
    {
        cwd = cwd.TrimEnd('\\', '/');
        return cwd.Equals(folder, System.StringComparison.OrdinalIgnoreCase)
            || cwd.StartsWith(folder + "\\", System.StringComparison.OrdinalIgnoreCase);
    }

    int count;
    bool selected;
    ProjectIconLook? look;

    void Raise(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
