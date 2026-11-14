using System.Windows;
using DupliXon.App.ViewModels;
using DupliXon.App.Views;

namespace DupliXon.App;

public partial class App : Application
{
    private MainViewModel? _vm;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        _vm = new MainViewModel();
        var window = new MainWindow { DataContext = _vm };
        _vm.RequestAttention += window.BringToFront;
        MainWindow = window;
        window.Show();
        await _vm.InitializeAsync();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        // Stop encoders (ffmpeg child processes) and the web server before leaving.
        _vm?.DisposeAsync().AsTask().Wait(TimeSpan.FromSeconds(3));
        base.OnExit(e);
    }
}
