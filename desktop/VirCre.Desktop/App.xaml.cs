using Microsoft.UI.Xaml;

namespace VirCre.Desktop;

public partial class App : Application {
    private Window? window;
    public App() { InitializeComponent(); }
    protected override void OnLaunched(LaunchActivatedEventArgs args) {
        window = new MainWindow();
        window.Activate();
    }
}
