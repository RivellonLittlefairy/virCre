using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using VirCre.Launcher;
using VirCre.Presentation;

namespace VirCre.Desktop;

public sealed partial class MainWindow {
    private bool shortcutOnly;
    private int pendingShortcutActivations;
    private Task shortcutTask=Task.CompletedTask;
    private readonly List<Task> shortcutWrites=[];
    public void ActivateShortcut(string? payload) {
        if(windowShutdown.HasStarted) {
            if(!closing) {ShowLauncher();ShowAction("正在结束会话，请先关闭剩余应用窗口。",true);}
            return;
        }
        if(payload==null) {ShowLauncher();return;}
        pendingShortcutActivations++;
        shortcutTask=OpenShortcutAfterAsync(shortcutTask,payload,model.ExportSettings());
    }
    private void ShowLauncher() {
        shortcutOnly=false;
        Activate();
    }
    private void CloseIdleShortcutSession() {
        if(!shortcutOnly || closing || windowShutdown.HasStarted || pendingShortcutActivations!=0 || appWindows.Windows.Count!=0)return;
        RuntimeDiagnostics.Write("shortcut-session-ended");
        Close();
    }
    private ShortcutLauncher? shortcutLauncher;
    private ShortcutLauncher GetShortcutLauncher() {
        if(shortcutLauncher!=null)return shortcutLauncher;
        var runner=new CommandRunner();
        var directory=new PhoneCatalog(runner);
        return shortcutLauncher=new(directory,runner,AppLaunches,(serial,packageName,token)=>directory.CanLaunchAsync(serial,packageName,token));
    }
    private async Task OpenShortcutAfterAsync(Task previous, string payload, UserSettings settings) {
        try {
            await previous;
            if(closing || windowShutdown.HasStarted) return;
            var request=ShortcutRequest.Decode(payload);
            RuntimeDiagnostics.Write("shortcut-launch",details:shortcutOnly?"background":"launcher");
            ShowAction($"正在连接 {request.DeviceName}，打开 {request.Name}…");
            var result=await GetShortcutLauncher().OpenAsync(payload,settings,lifetime.Token);
            RuntimeDiagnostics.Write("shortcut-opened",details:result.Reused?"existing-window":"new-window");
            ShowAction(result.Activated?$"已打开 {request.Name}。":$"{request.Name} 窗口已存在，请从任务栏切回。");
        } catch(OperationCanceledException) { }
        catch(Exception error) {RuntimeDiagnostics.Write("shortcut-launch-failed",error);ShowAction("快捷方式启动失败："+error.Message,true);}
        finally {
            pendingShortcutActivations--;
            // Queue cleanup after this activation task completes; shutdown waits for shortcutTask.
            DispatcherQueue.TryEnqueue(CloseIdleShortcutSession);
        }
    }
    private Task<ShortcutCreation> PrepareShortcutAsync(AppCard card, string directory,bool desktop=false,PhoneDevice? device=null) {
        shortcutWrites.RemoveAll(t=>t.IsCompleted);
        var work=CreateShortcutCoreAsync(card,directory,desktop,device);
        shortcutWrites.Add(ObserveWriteAsync(work));
        return work;
    }
    private static async Task ObserveWriteAsync(Task work) {try {await work;} catch(Exception) { /* UI action reports the error; shutdown only waits for completion. */ }}
    private async Task<ShortcutCreation> CreateShortcutCoreAsync(AppCard card,string directory,bool desktop,PhoneDevice? device) {
        if(device is null || !CanActOnCard(card,device)) throw new IOException("设备或应用已变化，请重新选择。");
        var hardware=device.HardwareId.Length>0?device.HardwareId:await ShortcutRequest.GetHardwareIdAsync(new CommandRunner(),device.Serial,lifetime.Token);
        if(hardware.Length==0) throw new IOException("无法读取手机稳定标识，暂不能创建自动连接快捷方式。请检查 ADB 授权后重试。");
        // Reading the hardware identity may take a while; fail before writing anything if the target moved on.
        if(!CanActOnCard(card,device)) throw new IOException("设备或应用已变化，请重新选择。");
        var request=new ShortcutRequest(device.Serial,hardware,device.Model,card.PackageName,card.Name);
        lifetime.Token.ThrowIfCancellationRequested();
        return await Task.Run(()=>desktop?ShortcutFiles.CreateWithFallback(request,card.IconPath,directory,Path.Combine(ShortcutDataDirectory,"shortcuts")):
            new ShortcutCreation(ShortcutFiles.Create(request,card.IconPath,directory),false));
    }
    private async void CreateShortcut_Click(object sender,RoutedEventArgs args) {
        if((sender as MenuFlyoutItem)?.Tag is not AppCard card) return;
        try {
            var created=await PrepareShortcutAsync(card,Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),desktop:true,device:DevicePicker.SelectedItem as PhoneDevice);
            if(!created.Staged) ShowAction("已创建桌面快捷方式："+Path.GetFileNameWithoutExtension(created.Path));
            else {
                ShowAction("桌面暂不可写，快捷方式已生成。可打开所在文件夹，将它拖到桌面。");
                var reveal=new Button {Content="打开所在文件夹"};
                reveal.Click+=async (_,_)=>{
                    try {
                        await Task.Run(()=>NativeShell.OpenParentAndSelect(created.Path));
                        RuntimeDiagnostics.Write("shortcut-folder-opened",details:created.Path);
                    } catch(Exception error) {RuntimeDiagnostics.Write("shortcut-folder-open-failed",error,created.Path);ShowAction("打开文件夹失败："+error.Message,true);}
                };
                SetStatusAction(reveal);
            }
        } catch(Exception error) {RuntimeDiagnostics.Write("shortcut-create-failed",error);ShowAction("创建快捷方式失败："+error.Message,true);}
    }
}
