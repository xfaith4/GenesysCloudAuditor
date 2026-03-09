using System.Windows;
using System.Windows.Controls;
using GenesysExtensionAudit.ViewModels;

namespace GenesysExtensionAudit.Views;

public partial class SettingsView : UserControl
{
    public SettingsView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        // Pre-populate the PasswordBox when the ViewModel is first bound.
        if (e.NewValue is SettingsViewModel vm)
        {
            TokenBox.Password = vm.GitHubToken;
            ClientSecretBox.Password = vm.ClientSecret;
        }
    }

    private void TokenBox_PasswordChanged(object sender, RoutedEventArgs e)
    {
        if (DataContext is SettingsViewModel vm)
            vm.GitHubToken = TokenBox.Password;
    }

    private void ClientSecretBox_PasswordChanged(object sender, RoutedEventArgs e)
    {
        if (DataContext is SettingsViewModel vm)
            vm.ClientSecret = ClientSecretBox.Password;
    }
}
