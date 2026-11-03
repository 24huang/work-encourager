using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Windows.Forms;

public static class PetArt
{
    public static readonly string[] Names = { "猫咪", "卡皮巴拉", "小兔子", "小机器人", "招财猫" };
    public static Bitmap Render(int kind, int theme, int frame, bool walking, bool speaking)
    {
        return PetModel3D.Render(kind, theme, frame, 0, 25, 12, walking, speaking);
    }
}


// 预览与点击固定的详情分开管理，拖动或点击后的同一次悬停不会反复弹出预览。
public sealed class PetHoverState
{
    private DateTime entered;
    private bool inside, suppressed;
    public bool Inside { get { return inside; } }
    public void Enter(DateTime now) { inside = true; suppressed = false; entered = now; }
    public void Leave() { inside = false; suppressed = false; }
    public void Suppress() { suppressed = true; }
    public bool ShouldShow(DateTime now, bool blocked)
    {
        if (blocked) entered = now;
        return inside && !suppressed && !blocked && (now - entered).TotalMilliseconds >= 450;
    }
}

public sealed class DesktopPet : Form
{
    private Settings settings;
    private readonly System.Windows.Forms.Timer timer = new System.Windows.Forms.Timer();
    private readonly Action toggleDetails;
    private readonly Action<Point> moved;
    private readonly Action<double, double> viewChanged;
    private readonly Action<bool> hoverChanged;
    private readonly PetHoverState hover = new PetHoverState();
    private bool previewVisible;
    private readonly ContextMenuStrip petMenu;
    private readonly PetBehavior behavior = new PetBehavior(DateTime.Now, Environment.TickCount);
    private Point down;
    private PointF grab;
    private bool dragging, rotating, travelled;
    private double yaw, pitch, downYaw, downPitch;
    private int frame, direction = -1;
    private int dockEdge;
    private bool dockExpanded;
    private double dockReveal;
    private bool fitting, perMonitorDpi;
    private Rectangle displayArea;
    public int DockEdge { get { return dockEdge; } }
    public bool Speaking, Paused;
    public int CurrentPose { get { return behavior.Current; } }
    [StructLayout(LayoutKind.Sequential)] private struct XY { public int X, Y; public XY(int x, int y) { X = x; Y = y; } }
    [StructLayout(LayoutKind.Sequential, Pack = 1)] private struct Blend { public byte Op, Flags, Alpha, Format; }
    [DllImport("user32.dll")] private static extern IntPtr GetDC(IntPtr window);
    [DllImport("user32.dll")] private static extern IntPtr WindowFromPoint(Point point);
    [DllImport("user32.dll")] private static extern IntPtr SetThreadDpiAwarenessContext(IntPtr context);
    [DllImport("user32.dll")] private static extern IntPtr MonitorFromPoint(Point point, int flags);
    [DllImport("shcore.dll")] private static extern int GetScaleFactorForMonitor(IntPtr monitor, out int scale);
    [DllImport("user32.dll")] private static extern int ReleaseDC(IntPtr window, IntPtr dc);
    [DllImport("gdi32.dll")] private static extern IntPtr CreateCompatibleDC(IntPtr dc);
    [DllImport("gdi32.dll")] private static extern IntPtr SelectObject(IntPtr dc, IntPtr value);
    [DllImport("gdi32.dll")] private static extern bool DeleteObject(IntPtr value);
    [DllImport("gdi32.dll")] private static extern bool DeleteDC(IntPtr dc);
    [DllImport("user32.dll", SetLastError = true)] private static extern bool UpdateLayeredWindow(IntPtr window, IntPtr dst, ref XY position, ref XY size, IntPtr src, ref XY source, int color, ref Blend blend, int flags);
    public DesktopPet(Settings value, Action toggle, Action<Point> onMoved, ContextMenuStrip menu, Action<double, double> onViewChanged = null, Action<bool> onHoverChanged = null)
    {
        settings = value; toggleDetails = toggle; moved = onMoved; petMenu = menu; viewChanged = onViewChanged; hoverChanged = onHoverChanged;
        // 仅桌宠窗口使用每屏DPI，其余已有表单继续使用原来的缩放模式。
        IntPtr previousDpi = IntPtr.Zero;
        try { previousDpi = SetThreadDpiAwarenessContext(new IntPtr(-4)); perMonitorDpi = previousDpi != IntPtr.Zero; } catch (EntryPointNotFoundException) { }
        try
        {
        yaw = value.PetYaw; pitch = value.PetPitch;
        Text = "桌宠 · 上班鼓励器"; ClientSize = new Size(200, 190); FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false; StartPosition = FormStartPosition.Manual; AutoScaleMode = AutoScaleMode.None;
        TopMost = value.AlwaysOnTop; Cursor = Cursors.Hand;
        Rectangle area = Screen.PrimaryScreen.WorkingArea;
        Location = value.PetX == int.MinValue ? new Point(area.Right - Width - 36, area.Bottom - Height - 20) : new Point(value.PetX, value.PetY);
        Rectangle active = Screen.FromRectangle(Bounds).WorkingArea;
        Location = new Point(Math.Max(active.Left, Math.Min(Left, active.Right - Width)), Math.Max(active.Top, Math.Min(Top, active.Bottom - Height)));
        dockEdge = value.PetDockEdge;
        DockAt(Location, active, true);
        IntPtr nativeWindow = Handle; FitDisplay();
        timer.Interval = 100; timer.Tick += delegate
        {
            frame++;
            if (!Visible) { hover.Leave(); RefreshHover(); return; }
            if (frame % 10 == 0 && !rotating) FitDisplay();
            if (dockEdge != 0 && !dockExpanded && Bounds.Contains(Cursor.Position) && WindowFromPoint(Cursor.Position) == Handle)
            { dockExpanded = true; if (!hover.Inside) hover.Enter(DateTime.UtcNow); }
            // 展开时图像会离开原来的鼠标像素；以窗口范围维持悬停，避免反复伸缩。
            if (dockEdge != 0 && dockExpanded && !Bounds.Contains(Cursor.Position) && !Capture && !petMenu.Visible)
            { dockExpanded = false; hover.Leave(); }
            dockReveal = Math.Max(0, Math.Min(1, dockReveal + (dockExpanded ? .4 : -.4)));
            RefreshHover();
            behavior.Update(settings.PetPose, DateTime.Now, dockEdge != 0 || Paused || Speaking || dragging || rotating || hover.Inside);
            bool walk = dockEdge == 0 && settings.PetWalk && CurrentPose == 0 && !Paused && !Speaking && !dragging && !rotating && !hover.Inside && frame % 170 > 95;
            if (walk)
            {
                Rectangle screen = Screen.FromRectangle(Bounds).WorkingArea;
                if (Left <= screen.Left || Right >= screen.Right) direction = -direction;
                Left = Math.Max(screen.Left, Math.Min(screen.Right - Width, Left + direction * 2));
                if (frame % 170 == 169) moved(Location);
            }
            Draw(walk);
        };
        Shown += delegate { Draw(false); timer.Start(); };
        MouseEnter += delegate { dockExpanded = dockEdge != 0; if (!hover.Inside) hover.Enter(DateTime.UtcNow); RefreshHover(); };
        MouseLeave += delegate
        {
            if (dockEdge != 0 && dockExpanded && Bounds.Contains(Cursor.Position)) return;
            dockExpanded = false; hover.Leave(); RefreshHover();
        };
        MouseDown += delegate(object sender, MouseEventArgs e)
        {
            if (Paused || (e.Button != MouseButtons.Left && e.Button != MouseButtons.Right)) return;
            hover.Suppress(); RefreshHover();
            dragging = e.Button == MouseButtons.Left; rotating = e.Button == MouseButtons.Right;
            travelled = false; down = Cursor.Position; grab = new PointF(e.X / (float)Width, e.Y / (float)Height); downYaw = yaw; downPitch = pitch; Capture = true;
        };
        MouseMove += delegate
        {
            if (!dragging && !rotating) return;
            int x = Cursor.Position.X - down.X, y = Cursor.Position.Y - down.Y;
            if (Math.Abs(x) + Math.Abs(y) > 5) travelled = true;
            if (!travelled) return;
            if (rotating) { yaw = ((downYaw + x * .7) % 360 + 360) % 360; pitch = Math.Max(-25, Math.Min(60, downPitch + y * .45)); }
            else { dockEdge = 0; dockExpanded = false; dockReveal = 0; Location = new Point(Cursor.Position.X - (int)(grab.X * Width), Cursor.Position.Y - (int)(grab.Y * Height)); }
            Draw(false);
        };
        MouseUp += delegate(object sender, MouseEventArgs e)
        {
            if (rotating && e.Button == MouseButtons.Right)
            {
                rotating = false; Capture = false;
                if (travelled) { if (viewChanged != null) viewChanged(yaw, pitch); }
                else petMenu.Show(Cursor.Position);
                return;
            }
            if (!dragging || e.Button != MouseButtons.Left) return;
            dragging = false; Capture = false;
            if (travelled)
            {
                Rectangle screen = Screen.FromRectangle(Bounds).WorkingArea;
                DockAt(Location, screen, false);
                moved(Location);
            }
            else toggleDetails();
        };
        }
        finally { if (previousDpi != IntPtr.Zero) SetThreadDpiAwarenessContext(previousDpi); }
    }
    public static Size SizeFor(int percent, int dpi, Size available)
    {
        double scale = percent / 100.0 * dpi / 96.0;
        scale = Math.Min(scale, Math.Min(Math.Max(1, available.Width - 8) / 200.0, Math.Max(1, available.Height - 8) / 190.0));
        return new Size(Math.Max(1, (int)Math.Round(200 * scale)), Math.Max(1, (int)Math.Round(190 * scale)));
    }
    public void FitDisplay()
    {
        if (fitting || settings == null || !IsHandleCreated || IsDisposed) return;
        fitting = true;
        try
        {
            Rectangle area = (dragging ? Screen.FromPoint(Cursor.Position) : Screen.FromRectangle(Bounds)).WorkingArea;
            int factor = 100;
            if (perMonitorDpi)
            {
                try { int actual; if (GetScaleFactorForMonitor(MonitorFromPoint(new Point(area.Left + area.Width / 2, area.Top + area.Height / 2), 2), out actual) == 0) factor = actual; }
                catch (DllNotFoundException) { } catch (EntryPointNotFoundException) { }
            }
            else using (Graphics g = Graphics.FromHwnd(Handle)) factor = (int)Math.Round(g.DpiX / 96 * 100);
            Size next = SizeFor(settings.PetScale, (int)Math.Round(factor * .96), area.Size);
            bool changed = next != ClientSize || displayArea != area;
            displayArea = area;
            if (!changed && (dragging || area.Contains(Bounds))) return;
            ClientSize = next;
            if (!dragging) DockAt(Location, area, true);
            else Draw(false);
        }
        finally { fitting = false; }
    }
    protected override void WndProc(ref Message message)
    {
        if ((message.Msg == 0x02E0 || message.Msg == 0x007E) && IsHandleCreated && !IsDisposed)
        {
            // 延后到本次显示器变更消息处理完，再重新读取屏幕工作区。
            BeginInvoke(new Action(delegate { FitDisplay(); }));
        }
        base.WndProc(ref message);
    }
    public static int FindDockEdge(Rectangle bounds, Rectangle area)
    {
        int[] distances = { bounds.Left - area.Left, area.Right - bounds.Right, bounds.Top - area.Top, area.Bottom - bounds.Bottom };
        int edge = 0, nearest = 25;
        for (int i = 0; i < distances.Length; i++) if (distances[i] < nearest) { nearest = distances[i]; edge = i + 1; }
        return edge;
    }
    public void DockAt(Point position, Rectangle area, bool restoring)
    {
        if (!restoring) dockEdge = FindDockEdge(new Rectangle(position, Size), area);
        int x = Math.Max(area.Left, Math.Min(position.X, area.Right - Width)), y = Math.Max(area.Top, Math.Min(position.Y, area.Bottom - Height));
        if (dockEdge == 1) x = area.Left; else if (dockEdge == 2) x = area.Right - Width;
        else if (dockEdge == 3) y = area.Top; else if (dockEdge == 4) y = area.Bottom - Height;
        Location = new Point(x, y);
        dockExpanded = false; dockReveal = 0;
        if (IsHandleCreated) Draw(false);
    }
    protected override bool ShowWithoutActivation { get { return true; } }
    private void RefreshHover()
    {
        bool show = hover.ShouldShow(DateTime.UtcNow, Paused || Speaking || dragging || rotating || petMenu.Visible || !settings.HoverEnabled);
        if (show != previewVisible) { previewVisible = show; if (hoverChanged != null) hoverChanged(show); }
    }
    protected override CreateParams CreateParams { get { CreateParams p = base.CreateParams; p.ExStyle |= 0x00080000 | 0x08000000; return p; } }
    public void ApplySettings(Settings value)
    {
        settings = value; TopMost = value.AlwaysOnTop;
        if (!rotating) { yaw = value.PetYaw; pitch = value.PetPitch; }
        FitDisplay();
        if (Visible) Draw(false);
    }
    private void Draw(bool walking)
    {
        if (!IsHandleCreated) return;
        int pose = behavior.Update(settings.PetPose, DateTime.Now, dockEdge != 0 || Paused || Speaking || dragging || rotating || hover.Inside);
        using (Bitmap character = dockEdge == 0 ? PetModel3D.Render(settings.PetCharacter, settings.Theme, frame, pose, yaw, pitch, walking, Speaking, dragging && travelled, Math.Min(300, Width)) : PetModel3D.RenderDocked(settings.PetCharacter, settings.Theme, frame, dockEdge, dockReveal, Math.Min(300, Width)))
        using (Bitmap bitmap = new Bitmap(Width, Height, PixelFormat.Format32bppPArgb))
        {
            using (Graphics graphics = Graphics.FromImage(bitmap))
            { graphics.CompositingMode = CompositingMode.SourceCopy; graphics.InterpolationMode = InterpolationMode.HighQualityBicubic; graphics.DrawImage(character, new Rectangle(0, 0, Width, Height)); }
            IntPtr screen = GetDC(IntPtr.Zero), dc = CreateCompatibleDC(screen), image = bitmap.GetHbitmap(Color.FromArgb(0));
            IntPtr old = SelectObject(dc, image);
            try
            {
                XY at = new XY(Left, Top), size = new XY(Width, Height), source = new XY(0, 0);
                Blend blend = new Blend { Alpha = 255, Format = 1 };
                if (!UpdateLayeredWindow(Handle, screen, ref at, ref size, dc, ref source, 0, ref blend, 2)) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
            }
            finally { SelectObject(dc, old); DeleteObject(image); DeleteDC(dc); ReleaseDC(IntPtr.Zero, screen); }
        }
    }
    public static void PlaceBeside(Form window, Rectangle petBounds)
    {
        Rectangle area = Screen.FromRectangle(petBounds).WorkingArea;
        int x = petBounds.Left - window.Width + 22;
        if (x < area.Left) x = petBounds.Right - 22;
        ReminderPopup bubble = window as ReminderPopup;
        int y = bubble == null ? petBounds.Top - 30 : petBounds.Top + petBounds.Height / 3 - window.Height / 2;
        window.Location = new Point(Math.Max(area.Left, Math.Min(x, area.Right - window.Width)), Math.Max(area.Top, Math.Min(y, area.Bottom - window.Height)));
        if (bubble != null) bubble.AimAtPet(petBounds);
    }
    protected override void Dispose(bool disposing) { if (disposing) timer.Dispose(); base.Dispose(disposing); }
}
