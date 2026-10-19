namespace VirCre.Presentation;

/// <summary>A late asynchronous callback may release only the pointer gesture that it started.</summary>
public sealed class GestureOwnership {
    private object? current;
    public object Begin()=>current=new object();
    public bool IsCurrent(object ticket)=>ReferenceEquals(current,ticket);
    public void Release(object ticket,Action release) {if(!IsCurrent(ticket))return;current=null;release();}
}
