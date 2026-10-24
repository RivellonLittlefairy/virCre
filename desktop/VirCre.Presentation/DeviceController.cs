using VirCre.Launcher;

namespace VirCre.Presentation;

/// <summary>
/// Owns device discovery, selection and the scan cancellation rules. Snapshots reach
/// <see cref="Changed"/> through the model's dispatcher, so the catalog may perform its
/// I/O on any thread; the host still chooses where the controller itself is driven from.
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
        // Direct selections may use a caller token that shutdown cannot cancel.
        // Stop the catalog now, then wait for both discovery and app-reader cleanup.
        var catalogStop = catalog.StopAsync();
        await Task.WhenAll(pending.Append(catalogStop).ToArray());
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
            PublishIfCurrent(ticket, request, new(found, selected, false));
            if (selected?.State != DeviceState.Online && previous != null) {
                model.ReportDeviceError(model.Text[selected?.State == DeviceState.Unauthorized
                    ? TextKeys.ScanReauthorize : TextKeys.ScanUnconfirmed] + model.Text[TextKeys.ScanKeepsPrevious], preserveCatalog: true);
            } else await catalog.SelectAsync(selected, request.Token, forceRefresh);
        } catch (OperationCanceledException) {
        } catch (Exception error) when (CatalogErrors.IsExpected(error)) {
            if (IsCurrent(ticket, request)) {
                var message = model.Text.Format(TextKeys.ScanFailed, error.Message);
                PublishIfCurrent(ticket, request, new([], null, false, message));
                model.ReportDeviceError(message, preserveCatalog: true);
            }
        } finally {
            if (ReferenceEquals(scan, request)) scan = null;
            model.Dispatcher.Post(() => { if (IsCurrent(ticket, request) && state.Scanning) PublishNow(state with { Scanning = false }); });
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

    /// <summary>State changes reach <see cref="Changed"/> on the host's UI thread, never on a worker.</summary>
    private void Publish(DeviceSelectionState next) => model.Dispatcher.Post(() => PublishNow(next));

    /// <summary>Rechecks the ticket on the UI thread so a cancellation that landed while queued still wins.</summary>
    private void PublishIfCurrent(int ticket, CancellationTokenSource request, DeviceSelectionState next) =>
        model.Dispatcher.Post(() => { if (IsCurrent(ticket, request)) PublishNow(next); });

    private void PublishNow(DeviceSelectionState next) {
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
