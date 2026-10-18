using System.Diagnostics;
using System.Runtime.InteropServices;
using VirCam.Core;

namespace VirCam.Desktop;

internal sealed class MainForm : Form {
    private sealed record Source(string? Path,Rectangle Region);
    private sealed record CaptureContext(Rectangle Controller,IntPtr Mirror);
    private sealed record Device(string Serial,string Label) {public override string ToString()=>Label;}
    private readonly string root,powershell;
    private readonly ComboBox devices=new(){DropDownStyle=ComboBoxStyle.DropDownList,Width=430};
    private readonly CheckBox install=new(){Text="更新测试 APK（首次或代码更新后勾选）",Checked=true,AutoSize=true};
    private readonly Button connect=new(){Text="启动手机会话",AutoSize=true};
    private readonly Button begin=new(){Text="开始输入",Enabled=false,AutoSize=true};
    private readonly Button pause=new(){Text="暂停输入",Enabled=false,AutoSize=true};
    private readonly Button stop=new(){Text="停止会话",Enabled=false,AutoSize=true};
    private readonly Label status=new(){Text="先刷新设备并明确选择一台手机。",AutoSize=false,Width=650,Height=54};
    private readonly Label sourceLabel=new(){Text="尚未选择输入源",AutoSize=true,MaximumSize=new Size(650,0)};
    private readonly PictureBox preview=new(){Width=650,Height=340,BackColor=Color.FromArgb(24,44,35),SizeMode=PictureBoxSizeMode.Zoom};
    private readonly System.Windows.Forms.Timer timer=new(){Interval=250};
    private Source? selected;
    private Source? active;
    private readonly InputRevision revision=new();
    private string? lastLogDirectory;
    private CaptureContext captureContext=new(Rectangle.Empty,IntPtr.Zero);
    private Bitmap? nextPreview;
    private DesktopSession? session;
    private FrameClient? client;
    private CancellationTokenSource? cancellation;
    private Task? pump;
    private string pendingStatus="先刷新设备并明确选择一台手机。";
    private bool stopping,allowClose;

