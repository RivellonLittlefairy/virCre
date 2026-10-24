using VirCre.Launcher;

namespace VirCre.Presentation;

/// <summary>Each selection owns its cancellation source and may publish only while still current.</summary>
public sealed class CatalogController(IPhoneCatalog phone, ShellViewModel model) : IDisposable {
    private CancellationTokenSource? active;
    private int generation;
    private bool disposed;
    private readonly List<Task> pending = [];

    public void CancelActive() {
        ObjectDisposedException.ThrowIf(disposed, this);
        generation++;
        active?.Cancel();
    }

    public Task SelectAsync(PhoneDevice? device, CancellationToken token = default, bool forceRefresh = false) {
        ObjectDisposedException.ThrowIf(disposed, this);
        pending.RemoveAll(task => task.IsCompleted);
        var work = SelectCoreAsync(device, token, forceRefresh);
        pending.Add(work);
        return work;
    }
    private async Task SelectCoreAsync(PhoneDevice? device, CancellationToken token, bool forceRefresh) {
        var ticket = ++generation;
        active?.Cancel();
        using var request = CancellationTokenSource.CreateLinkedTokenSource(token);
        active = request;
        model.BeginLoad(device, () => IsCurrent(ticket));
        if (device?.State != DeviceState.Online) { active = null; return; }
        try {
            var apps = await phone.GetAppsAsync(device.Serial, request.Token, forceRefresh);
            if (IsCurrent(ticket)) {
                request.Token.ThrowIfCancellationRequested();
                // Ownership may change while this result waits in the host's UI queue.
                model.CompleteLoad(apps, () => IsCurrent(ticket) && !request.IsCancellationRequested && !token.IsCancellationRequested);
            }
        } catch (OperationCanceledException) {
            if (IsCurrent(ticket)) model.FailLoad(model.Text[TextKeys.LoadCancelled], () => IsCurrent(ticket));
        } catch (Exception error) when (CatalogErrors.IsExpected(error)) {
            if (IsCurrent(ticket)) model.FailLoad(error.Message, () => IsCurrent(ticket));
        } finally {
            if (ReferenceEquals(active, request)) active = null;
        }
    }
    private bool IsCurrent(int ticket) => !disposed && ticket == generation;
    public void Dispose() {
        disposed = true;
        generation++;
        active?.Cancel();
        active = null;
    }
    public async Task StopAsync() {
        Dispose();
        await Task.WhenAll(pending.ToArray());
    }
}
