using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

public sealed class DetailCard : Control
{
    public static readonly string[] Keys = { "income", "rate", "progress", "countdown", "encouragement", "month", "overtime" };
    public static readonly string[] Titles = { "今日收入", "每秒收入", "上班进度", "剩余有效工时", "鼓励语", "本月累计", "加班与调休" };
    private static readonly string[] Phrases = { "每一点努力，都在靠近生活的小目标。", "工作有进度，生活也要留一点甜。", "认真上班，也记得适时起身喝水。" };
    public static string EncouragementAt(DateTime now) { return Phrases[(now.DayOfYear + now.Hour / 2) % Phrases.Length]; }
    public readonly string Key;
    private Settings settings;
    private Snapshot snapshot;
    private Point press;
    private bool pressed, highlight;
    private readonly Action<string, string> reorder;
    private readonly Action scroll;
    public DetailCard(string key, Settings value, Action<string, string> onReorder, Action onScroll)
    {
        Key = key; settings = value; reorder = onReorder; scroll = onScroll;
        Name = "Detail_" + key; AccessibleName = Titles[Array.IndexOf(Keys, key)];
        DoubleBuffered = true; AllowDrop = true; Cursor = Cursors.SizeAll; TabStop = false;
        MouseDown += delegate(object sender, MouseEventArgs e) { if (e.Button == MouseButtons.Left) { pressed = true; press = e.Location; } };
        MouseUp += delegate { pressed = false; };
        MouseMove += delegate(object sender, MouseEventArgs e)
        {
            if (pressed && e.Button == MouseButtons.Left && Math.Abs(e.X - press.X) + Math.Abs(e.Y - press.Y) > 7)
            { pressed = false; DoDragDrop("WorkEncouragerCard:" + Key, DragDropEffects.Move); }
        };
        DragEnter += delegate(object sender, DragEventArgs e)
        {
            string text = e.Data.GetData(DataFormats.Text) as string;
            if (text != null && text.StartsWith("WorkEncouragerCard:")) { e.Effect = DragDropEffects.Move; highlight = true; Invalidate(); }
        };
        DragOver += delegate(object sender, DragEventArgs e) { scroll(); };
        DragLeave += delegate { highlight = false; Invalidate(); };
        DragDrop += delegate(object sender, DragEventArgs e)
        {
            highlight = false; Invalidate();
            string text = e.Data.GetData(DataFormats.Text) as string;
            if (text != null && text.StartsWith("WorkEncouragerCard:"))
            {
                string source = text.Substring("WorkEncouragerCard:".Length);
                BeginInvoke(new Action(delegate { reorder(source, Key); }));
            }
        };
        UpdateView(value, WorkClock.At(value, DateTime.Now));
    }
    public static int CardHeight(string key, bool mini)
    {
        if (key == "income" || key == "month") return mini ? 64 : 76;
        if (key == "overtime") return mini ? 102 : 110;
        if (key == "rate") return mini ? 44 : 48;
        if (key == "progress") return mini ? 56 : 64;
        if (key == "countdown") return mini ? 60 : 68;
        return mini ? 48 : 52;
    }
    public void UpdateView(Settings value, Snapshot current)
    {
        settings = value; snapshot = current;
        BackColor = value.Theme == 0 ? Color.FromArgb(12, 18, 36) : Color.FromArgb(251, 240, 250);
        Invalidate();
    }
    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e); Graphics g = e.Graphics; g.SmoothingMode = SmoothingMode.AntiAlias;
        bool pink = settings.Theme == 1, mini = Width < 250;
        Color accent = pink ? Color.FromArgb(174, 67, 139) : Color.FromArgb(94, 224, 255);
        Color secondary = pink ? Color.FromArgb(155, 105, 214) : Color.FromArgb(143, 117, 255);
        Color ink = pink ? Color.FromArgb(77, 45, 84) : Color.FromArgb(233, 244, 255);
        Color muted = pink ? Color.FromArgb(141, 105, 145) : Color.FromArgb(144, 165, 197);
        Rectangle box = new Rectangle(0, 0, Width - 1, Height - 1);
        using (GraphicsPath shape = Widget.Rounded(box, 10))
        using (LinearGradientBrush brush = new LinearGradientBrush(box, pink ? Color.FromArgb(255, 251, 255) : Color.FromArgb(25, 39, 66), pink ? Color.FromArgb(249, 227, 245) : Color.FromArgb(36, 31, 64), 24f))
        using (Pen border = new Pen(Color.FromArgb(highlight ? 220 : 60, highlight ? accent : secondary), highlight ? 2 : 1))
        { g.FillPath(brush, shape); g.DrawPath(border, shape); }
        int index = Array.IndexOf(Keys, Key);
        Widget.Write(g, Titles[index], 8, muted, new RectangleF(10, 4, Width - 45, 18), FontStyle.Regular, StringAlignment.Near);
        Widget.Write(g, "⠿", 10, secondary, new RectangleF(Width - 27, 3, 19, 21), FontStyle.Regular, StringAlignment.Center);
        if (Key == "income" || Key == "month")
        {
            decimal earned = Key == "month" ? snapshot.MonthEarned : snapshot.Earned;
            string amount = settings.PrivacyMode ? "¥ ****" : settings.MonthlySalary == 0 ? "¥ —" : "¥ " + earned.ToString("N2");
            Widget.Write(g, amount, settings.PrivacyMode ? mini ? 23 : 27 : earned >= 1000000 ? 13 : earned >= 100000 ? 17 : mini ? 23 : 27, ink, new RectangleF(8, 22, Width - 16, Height - 23), FontStyle.Bold, StringAlignment.Near);
            Widget.Write(g, Key == "month" ? "本月估算" : "估算", 7, muted, new RectangleF(Width - 57, Height - 23, 44, 16), FontStyle.Regular, StringAlignment.Far);
        }
        else if (Key == "overtime")
        {
            Widget.Write(g, "今日 " + (snapshot.Overtime.TodayMinutes / 60m).ToString("0.#") + " 小时" + (settings.PrivacyMode ? "" : " · ¥ " + snapshot.Overtime.TodayValue.ToString("N2")), 9, ink, new RectangleF(10, 24, Width - 20, 24), FontStyle.Bold, StringAlignment.Near);
            Widget.Write(g, "可调休 " + (snapshot.Overtime.BalanceMinutes / 60m).ToString("0.#") + " 小时", 9, ink, new RectangleF(10, 48, Width - 20, 24), FontStyle.Regular, StringAlignment.Near);
            Widget.Write(g, settings.PrivacyMode ? "对应加班费已隐藏" : "余额折算 ¥ " + snapshot.Overtime.BalanceValue.ToString("N2"), 9, accent, new RectangleF(10, 73, Width - 20, 24), FontStyle.Regular, StringAlignment.Near);
        }
        else if (Key == "rate")
        {
            string amount = settings.PrivacyMode ? "¥ **** / 秒" : settings.MonthlySalary == 0 ? "设置薪资后显示" : "¥ " + snapshot.PerSecond.ToString(snapshot.PerSecond >= .0001m ? "0.0000" : "0.0000########") + " / 秒";
            Widget.Write(g, amount, mini ? 11 : 12, accent, new RectangleF(10, 22, Width - 20, Height - 23), FontStyle.Bold, StringAlignment.Near);
        }
        else if (Key == "progress")
        {
            Rectangle ring = new Rectangle(12, 25, mini ? 24 : 30, mini ? 24 : 30);
            using (Pen track = new Pen(Color.FromArgb(35, secondary), 4)) g.DrawEllipse(track, ring);
            using (Pen progress = new Pen(accent, 4) { StartCap = LineCap.Round, EndCap = LineCap.Round }) if (snapshot.Progress > 0) g.DrawArc(progress, ring, -90, (float)snapshot.Progress * 360);
            Widget.Write(g, snapshot.Progress.ToString("P1"), mini ? 15 : 18, ink, new RectangleF(53, 23, 90, Height - 25), FontStyle.Bold, StringAlignment.Near);
            Widget.Write(g, snapshot.Status, 7, accent, new RectangleF(132, 28, Width - 142, Height - 31), FontStyle.Regular, StringAlignment.Far);
        }
        else if (Key == "countdown")
        {
            string time = snapshot.WorkingDay ? string.Format("{0:00}:{1:00}:{2:00}", (int)(snapshot.Remaining / 3600), (int)(snapshot.Remaining / 60) % 60, (int)snapshot.Remaining % 60) : "今日休息";
            Widget.Write(g, time, mini ? 17 : 20, ink, new RectangleF(8, 22, Width - 16, 28), FontStyle.Bold, StringAlignment.Near);
            Widget.Write(g, settings.Start + " — " + settings.End, 7, muted, new RectangleF(10, Height - 18, Width - 20, 16), FontStyle.Regular, StringAlignment.Far);
        }
        else
        {
            Widget.Write(g, EncouragementAt(DateTime.Now), mini ? 7.5f : 8, ink, new RectangleF(10, 23, Width - 20, Height - 24), FontStyle.Regular, StringAlignment.Near);
        }
    }
}