    public MainForm(string root,string powershell) {
        this.root=root;this.powershell=powershell;
        Text="VirCam · PC 画面输入";ClientSize=new Size(710,730);MinimumSize=new Size(700,600);
        StartPosition=FormStartPosition.CenterScreen;Font=new Font("Microsoft YaHei UI",10);
        var layout=new FlowLayoutPanel {Dock=DockStyle.Fill,FlowDirection=FlowDirection.TopDown,WrapContents=false,AutoScroll=true,Padding=new Padding(18)};
        Controls.Add(layout);
        layout.Controls.Add(new Label{Text="VirCam · 图片 / 固定桌面区域",AutoSize=true,Font=new Font(Font.FontFamily,15,FontStyle.Bold)});
        var deviceRow=Row();deviceRow.Controls.Add(devices);
        var refresh=new Button{Text="刷新设备",AutoSize=true};refresh.Click+=async(_,_)=>await RefreshDevicesAsync();deviceRow.Controls.Add(refresh);layout.Controls.Add(deviceRow);
        layout.Controls.Add(install);
        var sessionRow=Row();sessionRow.Controls.Add(connect);sessionRow.Controls.Add(stop);layout.Controls.Add(sessionRow);
        connect.Click+=async(_,_)=>await ConnectAsync();stop.Click+=async(_,_)=>await StopSessionAsync();
        layout.Controls.Add(status);
        var sources=Row();var image=new Button{Text="选择图片",AutoSize=true};var region=new Button{Text="框选桌面区域",AutoSize=true};
        sources.Controls.Add(image);sources.Controls.Add(region);layout.Controls.Add(sources);
        image.Click+=(_,_)=>SelectImage();region.Click+=(_,_)=>SelectRegion();
        layout.Controls.Add(sourceLabel);layout.Controls.Add(preview);
        var actions=Row();actions.Controls.Add(begin);actions.Controls.Add(pause);layout.Controls.Add(actions);
        begin.Click+=(_,_)=>{
            if(selected==null) { SetStatus("请先选择图片或框选区域。");return; }
            ChangeInput(selected,"电脑画面输入中");
        };
        pause.Click+=(_,_)=>ChangeInput(null,"已暂停，手机将显示暂停占位图。");
        var logs=new Button{Text="打开本次日志",AutoSize=true};actions.Controls.Add(logs);
        logs.Click+=(_,_)=>{var folder=session?.LogDirectory??lastLogDirectory;if(folder!=null && Directory.Exists(folder)) Process.Start(new ProcessStartInfo("explorer.exe",folder){UseShellExecute=true});};
        layout.Controls.Add(new Label{Text="固定区域采集屏幕当前可见内容。请把控制窗口和投屏窗口移出采集区。\n首版不读取被遮住的窗口背后内容；单次手机会话最长 10 分钟。",AutoSize=true,MaximumSize=new Size(650,0)});
        timer.Tick+=async(_,_)=>{
            captureContext=new(Visible?Bounds:Rectangle.Empty,session?.MirrorHandle()??IntPtr.Zero);
            status.Text=Volatile.Read(ref pendingStatus);
            var bitmap=Interlocked.Exchange(ref nextPreview,null);
            if(bitmap!=null) {var old=preview.Image;preview.Image=bitmap;old?.Dispose();}
            if(session!=null && session.HasExited && client!=null && !stopping) await StopSessionAsync("手机会话已结束；需要用户重新启动。");
        };
        timer.Start();Shown+=async(_,_)=>await RefreshDevicesAsync();
        FormClosing+=async(_,e)=>{
            if(allowClose || session==null) return;
            e.Cancel=true;
            await StopSessionAsync();
            if(session==null) {allowClose=true;Close();}
        };
        FormClosed+=(_,_)=>{timer.Dispose();Interlocked.Exchange(ref nextPreview,null)?.Dispose();preview.Image?.Dispose();};
    }
    private static FlowLayoutPanel Row()=>new(){AutoSize=true,WrapContents=false,Margin=new Padding(0,6,0,6)};
    private void SetStatus(string text)=>Volatile.Write(ref pendingStatus,text);
    private async Task RefreshDevicesAsync() {
        try {
            var process=new Process {StartInfo=new ProcessStartInfo("adb"){UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true}};
            process.StartInfo.ArgumentList.Add("devices");process.StartInfo.ArgumentList.Add("-l");process.Start();
            using(process) {
                var output=process.StandardOutput.ReadToEndAsync();var errors=process.StandardError.ReadToEndAsync();
                using var timeout=new CancellationTokenSource(10000);
                try {await process.WaitForExitAsync(timeout.Token);} catch {process.Kill(true);throw;}
                if(process.ExitCode!=0) throw new IOException(await errors);
                var keep=(devices.SelectedItem as Device)?.Serial;devices.Items.Clear();
                foreach(var line in (await output).Split('\n')) {
                    var parts=line.Split((char[]?)null,StringSplitOptions.RemoveEmptyEntries);
                    if(parts.Length<2 || parts[1]!="device") continue;
                    var model=parts.FirstOrDefault(p=>p.StartsWith("model:"))?[6..]??"Android";
                    var value=new Device(parts[0],model+" · "+parts[0]);devices.Items.Add(value);
                    if(value.Serial==keep) devices.SelectedItem=value;
                }
                if(devices.Items.Count==1) devices.SelectedIndex=0;
                SetStatus(devices.Items.Count>0?"设备已列出。启动会话后，请确认手机安装与虚拟相机授权。":"没有已授权的 ADB 设备。");
            }
        } catch(Exception e) {SetStatus("设备检查失败："+e.Message);}
    }
    private async Task ConnectAsync() {
        if(devices.SelectedItem is not Device device) {SetStatus("请明确选择设备。");return;}
        connect.Enabled=false;stop.Enabled=true;
        cancellation=new();session=new(root,powershell);SetStatus("正在启动。请查看手机安装确认和置顶投屏中的摄像头授权。");
        try {
            var info=await session.StartAsync(device.Serial,install.Checked,cancellation.Token);
            client=new(info);await client.ConnectAsync(cancellation.Token);
            begin.Enabled=true;pause.Enabled=true;
            SetStatus("会话已连接。确认投屏相机授权后，选择输入源并点击“开始输入”。");
            pump=Task.Run(()=>PumpAsync(cancellation.Token));
        } catch(Exception e) {await StopSessionAsync("启动失败："+e.Message);}
    }
    private async Task PumpAsync(CancellationToken token) {
        try {
            while(!token.IsCancellationRequested) {
                var epoch=revision.Current;
                var source=Volatile.Read(ref active);
                if(source==null) {await client!.PauseAsync(token);await Task.Delay(500,token);continue;}
                try {
                    var context=Volatile.Read(ref captureContext);
                    using var image=source.Path!=null?DesktopCapture.FileImage(source.Path):DesktopCapture.Region(source.Region,context.Controller,context.Mirror);
                    var png=DesktopCapture.Png(image);
                    if(!revision.IsCurrent(epoch)) {await client!.PauseAsync(token);continue;}
                    var sequence=await client!.SendFrameAsync(png,DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),token);
                    if(!revision.TryCommit(epoch,()=>{
                        var old=Interlocked.Exchange(ref nextPreview,new Bitmap(image));old?.Dispose();
                        SetStatus("电脑画面输入中 · 已确认帧序号 "+sequence);
                    })) {await client.PauseAsync(token);continue;}
                } catch(Exception e) when(e is InvalidOperationException || e is ArgumentException || e is ExternalException) {
                    revision.TryCommit(epoch,()=>{Volatile.Write(ref active,null);SetStatus(e.Message);});
                    await client!.PauseAsync(token);
                    BeginInvoke(()=>revision.TryCommit(epoch,ClearPreview));
                }
                await Task.Delay(200,token);
            }
        } catch(OperationCanceledException) when(token.IsCancellationRequested) { }
        catch(Exception e) {
            Volatile.Write(ref active,null);SetStatus("输入连接已断开，不会自动恢复："+e.Message);
            if(!IsDisposed) BeginInvoke(()=>{begin.Enabled=false;pause.Enabled=false;ClearPreview();});
        }
    }
    private void SelectImage() {
        using var picker=new OpenFileDialog{Filter="图片|*.png;*.jpg;*.jpeg;*.bmp"};
        if(picker.ShowDialog(this)!=DialogResult.OK) return;
        selected=new(picker.FileName,Rectangle.Empty);ChangeInput(null,"图片已选择，点击开始输入后发送。");
        sourceLabel.Text="图片："+Path.GetFileName(picker.FileName);
    }
    private void SelectRegion() {
        ChangeInput(null,"正在选择区域，输入已暂停。");Hide();
        using var selector=new RegionSelector();var result=selector.ShowDialog();Show();Activate();
        if(result!=DialogResult.OK) return;
        selected=new(null,selector.Selected);var r=selector.Selected;
        sourceLabel.Text=$"固定区域：X={r.X}, Y={r.Y}, W={r.Width}, H={r.Height}（物理像素）";
        ClearPreview();SetStatus("区域已选择。请移开投屏和控制窗口，再开始输入。");
    }
    private void ClearPreview() {Interlocked.Exchange(ref nextPreview,null)?.Dispose();var old=preview.Image;preview.Image=null;old?.Dispose();}
    private void ChangeInput(Source? value,string message) {revision.Change(()=>{Volatile.Write(ref active,value);ClearPreview();SetStatus(message);});}
    private async Task StopSessionAsync(string message="已停止并请求释放本次实验资源。") {
        if(stopping) return;stopping=true;begin.Enabled=false;pause.Enabled=false;stop.Enabled=false;
        ChangeInput(null,"正在停止并清理资源…");cancellation?.Cancel();
        try {
            if(pump!=null) await pump;client?.Dispose();client=null;pump=null;
            if(session!=null) {lastLogDirectory=session.LogDirectory;await session.StopAsync();}session=null;
            cancellation?.Dispose();cancellation=null;ClearPreview();SetStatus(message);connect.Enabled=true;
        } catch(Exception e) {
            lastLogDirectory=session?.LogDirectory??lastLogDirectory;
            SetStatus("停止或清理失败，请查看日志："+e.Message);
            if(session?.HasExited==true) {session=null;connect.Enabled=true;} else stop.Enabled=true;
        }
        finally {stopping=false;}
    }
}
