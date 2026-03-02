using CommunityToolkit.Mvvm.ComponentModel;
using System.Collections.ObjectModel;

namespace GenesysExtensionAudit.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private readonly INavigationService _navigation;

    public MainViewModel(INavigationService navigation)
    {
        _navigation = navigation;
    }

    public ReadOnlyObservableCollection<NavigationItem> NavigationItems => _navigation.Items;

    public string StatusText => "Idle";
    public string FooterText => "Ready";

    public NavigationItem? CurrentItem
    {
        get => _navigation.Current;
        set
        {
            // TabControl sets SelectedItem directly; keep navigation service in sync.
            if (value is null) return;
            _navigation.Navigate(value.Key);
            OnPropertyChanged();
        }
    }
}
