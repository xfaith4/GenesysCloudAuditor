using System.Windows;
using GenesysExtensionAudit.Services;
using Microsoft.Extensions.DependencyInjection;

namespace GenesysExtensionAudit;

public partial class App : System.Windows.Application
{
    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        try
        {
            Bootstrapper.Initialize();
            await Bootstrapper.StartAsync();
            _ = Task.Run(async () =>
            {
                var cache = Bootstrapper.Services.GetRequiredService<IAuditLogCatalogCache>();
                await cache.WarmAsync(CancellationToken.None).ConfigureAwait(false);
            });

            var mainWindow = Bootstrapper.Services.GetRequiredService<MainWindow>();
            MainWindow = mainWindow;
            mainWindow.Show();
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"Genesys Cloud Auditor could not start.\n\n{ex.Message}",
                "Startup Error",
                MessageBoxButton.OK,
                MessageBoxImage.Error);

            Bootstrapper.Dispose();
            Shutdown(-1);
        }
    }

    protected override async void OnExit(ExitEventArgs e)
    {
        try
        {
            await Bootstrapper.StopAsync();
        }
        finally
        {
            Bootstrapper.Dispose();
            base.OnExit(e);
        }
    }
}
