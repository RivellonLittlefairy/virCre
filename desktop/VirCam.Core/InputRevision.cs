namespace VirCam.Core;

/// <summary>Atomically invalidates work captured before a pause or source change.</summary>
public sealed class InputRevision {
    private readonly object gate=new();
    private long revision;
    public long Current {get {lock(gate) return revision;}}
    public void Change(Action apply) {lock(gate) {revision++;apply();}}
    public bool IsCurrent(long value) {lock(gate) return revision==value;}
    public bool TryCommit(long value,Action publish) {lock(gate) {if(revision!=value)return false;publish();return true;}}
}
