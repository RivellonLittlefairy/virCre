using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using VirCre.Launcher;
using VirCre.Presentation;

namespace VirCre.Desktop;

public sealed partial class MainWindow {
    private Task shortcutTask=Task.CompletedTask;
    private readonly List<Task> shortcutWrites=[];
    public void ActivateShortcut(string? payload) {
        if(windowShutdown.HasStarted) {
            if(!closing) {Activate();ShowAction("正在结束会话，请先关闭剩余应用窗口。",true);}
            return;
        }
        Activate();
        if(payload!=null) shortcutTask=OpenShortcutAfterAsync(shortcutTask,payload);
    }
    private async Task OpenShortcutAfterAsync(Task previous, string payload) {
        await previous;
        if(closing || windowShutdown.HasStarted) return;
        try {
            var request=ShortcutRequest.Decode(payload);
            ShowAction($"正在连接 {request.DeviceName}，打开 {request.Name}…");
            var runner=new CommandRunner();
            var directory=new PhoneCatalog(runner);
            var devices=await directory.GetDevicesAsync(lifetime.Token);
            var device=await request.ResolveAsync(devices,(s,t)=>ShortcutRequest.GetHardwareIdAsync(runner,s,t),lifetime.Token);
            if(!await directory.CanLaunchAsync(device.Serial,request.PackageName,lifetime.Token))
                throw new IOException("手机中已找不到此应用的启动入口，请刷新列表并重新创建快捷方式。");
            var result=await appWindows.OpenAsync(CreateLaunchRequest(device.Serial,device.Model,request.PackageName,request.Name),lifetime.Token);
            ShowAction(result.Activated?$"已打开 {request.Name}。":$"{request.Name} 窗口已存在，请从任务栏切回。");
        } catch(OperationCanceledException) { }
        catch(Exception error) {ShowAction("快捷方式启动失败："+error.Message,true);}
    }
    private Task<ShortcutCreation> PrepareShortcutAsync(AppCard card, string directory,bool desktop=false) {
        shortcutWrites.RemoveAll(t=>t.IsCompleted);
        var work=CreateShortcutCoreAsync(card,directory,desktop);
        shortcutWrites.Add(ObserveWriteAsync(work));
        return work;
    }
    private static async Task ObserveWriteAsync(Task work) {try {await work;} catch(Exception) { /* UI action reports the error; shutdown only waits for completion. */ }}
    private async Task<ShortcutCreation> CreateShortcutCoreAsync(AppCard card,string directory,bool desktop) {
        if(closing || windowShutdown.HasStarted || scanning || model.IsLoading || DevicePicker.SelectedItem is not PhoneDevice {State:DeviceState.Online} device ||
            card.SourceSerial!=device.Serial || model.CurrentSerial!=device.Serial || !model.Apps.Any(a=>a.PackageName==card.PackageName)) throw new IOException("设备或应用已变化，请重新选择。");
        var hardware=device.HardwareId.Length>0?device.HardwareId:await ShortcutRequest.GetHardwareIdAsync(new CommandRunner(),device.Serial,lifetime.Token);
        if(hardware.Length==0) throw new IOException("无法读取手机稳定标识，暂不能创建自动连接快捷方式。请检查 ADB 授权后重试。");
        var request=new ShortcutRequest(device.Serial,hardware,device.Model,card.PackageName,card.Name);
        lifetime.Token.ThrowIfCancellationRequested();
        return await Task.Run(()=>desktop?ShortcutFiles.CreateWithFallback(request,card.IconPath,directory,Path.Combine(ShortcutDataDirectory,"shortcuts")):
            new ShortcutCreation(ShortcutFiles.Create(request,card.IconPath,directory),false));
    }
    private async void CreateShortcut_Click(object sender,RoutedEventArgs args) {
        if((sender as MenuFlyoutItem)?.Tag is not AppCard card) return;
        try {
            var created=await PrepareShortcutAsync(card,Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),desktop:true);
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
                ActionNotice.ActionButton=reveal;
            }
        } catch(Exception error) {RuntimeDiagnostics.Write("shortcut-create-failed",error);ShowAction("创建快捷方式失败："+error.Message,true);}
    }
}
