using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Windows.Forms;

// 角色由矢量图形直接绘制，透明边缘和动画均无需网络素材或额外运行库。
public static class PetArt
{
    public static readonly string[] Names = { "猫咪", "卡皮巴拉", "小兔子", "小机器人", "招财猫" };
    private static void Oval(Graphics g, Color fill, Pen outline, float x, float y, float w, float h)
    {
        using (SolidBrush brush = new SolidBrush(fill)) g.FillEllipse(brush, x, y, w, h);
        if (outline != null) g.DrawEllipse(outline, x, y, w, h);
    }
    public static Bitmap Render(int kind, int theme, int frame, bool walking, bool speaking)
    {
        Bitmap bitmap = new Bitmap(200, 190, PixelFormat.Format32bppPArgb);
        using (Graphics g = Graphics.FromImage(bitmap))
        using (Pen outline = new Pen(Color.FromArgb(83, 66, 65), 3) { LineJoin = LineJoin.Round, StartCap = LineCap.Round, EndCap = LineCap.Round })
        {
            g.SmoothingMode = SmoothingMode.AntiAlias; g.Clear(Color.Transparent);
            Color accent = theme == 0 ? Color.FromArgb(92, 187, 220) : Color.FromArgb(223, 138, 180);
            bool blink = frame % 68 >= 63 && frame % 68 <= 65;
            float bob = (float)Math.Sin(frame * .14) * 2, step = walking ? (float)Math.Sin(frame * .75) * 4 : 0;
            Oval(g, Color.FromArgb(28, 35, 38, 57), null, 47, 169, 110, 12);
            g.TranslateTransform(0, bob);
            if (kind == 3)
            {
                outline.Color = Color.FromArgb(35, 62, 92);
                using (Pen antenna = new Pen(accent, 4)) g.DrawLine(antenna, 100, 24, 100, 43);
                Oval(g, accent, outline, 94, 17, 12, 12);
                using (GraphicsPath body = Widget.Rounded(new Rectangle(64, 112, 73, 52), 15))
                using (LinearGradientBrush metal = new LinearGradientBrush(new Rectangle(64, 112, 73, 52), Color.FromArgb(219, 238, 250), Color.FromArgb(132, 164, 198), 65f)) { g.FillPath(metal, body); g.DrawPath(outline, body); }
                Oval(g, Color.FromArgb(130, 169, 199), outline, 58, 154 + step, 32, 17);
                Oval(g, Color.FromArgb(130, 169, 199), outline, 111, 154 - step, 32, 17);
                Oval(g, Color.FromArgb(176, 205, 224), outline, 44, 121 - step, 20, 34);
                Oval(g, Color.FromArgb(176, 205, 224), outline, 138, 121 + step, 20, 34);
                using (GraphicsPath head = Widget.Rounded(new Rectangle(42, 43, 116, 79), 23))
                using (LinearGradientBrush metal = new LinearGradientBrush(new Rectangle(42, 43, 116, 79), Color.FromArgb(237, 249, 255), Color.FromArgb(163, 192, 218), 60f)) { g.FillPath(metal, head); g.DrawPath(outline, head); }
                using (GraphicsPath screen = Widget.Rounded(new Rectangle(53, 56, 94, 52), 14))
                using (SolidBrush dark = new SolidBrush(Color.FromArgb(20, 37, 61))) g.FillPath(dark, screen);
                using (Pen light = new Pen(accent, 5) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                {
                    if (blink) { g.DrawLine(light, 69, 78, 80, 78); g.DrawLine(light, 119, 78, 130, 78); }
                    else { g.DrawArc(light, 68, 69, 13, 17, 200, 140); g.DrawArc(light, 119, 69, 13, 17, 200, 140); }
                    g.DrawArc(light, 88, 81, 24, speaking ? 14 : 9, 0, 180);
                }
                Oval(g, accent, outline, 88, 128, 24, 24);
                using (Pen power = new Pen(Color.White, 2)) { g.DrawArc(power, 94, 134, 12, 12, -50, 280); g.DrawLine(power, 100, 132, 100, 139); }
            }
            else
            {
                Color fur = kind == 0 ? Color.FromArgb(255, 221, 164) : kind == 1 ? Color.FromArgb(192, 142, 98) : Color.FromArgb(255, 245, 238);
                Color pale = kind == 1 ? Color.FromArgb(222, 177, 127) : Color.FromArgb(255, 245, 220);
                if (kind == 0 || kind == 4)
                {
                    using (Pen tail = new Pen(outline.Color, 22) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                    using (Pen inner = new Pen(fur, 16) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                    {
                        PointF[] points = { new PointF(137, 146), new PointF(164, 148), new PointF(170, 124 + (float)Math.Sin(frame * .18) * 5) };
                        g.DrawCurve(tail, points); g.DrawCurve(inner, points);
                    }
                }
                Oval(g, fur, outline, 61, 103, 80, 64);
                Oval(g, pale, null, 78, 122, 46, 39);
                Oval(g, fur, outline, 56, 152 + step, 38, 20);
                Oval(g, fur, outline, 108, 152 - step, 38, 20);
                if (kind == 4)
                {
                    GraphicsState paw = g.Save();
                    g.TranslateTransform(158, 126); g.RotateTransform((float)Math.Sin(frame * .18) * 9); g.TranslateTransform(-158, -126);
                    Oval(g, fur, outline, 143, 91, 28, 49); Oval(g, fur, outline, 147, 67, 31, 34);
                    using (Pen fingers = new Pen(Color.FromArgb(211, 129, 127), 2)) { g.DrawLine(fingers, 156, 74, 156, 82); g.DrawLine(fingers, 163, 72, 163, 81); g.DrawLine(fingers, 170, 75, 170, 83); }
                    g.Restore(paw);
                }
                if (kind == 2)
                {
                    Oval(g, fur, outline, 58, 8, 29, 78); Oval(g, fur, outline, 111, 5, 29, 80);
                    Oval(g, Color.FromArgb(249, 187, 200), null, 66, 19, 13, 51); Oval(g, Color.FromArgb(249, 187, 200), null, 119, 16, 13, 53);
                }
                else if (kind == 1)
                {
                    Oval(g, fur, outline, 52, 46, 27, 30); Oval(g, fur, outline, 125, 46, 27, 30);
                    Oval(g, Color.FromArgb(143, 100, 76), null, 60, 53, 12, 14); Oval(g, Color.FromArgb(143, 100, 76), null, 132, 53, 12, 14);
                }
                else
                {
                    PointF[] left = { new PointF(52, 81), new PointF(54, 32), new PointF(89, 58) };
                    PointF[] right = { new PointF(111, 58), new PointF(147, 32), new PointF(149, 82) };
                    using (SolidBrush brush = new SolidBrush(fur)) { g.FillPolygon(brush, left); g.FillPolygon(brush, right); }
                    g.DrawPolygon(outline, left); g.DrawPolygon(outline, right);
                    using (SolidBrush pink = new SolidBrush(Color.FromArgb(238, 158, 160)))
                    { g.FillPolygon(pink, new Point[] { new Point(60, 64), new Point(61, 45), new Point(77, 60) }); g.FillPolygon(pink, new Point[] { new Point(124, 60), new Point(140, 45), new Point(141, 65) }); }
                }
                Oval(g, fur, outline, kind == 1 ? 45 : 43, 52, kind == 1 ? 113 : 115, 82);
                if (kind == 1)
                {
                    Oval(g, pale, outline, 62, 92, 93, 42);
                    Oval(g, outline.Color, null, 111, 99, 16, 10);
                    using (Pen nose = new Pen(outline.Color, 2)) { g.DrawLine(nose, 119, 110, 119, speaking ? 122 : 117); g.DrawArc(nose, 106, 112, 26, 10, 0, 180); }
                }
                else
                {
                    Oval(g, pale, null, 75, 91, 50, 35);
                    using (SolidBrush nose = new SolidBrush(Color.FromArgb(153, 101, 108))) g.FillPolygon(nose, new Point[] { new Point(94, 99), new Point(106, 99), new Point(100, 105) });
                    g.DrawArc(outline, 87, 102, 13, speaking ? 13 : 9, 0, 180); g.DrawArc(outline, 100, 102, 13, speaking ? 13 : 9, 0, 180);
                }
                int eyeY = kind == 1 ? 87 : 85;
                if (blink) { g.DrawLine(outline, 69, eyeY + 3, 79, eyeY + 3); g.DrawLine(outline, 121, eyeY + 3, 131, eyeY + 3); }
                else { Oval(g, outline.Color, null, 71, eyeY, 7, 10); Oval(g, outline.Color, null, 122, eyeY, 7, 10); Oval(g, Color.White, null, 72, eyeY, 2, 3); Oval(g, Color.White, null, 123, eyeY, 2, 3); }
                Oval(g, Color.FromArgb(105, 241, 139, 154), null, 59, 99, 15, 7); Oval(g, Color.FromArgb(105, 241, 139, 154), null, 133, 99, 15, 7);
                using (Pen collar = new Pen(kind == 4 ? Color.FromArgb(218, 70, 78) : accent, 7)) g.DrawArc(collar, 67, 113, 68, 24, 10, 160);
                if (kind == 4)
                {
                    Oval(g, Color.FromArgb(255, 207, 81), outline, 80, 132, 43, 39);
                    using (Pen gold = new Pen(Color.FromArgb(222, 153, 42), 2)) g.DrawEllipse(gold, 85, 136, 33, 30);
                    using (Font fortune = new Font("Microsoft YaHei UI", 15, FontStyle.Bold)) TextRenderer.DrawText(g, "福", fortune, new Rectangle(80, 132, 43, 39), Color.FromArgb(154, 80, 34), TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
                }
                else Oval(g, Color.FromArgb(250, 205, 101), outline, 94, 131, 14, 14);
                if (kind == 1) { Oval(g, Color.FromArgb(255, 170, 75), outline, 83, 34, 35, 25); using (Pen leaf = new Pen(Color.FromArgb(91, 143, 85), 4)) g.DrawLine(leaf, 101, 33, 112, 27); }
                if (kind == 0) using (Pen stripes = new Pen(Color.FromArgb(216, 158, 92), 4)) { g.DrawLine(stripes, 90, 57, 94, 67); g.DrawLine(stripes, 102, 55, 102, 67); g.DrawLine(stripes, 113, 57, 109, 67); }
            }
        }
        return bitmap;
    }
}

public sealed class DesktopPet : Form
{
    private Settings settings;
    private readonly System.Windows.Forms.Timer timer = new System.Windows.Forms.Timer();
    private readonly Action toggleDetails;
    private readonly Action<Point> moved;
    private Point down, origin;
    private bool dragging, travelled;
    private int frame, direction = -1;
    public bool Speaking, Paused;
    [StructLayout(LayoutKind.Sequential)] private struct XY { public int X, Y; public XY(int x, int y) { X = x; Y = y; } }
    [StructLayout(LayoutKind.Sequential, Pack = 1)] private struct Blend { public byte Op, Flags, Alpha, Format; }
    [DllImport("user32.dll")] private static extern IntPtr GetDC(IntPtr window);
    [DllImport("user32.dll")] private static extern int ReleaseDC(IntPtr window, IntPtr dc);
    [DllImport("gdi32.dll")] private static extern IntPtr CreateCompatibleDC(IntPtr dc);
    [DllImport("gdi32.dll")] private static extern IntPtr SelectObject(IntPtr dc, IntPtr value);
    [DllImport("gdi32.dll")] private static extern bool DeleteObject(IntPtr value);
    [DllImport("gdi32.dll")] private static extern bool DeleteDC(IntPtr dc);
    [DllImport("user32.dll", SetLastError = true)] private static extern bool UpdateLayeredWindow(IntPtr window, IntPtr dst, ref XY position, ref XY size, IntPtr src, ref XY source, int color, ref Blend blend, int flags);
    public DesktopPet(Settings value, Action toggle, Action<Point> onMoved, ContextMenuStrip menu)
    {
        settings = value; toggleDetails = toggle; moved = onMoved;
        Text = "桌宠 · 上班鼓励器"; ClientSize = new Size(200, 190); FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false; StartPosition = FormStartPosition.Manual; AutoScaleMode = AutoScaleMode.None;
        TopMost = value.AlwaysOnTop; ContextMenuStrip = menu; Cursor = Cursors.Hand;
        Rectangle area = Screen.PrimaryScreen.WorkingArea;
        Location = value.PetX == int.MinValue ? new Point(area.Right - Width - 36, area.Bottom - Height - 20) : new Point(value.PetX, value.PetY);
        Rectangle active = Screen.FromRectangle(Bounds).WorkingArea;
        Location = new Point(Math.Max(active.Left, Math.Min(Left, active.Right - Width)), Math.Max(active.Top, Math.Min(Top, active.Bottom - Height)));
        timer.Interval = 80; timer.Tick += delegate
        {
            frame++;
            if (!Visible) return;
            bool walk = settings.PetWalk && !Paused && !Speaking && !dragging && frame % 170 > 95;
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
        MouseDown += delegate(object sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left || Paused) return;
            dragging = true; travelled = false; down = Cursor.Position; origin = Location; Capture = true;
        };
        MouseMove += delegate
        {
            if (!dragging) return;
            int x = Cursor.Position.X - down.X, y = Cursor.Position.Y - down.Y;
            if (Math.Abs(x) + Math.Abs(y) > 5) travelled = true;
            if (travelled) { Location = new Point(origin.X + x, origin.Y + y); Draw(false); }
        };
        MouseUp += delegate(object sender, MouseEventArgs e)
        {
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
    protected override CreateParams CreateParams { get { CreateParams p = base.CreateParams; p.ExStyle |= 0x00080000 | 0x08000000; return p; } }
    public void ApplySettings(Settings value) { settings = value; TopMost = value.AlwaysOnTop; if (Visible) Draw(false); }
    private void Draw(bool walking)
    {
        if (!IsHandleCreated) return;
        using (Bitmap bitmap = PetArt.Render(settings.PetCharacter, settings.Theme, frame, walking, Speaking))
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
        window.Location = new Point(Math.Max(area.Left, Math.Min(x, area.Right - window.Width)), Math.Max(area.Top, Math.Min(petBounds.Top - 30, area.Bottom - window.Height)));
    }
    protected override void Dispose(bool disposing) { if (disposing) timer.Dispose(); base.Dispose(disposing); }
}
