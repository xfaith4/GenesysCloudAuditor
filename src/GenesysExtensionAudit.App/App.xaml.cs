using System.Windows;
using Microsoft.Extensions.DependencyInjection;

namespace GenesysExtensionAudit;

public partial class App : System.Windows.Application
{
    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        Bootstrapper.Initialize();
        await Bootstrapper.StartAsync();

        var mainWindow = Bootstrapper.Services.GetRequiredService<MainWindow>();
        MainWindow = mainWindow;
        mainWindow.Show();
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
