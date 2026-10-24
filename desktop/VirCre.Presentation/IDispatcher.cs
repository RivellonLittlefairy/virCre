namespace VirCre.Presentation;

/// <summary>
/// Marshals presentation notifications onto the thread that owns the UI. Hosts supply their
/// toolkit's dispatcher; work posted from that thread runs inline so call order is preserved.
/// </summary>
public interface IDispatcher {
    /// <summary>True when the caller is already on the UI thread.</summary>
    bool HasAccess {get;}
    /// <summary>Runs <paramref name="work"/> now on the UI thread, or queues it when called elsewhere.</summary>
    void Post(Action work);
}

/// <summary>Runs every callback inline; used by tests and headless hosts that own their thread.</summary>
public sealed class ImmediateDispatcher : IDispatcher {
    public static ImmediateDispatcher Instance {get;}=new();
    private ImmediateDispatcher(){}
    public bool HasAccess=>true;
    public void Post(Action work)=>work();
}
