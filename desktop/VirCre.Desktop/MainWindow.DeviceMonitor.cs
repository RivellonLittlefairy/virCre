using VirCre.Launcher;

namespace VirCre.Desktop;

public sealed partial class MainWindow {
    private Task deviceMonitorTask=Task.CompletedTask;
    private Task automaticDeviceRefreshTask=Task.CompletedTask;
    private Task manualDeviceRefreshTask=Task.CompletedTask;
    private bool deviceRefreshPending;

    private void StartDeviceMonitor() {
        deviceMonitorTask=Task.Run(()=>new AdbDeviceMonitor().RunAsync(
            ()=>DispatcherQueue.TryEnqueue(QueueConnectionRefresh),
            error=>RuntimeDiagnostics.Write("device-monitor-retry",error),lifetime.Token));
    }

    private void QueueConnectionRefresh() {
        if(closing || windowShutdown.HasStarted)return;
        deviceRefreshPending=true;
        if(automaticDeviceRefreshTask.IsCompleted)automaticDeviceRefreshTask=RefreshConnectionsWhenReadyAsync();
    }

    private async Task RefreshConnectionsWhenReadyAsync() {
        try {
            while(deviceRefreshPending && !closing && !windowShutdown.HasStarted) {
                // USB and wireless transports can announce several changes together.
                await Task.Delay(300,lifetime.Token);
                while(!manualDeviceRefreshTask.IsCompleted)await manualDeviceRefreshTask;
                if(closing || windowShutdown.HasStarted)return;
                lifetime.Token.ThrowIfCancellationRequested();
                deviceRefreshPending=false;
                await devices.RefreshConnectionsAsync(lifetime.Token);
            }
        } catch(OperationCanceledException) when(lifetime.IsCancellationRequested) { }
        catch(Exception error) {
            RuntimeDiagnostics.Write("device-auto-refresh-failed",error);
            if(!closing && !windowShutdown.HasStarted)ShowAction("自动更新设备状态失败，请点击刷新重试。",true);
        }
    }
}
