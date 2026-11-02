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
    private Point down, origin;
    private bool dragging, rotating, travelled;
    private double yaw, pitch, downYaw, downPitch;
    private int frame, direction = -1;
    public bool Speaking, Paused;
    public int CurrentPose { get { return behavior.Current; } }
    [StructLayout(LayoutKind.Sequential)] private struct XY { public int X, Y; public XY(int x, int y) { X = x; Y = y; } }
    [StructLayout(LayoutKind.Sequential, Pack = 1)] private struct Blend { public byte Op, Flags, Alpha, Format; }
    [DllImport("user32.dll")] private static extern IntPtr GetDC(IntPtr window);
    [DllImport("user32.dll")] private static extern int ReleaseDC(IntPtr window, IntPtr dc);
    [DllImport("gdi32.dll")] private static extern IntPtr CreateCompatibleDC(IntPtr dc);
    [DllImport("gdi32.dll")] private static extern IntPtr SelectObject(IntPtr dc, IntPtr value);
    [DllImport("gdi32.dll")] private static extern bool DeleteObject(IntPtr value);
    [DllImport("gdi32.dll")] private static extern bool DeleteDC(IntPtr dc);
    [DllImport("user32.dll", SetLastError = true)] private static extern bool UpdateLayeredWindow(IntPtr window, IntPtr dst, ref XY position, ref XY size, IntPtr src, ref XY source, int color, ref Blend blend, int flags);
    public DesktopPet(Settings value, Action toggle, Action<Point> onMoved, ContextMenuStrip menu, Action<double, double> onViewChanged = null, Action<bool> onHoverChanged = null)
    {
        settings = value; toggleDetails = toggle; moved = onMoved; petMenu = menu; viewChanged = onViewChanged; hoverChanged = onHoverChanged;
        yaw = value.PetYaw; pitch = value.PetPitch;
        Text = "桌宠 · 上班鼓励器"; ClientSize = new Size(200, 190); FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false; StartPosition = FormStartPosition.Manual; AutoScaleMode = AutoScaleMode.None;
        TopMost = value.AlwaysOnTop; Cursor = Cursors.Hand;
        Rectangle area = Screen.PrimaryScreen.WorkingArea;
        Location = value.PetX == int.MinValue ? new Point(area.Right - Width - 36, area.Bottom - Height - 20) : new Point(value.PetX, value.PetY);
        Rectangle active = Screen.FromRectangle(Bounds).WorkingArea;
        Location = new Point(Math.Max(active.Left, Math.Min(Left, active.Right - Width)), Math.Max(active.Top, Math.Min(Top, active.Bottom - Height)));
        timer.Interval = 100; timer.Tick += delegate
        {
            frame++;
            if (!Visible) { hover.Leave(); RefreshHover(); return; }
            RefreshHover();
            behavior.Update(settings.PetPose, DateTime.Now, Paused || Speaking || dragging || rotating || hover.Inside);
            bool walk = settings.PetWalk && CurrentPose == 0 && !Paused && !Speaking && !dragging && !rotating && !hover.Inside && frame % 170 > 95;
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
        MouseEnter += delegate { hover.Enter(DateTime.UtcNow); RefreshHover(); };
        MouseLeave += delegate { hover.Leave(); RefreshHover(); };
        MouseDown += delegate(object sender, MouseEventArgs e)
        {
            if (Paused || (e.Button != MouseButtons.Left && e.Button != MouseButtons.Right)) return;
            hover.Suppress(); RefreshHover();
            dragging = e.Button == MouseButtons.Left; rotating = e.Button == MouseButtons.Right;
            travelled = false; down = Cursor.Position; origin = Location; downYaw = yaw; downPitch = pitch; Capture = true;
        };
        MouseMove += delegate
        {
            if (!dragging && !rotating) return;
            int x = Cursor.Position.X - down.X, y = Cursor.Position.Y - down.Y;
            if (Math.Abs(x) + Math.Abs(y) > 5) travelled = true;
            if (!travelled) return;
            if (rotating) { yaw = ((downYaw + x * .7) % 360 + 360) % 360; pitch = Math.Max(-25, Math.Min(60, downPitch + y * .45)); }
            else Location = new Point(origin.X + x, origin.Y + y);
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
                Location = new Point(Math.Max(screen.Left, Math.Min(Left, screen.Right - Width)), Math.Max(screen.Top, Math.Min(Top, screen.Bottom - Height)));
                moved(Location);
            }
            else toggleDetails();
        };
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
        if (Visible) Draw(false);
    }
    private void Draw(bool walking)
    {
        if (!IsHandleCreated) return;
        int pose = behavior.Update(settings.PetPose, DateTime.Now, Paused || Speaking || dragging || rotating || hover.Inside);
        using (Bitmap bitmap = PetModel3D.Render(settings.PetCharacter, settings.Theme, frame, pose, yaw, pitch, walking, Speaking))
        {
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
