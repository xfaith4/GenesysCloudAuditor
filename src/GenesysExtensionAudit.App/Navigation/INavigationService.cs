using System.Collections.ObjectModel;

namespace GenesysExtensionAudit.ViewModels;

/// <summary>
/// Represents a single navigable item in the shell's tab bar.
/// </summary>
public sealed class NavigationItem
{
    public string Key { get; }
    public string Title { get; }
    public object Content { get; }

    public NavigationItem(string key, string title, object content)
    {
        Key = key;
        Title = title;
        Content = content;
    }
}

/// <summary>
/// Manages the shell's navigation items and current selection.
/// </summary>
public interface INavigationService
{
    ReadOnlyObservableCollection<NavigationItem> Items { get; }
    NavigationItem? Current { get; }

    /// <summary>
    /// Registers a navigable destination with a factory that produces its ViewModel.
    /// The factory is called once at registration time to create the Content.
    /// </summary>
    void Register(string key, string displayName, Func<object> factory);

    /// <summary>Sets the active navigation item by key.</summary>
    void Navigate(string key);
}
