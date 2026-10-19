using Microsoft.UI.Xaml;
using Microsoft.Windows.AppLifecycle;
using Windows.ApplicationModel.Activation;

namespace VirCre.Desktop;

public partial class App : Application {
    private MainWindow? window;
    private AppInstance? instance;
    public App() { InitializeComponent();UnhandledException+=App_UnhandledException; }
    private void App_UnhandledException(object sender,Microsoft.UI.Xaml.UnhandledExceptionEventArgs args) {
        RuntimeDiagnostics.Write("unhandled-exception",args.Exception);
        try {if(window?.TryRecoverCatalogFormatError(args.Exception)==true)args.Handled=true;}
        catch(Exception recoveryError) {RuntimeDiagnostics.Write("unhandled-recovery-failed",recoveryError);}
        // Unknown exceptions remain unhandled: continuing after arbitrary UI corruption is unsafe.
    }
    protected override async void OnLaunched(Microsoft.UI.Xaml.LaunchActivatedEventArgs args) {
        instance=AppInstance.FindOrRegisterForKey("virCre.Desktop.launcher");
        if(!instance.IsCurrent) {
            await instance.RedirectActivationToAsync(AppInstance.GetCurrent().GetActivatedEventArgs());
            Exit();return;
        }
        window = new MainWindow();
        instance.Activated+=(_,activation)=>window.DispatcherQueue.TryEnqueue(()=>window.ActivateShortcut(
            activation.Data is ILaunchActivatedEventArgs launch?ActivationArguments.ParseCommandLine(launch.Arguments):null));
        window.Activate();
        window.ActivateShortcut(ActivationArguments.Parse(Environment.GetCommandLineArgs().Skip(1).ToArray()));
    }
}
