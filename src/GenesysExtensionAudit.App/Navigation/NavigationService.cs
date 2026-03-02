using System.Collections.ObjectModel;

namespace GenesysExtensionAudit.ViewModels;

/// <summary>
/// Simple navigation service backed by an ObservableCollection.
/// Items are registered once at startup; navigation changes the Current item.
/// </summary>
public sealed class NavigationService : INavigationService
{
    private readonly ObservableCollection<NavigationItem> _items = new();
    private NavigationItem? _current;

    public ReadOnlyObservableCollection<NavigationItem> Items { get; }

    public NavigationService()
    {
        Items = new ReadOnlyObservableCollection<NavigationItem>(_items);
    }

    public NavigationItem? Current => _current;

    public void Register(string key, string displayName, Func<object> factory)
    {
        if (string.IsNullOrWhiteSpace(key))
            throw new ArgumentException("Key is required.", nameof(key));

        if (factory is null)
            throw new ArgumentNullException(nameof(factory));

        var content = factory();
        var item = new NavigationItem(key, displayName, content);
        _items.Add(item);
    }

    public void Navigate(string key)
    {
        var item = _items.FirstOrDefault(i =>
            string.Equals(i.Key, key, StringComparison.OrdinalIgnoreCase));

        if (item is not null)
            _current = item;
    }
}
