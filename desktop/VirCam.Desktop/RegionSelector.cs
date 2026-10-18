namespace VirCam.Desktop;

internal sealed class RegionSelector : Form {
    private Point anchor,current;
    private bool dragging;
    public Rectangle Selected {get;private set;}
    public RegionSelector() {
        AutoScaleMode=AutoScaleMode.None;FormBorderStyle=FormBorderStyle.None;
        Bounds=SystemInformation.VirtualScreen;BackColor=Color.Black;Opacity=0.35;TopMost=true;
        Cursor=Cursors.Cross;KeyPreview=true;ShowInTaskbar=false;DoubleBuffered=true;
        KeyDown+=(_,e)=>{if(e.KeyCode==Keys.Escape) { DialogResult=DialogResult.Cancel;Close(); }};
    }
    protected override void OnMouseDown(MouseEventArgs e) {
        if(e.Button!=MouseButtons.Left) return;
        anchor=Cursor.Position;current=anchor;dragging=true;Capture=true;Invalidate();
    }
    protected override void OnMouseMove(MouseEventArgs e) { if(dragging) { current=Cursor.Position;Invalidate(); } }
    protected override void OnMouseUp(MouseEventArgs e) {
        if(!dragging) return;
        current=Cursor.Position;Selected=FromPoints(anchor,current);Capture=false;
        DialogResult=Selected.Width>=32 && Selected.Height>=32?DialogResult.OK:DialogResult.Cancel;Close();
    }
    protected override void OnPaint(PaintEventArgs e) {
        base.OnPaint(e);
        if(!dragging) return;
        var box=FromPoints(anchor,current);box.Offset(-Left,-Top);
        using var pen=new Pen(Color.Lime,3);e.Graphics.DrawRectangle(pen,box);
    }
    private static Rectangle FromPoints(Point a,Point b)=>Rectangle.FromLTRB(Math.Min(a.X,b.X),Math.Min(a.Y,b.Y),Math.Max(a.X,b.X),Math.Max(a.Y,b.Y));
}
