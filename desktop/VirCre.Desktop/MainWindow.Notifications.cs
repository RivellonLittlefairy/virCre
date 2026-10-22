using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace VirCre.Desktop;

public sealed partial class MainWindow {
    private string? statusMessage;
    private DispatcherQueueTimer? statusMessageTimer;

    private void ShowAction(string message,bool error=false) {
        if(closing)return;
        // A shortcut-only session has no visible launcher to display failures.
        if(error && shortcutOnly)ShowLauncher();
        statusMessageTimer?.Stop();
        statusMessage=message;
        FooterAction.Content=null;
        FooterAction.Visibility=Visibility.Collapsed;
        NoticeIcon.Glyph=error?"\uEA39":"\uE946";
        RenderFooter();
        if(!error) {
            if(statusMessageTimer==null) {
                statusMessageTimer=DispatcherQueue.CreateTimer();
                statusMessageTimer.IsRepeating=false;
                statusMessageTimer.Interval=TimeSpan.FromSeconds(6);
                statusMessageTimer.Tick+=(_,_)=>ClearStatusMessage();
            }
            statusMessageTimer.Start();
        }
    }
    private void SetStatusAction(Button button) {
        // Keep a recovery action available until it is used, dismissed, or superseded.
        statusMessageTimer?.Stop();
        button.Style=(Style)Application.Current.Resources["ShellQuietButton"];
        button.FontSize=11;
        button.Padding=new Thickness(6,2,6,2);
        button.MinHeight=24;
        FooterAction.Content=button;
        FooterAction.Visibility=Visibility.Visible;
    }
    private void DismissStatusMessage_Click(object sender,RoutedEventArgs args)=>ClearStatusMessage();
    private void ClearStatusMessage() {
        statusMessageTimer?.Stop();
        statusMessage=null;
        FooterAction.Content=null;
        FooterAction.Visibility=Visibility.Collapsed;
        if(!closing)RenderFooter();
    }
    private void RenderFooter() {
        var hasMessage=statusMessage!=null;
        FooterStatus.Text=statusMessage??(windowShutdown.HasStarted
            ?"数据读取已停止，可管理剩余应用窗口后重试关闭。":model.FooterText);
        ToolTipService.SetToolTip(FooterStatus,FooterStatus.Text);
        FooterDot.Visibility=hasMessage?Visibility.Collapsed:Visibility.Visible;
        NoticeIcon.Visibility=DismissStatusMessage.Visibility=hasMessage?Visibility.Visible:Visibility.Collapsed;
    }
}
