using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using VirCre.Launcher;
using VirCre.Presentation;
using Windows.Foundation;

namespace VirCre.Desktop;

public sealed partial class MainWindow {
    private sealed class DragGesture(UIElement source,uint pointer,Point origin,PhoneDevice? device,object ticket) {
        public UIElement Source {get;}=source;
        public uint Pointer {get;}=pointer;
        public Point Origin {get;}=origin;
        public PhoneDevice? Device {get;}=device;
        public bool Preparing {get;set;}
        public object Ticket {get;}=ticket;
    }
    private readonly GestureOwnership dragOwnership=new();
    private DragGesture? dragGesture;
    private readonly Dictionary<string,Task<ShortcutCreation>> dragFiles=[];
    private Task nativeDragTask=Task.CompletedTask;
    private Dictionary<RoutedEvent,PointerEventHandler>? cardPointerHandlers;
    private Dictionary<RoutedEvent,PointerEventHandler> CardPointerHandlers => cardPointerHandlers??=new() {
        [UIElement.PointerPressedEvent]=AppCard_PointerPressed,
        [UIElement.PointerMovedEvent]=AppCard_PointerMoved,
        [UIElement.PointerReleasedEvent]=AppCard_PointerReleased,
        [UIElement.PointerCanceledEvent]=AppCard_PointerReleased,
        [UIElement.PointerCaptureLostEvent]=AppCard_PointerCaptureLost
    };

    private void AppCard_Loaded(object sender,RoutedEventArgs args) {
        if(sender is not UIElement source)return;
        // Button handles pointer events itself. Observe them without stealing its
        // capture until movement has actually crossed the shortcut drag threshold.
        foreach(var (routedEvent,handler) in CardPointerHandlers)source.AddHandler(routedEvent,handler,true);
    }
    private void AppCard_Unloaded(object sender,RoutedEventArgs args) {
        if(sender is not UIElement source)return;
        foreach(var (routedEvent,handler) in CardPointerHandlers)source.RemoveHandler(routedEvent,handler);
        if(dragGesture is { } gesture && ReferenceEquals(gesture.Source,source))
            dragOwnership.Release(gesture.Ticket,()=>{dragGesture=null;source.ReleasePointerCaptures();});
    }

    private void AppCard_PointerPressed(object sender,PointerRoutedEventArgs args) {
        if(closing || windowShutdown.HasStarted || sender is not FrameworkElement source || source.Tag is not AppCard || !args.GetCurrentPoint(source).Properties.IsLeftButtonPressed) return;
        // Touch keeps the list's native scrolling gesture; shortcut dragging is a mouse action.
        if(args.Pointer.PointerDeviceType==Microsoft.UI.Input.PointerDeviceType.Touch)return;
        for(var current=args.OriginalSource as DependencyObject;current!=null && !ReferenceEquals(current,sender);current=VisualTreeHelper.GetParent(current))
            if(current is ButtonBase button && button.Tag is not AppCard) return;
        dragGesture=new(source,args.Pointer.PointerId,args.GetCurrentPoint(source).Position,DevicePicker.SelectedItem as PhoneDevice,dragOwnership.Begin());
    }
    private void AppCard_PointerReleased(object sender,PointerRoutedEventArgs args) {
        if(dragGesture?.Pointer!=args.Pointer.PointerId) return;
        var gesture=dragGesture;
        if(gesture!=null) dragOwnership.Release(gesture.Ticket,()=>{dragGesture=null;(sender as UIElement)?.ReleasePointerCaptures();});
    }
    private void AppCard_PointerCaptureLost(object sender,PointerRoutedEventArgs args) {
        // Taking capture from the opening button cancels its click, but the
        // shortcut gesture remains owned until release/cancel or native drag.
        if(dragGesture?.Preparing!=true)AppCard_PointerReleased(sender,args);
    }
    private async void AppCard_PointerMoved(object sender,PointerRoutedEventArgs args) {
        if(closing || windowShutdown.HasStarted)return;
        if(sender is not FrameworkElement source || source.Tag is not AppCard card || dragGesture is not {Preparing:false} gesture || gesture.Pointer!=args.Pointer.PointerId) return;
        var point=args.GetCurrentPoint(source);
        if(!point.Properties.IsLeftButtonPressed) {AppCard_PointerReleased(sender,args);return;}
        if(Math.Abs(point.Position.X-gesture.Origin.X)<8 && Math.Abs(point.Position.Y-gesture.Origin.Y)<8) return;
        gesture.Preparing=true;args.Handled=true;
        source.CapturePointer(args.Pointer);
        try {
            var key=card.SourceSerial+"\n"+card.PackageName+"\n"+card.IconPath;
            if(dragFiles.TryGetValue(key,out var cached) && (cached.IsFaulted || cached.IsCanceled || cached.IsCompletedSuccessfully && !File.Exists(cached.Result.Path))) dragFiles.Remove(key);
            if(!dragFiles.TryGetValue(key,out var preparation)) {
                if(dragFiles.Count>=64) dragFiles.Clear();
                var staging=Path.Combine(ShortcutDataDirectory,"drag");
                CacheMaintenance.Prune(staging,29,TimeSpan.FromDays(7));
                preparation=PrepareShortcutAsync(card,Path.Combine(staging,Guid.NewGuid().ToString("N")),device:gesture.Device);
                dragFiles[key]=preparation;
                ShowAction("正在准备快捷方式，请保持鼠标按下…");
            }
            var created=await preparation;
            if(closing || windowShutdown.HasStarted) return;
            if(!dragOwnership.IsCurrent(gesture.Ticket)) {ShowAction("快捷方式已准备好，请再拖动一次应用卡片。");return;}
            if(!CanActOnCard(card,gesture.Device)) throw new IOException("设备或应用已经变化，请重新选择。");
            dragOwnership.Release(gesture.Ticket,()=>{dragGesture=null;source.ReleasePointerCaptures();});
            var finished=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            nativeDragTask=finished.Task;
            try {
                var copied=NativeFileDrag.Start(created.Path,()=>closing||lifetime.IsCancellationRequested);
                if(!closing) ShowAction(copied?"快捷方式已复制到目标位置。":"拖动已取消。");
            } finally {finished.SetResult();}
        } catch(Exception error) {RuntimeDiagnostics.Write("shortcut-drag-failed",error);ShowAction("拖出快捷方式失败："+error.Message,true);}
        finally {
            dragOwnership.Release(gesture.Ticket,()=>{dragGesture=null;source.ReleasePointerCaptures();});
        }
    }
}
