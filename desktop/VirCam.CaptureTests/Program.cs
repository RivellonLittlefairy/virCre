using VirCam.Core;
using VirCam.Desktop;

internal static class Program {
    [STAThread] static int Main(string[] args) {
        if(args.Length!=2) {Console.Error.WriteLine("Usage: capture-test connection.json qr-fixture.png");return 2;}
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);Application.EnableVisualStyles();
        using var fixture=new Bitmap(args[1]);
        using var source=new Form {Text="VirCam · 受控桌面采集测试源",ClientSize=new Size(640,480),AutoScaleMode=AutoScaleMode.None,
            TopMost=true,StartPosition=FormStartPosition.Manual,FormBorderStyle=FormBorderStyle.FixedToolWindow};
        source.Location=new Point(Screen.PrimaryScreen!.WorkingArea.Left+Math.Max(0,(Screen.PrimaryScreen.WorkingArea.Width-640)/2),Screen.PrimaryScreen.WorkingArea.Top+200);
        source.Controls.Add(new PictureBox {Dock=DockStyle.Fill,Image=fixture,SizeMode=PictureBoxSizeMode.Normal});
        int code=1;
        source.Shown+=async(_,_)=>{
            try {
                await Task.Delay(700);
                var rectangle=source.RectangleToScreen(source.ClientRectangle);
                using var image=DesktopCapture.Region(rectangle,Rectangle.Empty,IntPtr.Zero);
                int differences=0,total=0;
                for(int y=10;y<470;y+=11)for(int x=10;x<630;x+=11) {total++;if(image.GetPixel(x,y).ToArgb()!=fixture.GetPixel(x,y).ToArgb())differences++;}
                if(differences!=0) {
                    using var raw=new Bitmap(rectangle.Width,rectangle.Height,System.Drawing.Imaging.PixelFormat.Format32bppArgb);
                    using(var graphics=Graphics.FromImage(raw)) graphics.CopyFromScreen(rectangle.Location,Point.Empty,rectangle.Size);
                    int rawDifferences=0;
                    for(int y=10;y<470;y+=11)for(int x=10;x<630;x+=11)
                        if(raw.GetPixel(x,y).ToArgb()!=fixture.GetPixel(x,y).ToArgb()) rawDifferences++;
                    throw new Exception($"Controlled source capture mismatch: fit={differences}/{total}, raw={rawDifferences}/{total}; rect={rectangle}; dpi={source.DeviceDpi}. Keep the controlled source visible and uncovered.");
                }
                // A covering control window must be rejected, rather than captured recursively.
                try {using var forbidden=DesktopCapture.Region(rectangle,rectangle,IntPtr.Zero);throw new Exception("Occlusion was accepted");}
                catch(InvalidOperationException) { }
                if(args[0]!="--pixels-only") {
                    using var client=new FrameClient(ConnectionInfo.Load(args[0]));await client.ConnectAsync(CancellationToken.None);
                    for(int i=0;i<10;i++) {
                        using var frame=DesktopCapture.Region(rectangle,Rectangle.Empty,IntPtr.Zero);
                        for(int y=10;y<470;y+=11)for(int x=10;x<630;x+=11)
                            if(frame.GetPixel(x,y).ToArgb()!=fixture.GetPixel(x,y).ToArgb()) throw new Exception("Controlled source was covered during streaming");
                        await client.SendFrameAsync(DesktopCapture.Png(frame),DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),CancellationToken.None);
                        await Task.Delay(200);
                    }
                    for(int i=0;i<10;i++) {await client.PauseAsync(CancellationToken.None);await Task.Delay(200);}
                }
                Console.WriteLine($"PASS: captured actual visible region {rectangle}; pixel samples {total}, mismatch {differences}; occlusion rejected; network={args[0]!="--pixels-only"}");
                code=0;
            } catch(Exception e) {Console.Error.WriteLine(e);}
            finally {source.Close();}
        };
        Application.Run(source);return code;
    }
}
