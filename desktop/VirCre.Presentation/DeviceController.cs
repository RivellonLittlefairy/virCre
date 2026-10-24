using VirCre.Launcher;

namespace VirCre.Presentation;

/// <summary>
/// Owns device discovery, selection and the scan cancellation rules. Call it from the
/// UI synchronization context: it publishes snapshots through <see cref="Changed"/> on
/// that context, while the catalog performs its own I/O in the background.
/// </summary>
public sealed class DeviceController(IPhoneCatalog phone, CatalogController catalog, ShellViewModel model) : IDisposable {
    private readonly List<Task> pending = [];
    private CancellationTokenSource? scan;
    private int generation;
    private bool disposed;
    private DeviceSelectionState state = new([], null, false);

    public DeviceSelectionState State => state;
    public event Action? Changed;

    /// <summary>Rescans the ADB transport list, restores the previous selection and reloads its apps.</summary>
    public Task RefreshAsync(bool forceRefresh, CancellationToken token) {
        ObjectDisposedException.ThrowIf(disposed, this);
        var work = RefreshCoreAsync(forceRefresh, token);
        pending.RemoveAll(task => task.IsCompleted);
        pending.Add(work);
        return work;
    }

    /// <summary>Selects a device from the last snapshot; cancels any scan still in flight.</summary>
    public Task SelectAsync(PhoneDevice? device, CancellationToken token) {
        ObjectDisposedException.ThrowIf(disposed, this);
        ++generation;
        scan?.Cancel();
        Publish(new(state.Devices, device, false));
        var work = catalog.SelectAsync(device, token);
        pending.RemoveAll(task => task.IsCompleted);
        pending.Add(work);
        return work;
    }

    /// <summary>Stops accepting work, cancels discovery and waits for every accepted request.</summary>
    public async Task StopAsync() {
        Dispose();
        await Task.WhenAll(pending.ToArray());
        await catalog.StopAsync();
    }

    /// <summary>
    /// Drops device and selection state when a catalog read reported a format error, so the
    /// next refresh starts clean. Returns false when the caller should surface its own message.
    /// </summary>
    public bool TryRecoverFormatError(Exception error) {
        if (disposed || !CatalogErrors.IsFormatFailure(error) || !(state.Scanning || model.IsLoading)) return false;
        ++generation;
        scan?.Cancel();
        var reset = catalog.SelectAsync(null, CancellationToken.None);
        if (!reset.IsCompletedSuccessfully) {
            Publish(state with { Scanning = false });
            return false;
        }
        Publish(new([], null, false, error.Message));
        model.ReportDeviceError(error.Message);
        return true;
    }

    private async Task RefreshCoreAsync(bool forceRefresh, CancellationToken token) {
        if (disposed) return;
        var ticket = ++generation;
        scan?.Cancel();
        using var request = CancellationTokenSource.CreateLinkedTokenSource(token);
        scan = request;
        var previous = state.Selected;
        Publish(state with { Scanning = true, Error = null });
        catalog.CancelActive();
        try {
            var found = await phone.GetDevicesAsync(request.Token);
            if (!IsCurrent(ticket, request)) return;
            var selected = RestoreSelection(found, previous);
            Publish(new(found, selected, false));
            if (selected?.State != DeviceState.Online && previous != null) {
                model.ReportDeviceError((selected?.State == DeviceState.Unauthorized
                    ? "请在手机上允许调试授权后刷新。" : "手机连接未确认，请检查连接后刷新。") + "仍显示上次列表，暂不能启动应用。", preserveCatalog: true);
            } else await catalog.SelectAsync(selected, request.Token, forceRefresh);
        } catch (OperationCanceledException) {
        } catch (Exception error) when (CatalogErrors.IsExpected(error)) {
            if (IsCurrent(ticket, request)) {
                var message = "设备读取失败，仍显示上次列表，暂不能启动应用。" + error.Message;
                Publish(new([], null, false, message));
                model.ReportDeviceError(message, preserveCatalog: true);
            }
        } finally {
            if (ReferenceEquals(scan, request)) scan = null;
            if (IsCurrent(ticket, request) && state.Scanning) Publish(state with { Scanning = false });
        }
    }

    /// <summary>Hardware identity first, then any transport of the same phone, then a lone device.</summary>
    private static PhoneDevice? RestoreSelection(IReadOnlyList<PhoneDevice> devices, PhoneDevice? previous) {
        PhoneDevice? selected = previous is {HardwareId.Length:>0}
            ? devices.FirstOrDefault(device => device.HardwareId == previous.HardwareId) : null;
        selected ??= devices.FirstOrDefault(device => previous != null && device.HasConnection(previous.Serial));
        return selected ?? (devices.Count == 1 ? devices[0] : null);
    }

    private bool IsCurrent(int ticket, CancellationTokenSource request) =>
        !disposed && ticket == generation && !request.IsCancellationRequested;

    private void Publish(DeviceSelectionState next) {
        if (disposed) return;
        state = next;
        Changed?.Invoke();
    }

    public void Dispose() {
        if (disposed) return;
        disposed = true;
        ++generation;
        scan?.Cancel();
        scan = null;
    }
}
