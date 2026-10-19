using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using VirCre.Launcher;
using VirCre.Presentation;
using Windows.Foundation;

namespace VirCre.Desktop;

public sealed partial class MainWindow {
    private sealed class DragGesture(uint pointer,Point origin,object ticket) {
        public uint Pointer {get;}=pointer;
        public Point Origin {get;}=origin;
        public bool Preparing {get;set;}
        public object Ticket {get;}=ticket;
    }
    private readonly GestureOwnership dragOwnership=new();
    private DragGesture? dragGesture;
    private readonly Dictionary<string,Task<ShortcutCreation>> dragFiles=[];
    private Task nativeDragTask=Task.CompletedTask;

    private void AppCard_PointerPressed(object sender,PointerRoutedEventArgs args) {
        if(closing || sender is not FrameworkElement source || source.Tag is not AppCard || !args.GetCurrentPoint(source).Properties.IsLeftButtonPressed) return;
        for(var current=args.OriginalSource as DependencyObject;current!=null && !ReferenceEquals(current,sender);current=VisualTreeHelper.GetParent(current))
            if(current is Button) return;
        dragGesture=new(args.Pointer.PointerId,args.GetCurrentPoint(source).Position,dragOwnership.Begin());
        source.CapturePointer(args.Pointer);
    }
    private void AppCard_PointerReleased(object sender,PointerRoutedEventArgs args) {
        if(dragGesture?.Pointer!=args.Pointer.PointerId) return;
        var gesture=dragGesture;
        if(gesture!=null) dragOwnership.Release(gesture.Ticket,()=>{dragGesture=null;(sender as UIElement)?.ReleasePointerCaptures();});
    }
    private async void AppCard_PointerMoved(object sender,PointerRoutedEventArgs args) {
        if(sender is not FrameworkElement source || source.Tag is not AppCard card || dragGesture is not {Preparing:false} gesture || gesture.Pointer!=args.Pointer.PointerId) return;
        var point=args.GetCurrentPoint(source);
        if(!point.Properties.IsLeftButtonPressed) {AppCard_PointerReleased(sender,args);return;}
        if(Math.Abs(point.Position.X-gesture.Origin.X)<8 && Math.Abs(point.Position.Y-gesture.Origin.Y)<8) return;
        gesture.Preparing=true;args.Handled=true;
        try {
            var key=card.SourceSerial+"\n"+card.PackageName+"\n"+card.IconPath;
            if(dragFiles.TryGetValue(key,out var cached) && (cached.IsFaulted || cached.IsCanceled || cached.IsCompletedSuccessfully && !File.Exists(cached.Result.Path))) dragFiles.Remove(key);
            if(!dragFiles.TryGetValue(key,out var preparation)) {
                if(dragFiles.Count>=64) dragFiles.Clear();
                var staging=Path.Combine(ShortcutDataDirectory,"drag");
                CacheMaintenance.Prune(staging,29,TimeSpan.FromDays(7));
                preparation=PrepareShortcutAsync(card,Path.Combine(staging,Guid.NewGuid().ToString("N")));
                dragFiles[key]=preparation;
                ShowAction("正在准备快捷方式，请保持鼠标按下…");
            }
            var created=await preparation;
            if(closing) return;
            if(!dragOwnership.IsCurrent(gesture.Ticket)) {ShowAction("快捷方式已准备好，请再拖动一次应用卡片。");return;}
            if(model.CurrentSerial!=card.SourceSerial || model.IsLoading || scanning) throw new IOException("设备或应用已经变化，请重新选择。");
            dragOwnership.Release(gesture.Ticket,()=>{dragGesture=null;source.ReleasePointerCaptures();});
            var finished=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            nativeDragTask=finished.Task;
            try {
                var copied=NativeFileDrag.Start(created.Path,()=>closing||lifetime.IsCancellationRequested);
                if(!closing) ShowAction(copied?"快捷方式已复制到目标位置。":"拖动已取消。");
            } finally {finished.SetResult();}
        } catch(Exception error) {RuntimeDiagnostics.Write("shortcut-drag-failed",error);ShowAction("拖出快捷方式失败："+error.Message,true);}
        finally {
            dragOwnership.Release(gesture.Ticket,()=>{dragGesture=null;if(!closing)source.ReleasePointerCaptures();});
        }
    }
}
