using Microsoft.UI.Dispatching;
using VirCre.Presentation;

namespace VirCre.Desktop;

/// <summary>Routes presentation state onto the window's dispatcher queue.</summary>
internal sealed class WindowDispatcher(DispatcherQueue queue) : IDispatcher {
    public bool HasAccess => queue.HasThreadAccess;
    public void Post(Action work) {
        if(queue.HasThreadAccess) work();
        else queue.TryEnqueue(() => work());
    }
}
