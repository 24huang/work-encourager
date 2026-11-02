using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using System.Xml.Serialization;

[Serializable]
public class Settings
{
    public decimal MonthlySalary = 0;
    public string Start = "09:00", End = "18:00", BreakStart = "12:00", BreakEnd = "13:00";
    public bool HasBreak = true, ManualDays = false, AlwaysOnTop = true;
    public int WorkMode = 1, Days = 22, SingleRest = 0, X = -1, Y = -1;
    public int Theme = 0;
    public int WidgetSize = 0;
    public bool RemindersEnabled = false;
    public int ReminderMode = 0;
    public int WaterIntervalMinutes = 30, RestroomIntervalMinutes = 30;
    public bool DailySummaryEnabled = true;
    public string LastSummaryDate = "";
    public string ManualMonth = DateTime.Today.ToString("yyyy-MM");
    public DateTime DoubleSaturday = new DateTime(2026, 10, 31);

    public Settings Copy() { return (Settings)MemberwiseClone(); }
    public void Validate()
    {
        TimeSpan a, b, c, d;
        if (!TimeSpan.TryParseExact(Start, @"hh\:mm", CultureInfo.InvariantCulture, out a) ||
            !TimeSpan.TryParseExact(End, @"hh\:mm", CultureInfo.InvariantCulture, out b) || b <= a || b.TotalHours >= 24)
            throw new Exception("下班时间需晚于上班时间，请填写同一天内的班次。");
        if (HasBreak && (!TimeSpan.TryParseExact(BreakStart, @"hh\:mm", CultureInfo.InvariantCulture, out c) ||
            !TimeSpan.TryParseExact(BreakEnd, @"hh\:mm", CultureInfo.InvariantCulture, out d) || c < a || d > b || d <= c || d - c >= b - a))
            throw new Exception("午休需在上下班之间，结束时间晚于开始时间，并保留有效工时。");
        if (MonthlySalary < 0 || MonthlySalary > 100000000 || Days < 1 || Days > 31 || WorkMode < 0 || WorkMode > 2 || SingleRest < 0 || SingleRest > 6 || Theme < 0 || Theme > 1)
            throw new Exception("月薪、工作天数或休息制设置无效。");
        if (ReminderMode < 0 || ReminderMode > 2) throw new Exception("休息提醒方式无效。");
        if (WaterIntervalMinutes < 1 || WaterIntervalMinutes > 1440 || RestroomIntervalMinutes < 1 || RestroomIntervalMinutes > 1440)
            throw new Exception("喝水和如厕间隔需在1至1440分钟之间。");
        if (WidgetSize < 0 || WidgetSize > 1) throw new Exception("挂件大小设置无效。");
        if (DoubleSaturday.DayOfWeek != DayOfWeek.Saturday)
            throw new Exception("大小周的参考日期请选择一个双休周的周六。");
    }
}

public sealed class Snapshot
{
    public bool WorkingDay;
    public double Total, Elapsed, Progress, Remaining;
    public int MonthDays;
    public decimal Daily, Earned, PerSecond;
    public string Status;
}

public static class WorkClock
{
    public static bool IsWorkingDay(Settings s, DateTime date)
    {
        if (s.WorkMode == 0) return (int)date.DayOfWeek != s.SingleRest;
        if (date.DayOfWeek == DayOfWeek.Sunday) return false;
        if (s.WorkMode == 1) return date.DayOfWeek != DayOfWeek.Saturday;
        // 以用户指定的双休周周六为锚点；负周数同样保持单双周交替。
        DateTime monday = date.Date.AddDays(-(((int)date.DayOfWeek + 6) % 7));
        DateTime anchor = s.DoubleSaturday.Date.AddDays(-5);
        int weeks = (int)((monday - anchor).TotalDays / 7);
        return date.DayOfWeek != DayOfWeek.Saturday || weeks % 2 != 0;
    }

    public static int CountDays(Settings s, DateTime month)
    {
        int days = 0;
        for (int n = 1; n <= DateTime.DaysInMonth(month.Year, month.Month); n++)
            if (IsWorkingDay(s, new DateTime(month.Year, month.Month, n))) days++;
        return days;
    }

    public static Snapshot At(Settings s, DateTime now)
    {
        DateTime start = now.Date.Add(TimeSpan.Parse(s.Start)), end = now.Date.Add(TimeSpan.Parse(s.End));
        double total = (end - start).TotalSeconds;
        double elapsed = Math.Max(0, Math.Min(total, (now - start).TotalSeconds));
        DateTime pause = start, resume = start;
        if (s.HasBreak)
        {
            pause = now.Date.Add(TimeSpan.Parse(s.BreakStart));
            resume = now.Date.Add(TimeSpan.Parse(s.BreakEnd));
            // 直接根据时间求交集，避免程序关闭、休眠或午休时误累计。
            elapsed -= Math.Max(0, Math.Min((resume - pause).TotalSeconds, (now - pause).TotalSeconds));
            total -= (resume - pause).TotalSeconds;
        }
        Snapshot result = new Snapshot();
        result.WorkingDay = IsWorkingDay(s, now);
        result.Total = total;
        result.Elapsed = result.WorkingDay ? Math.Max(0, Math.Min(total, elapsed)) : 0;
        result.Progress = result.Elapsed / total;
        result.Remaining = total - result.Elapsed;
        result.MonthDays = s.ManualDays && s.ManualMonth == now.ToString("yyyy-MM") ? s.Days : CountDays(s, now);
        result.Daily = s.MonthlySalary / result.MonthDays;
        result.PerSecond = result.Daily / (decimal)total;
        result.Earned = result.Daily * (decimal)result.Progress;
        result.Status = !result.WorkingDay ? "今日休息" : now < start ? "尚未开工" : now >= end ? "今日已下班" : s.HasBreak && now >= pause && now < resume ? "午休充电中" : "正在积累小确幸";
        return result;
    }
    public static bool SummaryDue(Settings settings, DateTime now)
    {
        // 只补发当天的总结，日期随配置保存，重启和重新打开设置都不会重复弹出。
        return settings.DailySummaryEnabled && settings.MonthlySalary > 0 && IsWorkingDay(settings, now) &&
            now.TimeOfDay >= TimeSpan.Parse(settings.End) && settings.LastSummaryDate != now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
    }
}

public sealed class ReminderClock
{
    private DateTime last;
    private double waterSeconds, restroomSeconds;
    public ReminderClock(DateTime now) { Reset(now); }
    public void Reset(DateTime now) { last = now; waterSeconds = restroomSeconds = 0; }
    public string Poll(Settings settings, DateTime now)
    {
        if (!settings.RemindersEnabled || now.Date != last.Date || now < last)
        { Reset(now); return null; }
        Snapshot current = WorkClock.At(settings, now);
        double delta = Math.Max(0, current.Elapsed - WorkClock.At(settings, last).Elapsed);
        last = now;
        if (!current.WorkingDay || now.TimeOfDay < TimeSpan.Parse(settings.Start) || now.TimeOfDay >= TimeSpan.Parse(settings.End))
        { waterSeconds = restroomSeconds = 0; return null; }
        if (settings.ReminderMode != 2) waterSeconds += delta;
        if (settings.ReminderMode != 1) restroomSeconds += delta;
        if (settings.HasBreak && now.TimeOfDay >= TimeSpan.Parse(settings.BreakStart) && now.TimeOfDay < TimeSpan.Parse(settings.BreakEnd)) return null;
        bool water = settings.ReminderMode != 2 && waterSeconds >= settings.WaterIntervalMinutes * 60;
        bool restroom = settings.ReminderMode != 1 && restroomSeconds >= settings.RestroomIntervalMinutes * 60;
        if (!water && !restroom) return null;
        // 两个间隔互不重置；同时到期合并提示，休眠恢复时也不连续补弹。
        if (water) waterSeconds = 0;
        if (restroom) restroomSeconds = 0;
        if (water && restroom) return "喝点水；如有需要，起身去趟洗手间。";
        return water ? "喝水时间到了，给自己补充一点水分。" : "如有需要，起身去趟洗手间吧。";
    }
}

public static class Storage
{
    public static readonly string PathName = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "settings.xml");
    public static Settings Load()
    {
        if (!File.Exists(PathName)) return new Settings();
        using (FileStream stream = File.OpenRead(PathName))
        {
            Settings s = (Settings)new XmlSerializer(typeof(Settings)).Deserialize(stream);
            s.Validate();
            return s;
        }
    }
    public static void Save(Settings settings)
    {
        settings.Validate();
        string temp = PathName + ".tmp";
        using (StreamWriter writer = new StreamWriter(temp, false, new UTF8Encoding(false)))
            new XmlSerializer(typeof(Settings)).Serialize(writer, settings);
        // 同盘替换，保留上一份设置，写入中断时不会截断原配置。
        if (File.Exists(PathName)) File.Replace(temp, PathName, PathName + ".bak");
        else File.Move(temp, PathName);
    }
}

public sealed class SettingsForm : Form
{
    public Settings Result;
    private readonly Settings original;
    private NumericUpDown salary, dayCount, waterInterval, restroomInterval;
    private ComboBox mode, rest, reminderMode, widgetSize;
    private DateTimePicker start, end, pause, resume, anchor;
    private CheckBox lunch, manual, onTop, reminders, dailySummary;
    private Label calculated;
    private readonly Color ink = Color.FromArgb(32, 47, 44);

    public SettingsForm(Settings settings)
    {
        original = settings;
        Text = "设置 · 上班鼓励器";
        Font = new Font("Microsoft YaHei UI", 10);
        BackColor = Color.FromArgb(245, 248, 245);
        ForeColor = ink;
        ClientSize = new Size(470, 854);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false; MinimizeBox = false; StartPosition = FormStartPosition.CenterScreen;
        AutoScaleMode = AutoScaleMode.Dpi;
        Label title = LabelAt("把工作节奏，调成你的", 24, 17, 420);
        title.Font = new Font(Font.FontFamily, 16, FontStyle.Bold);
        LabelAt("按时间估算收入，设置仅保存在本机。", 24, 52, 420);
        LabelAt("月薪 / 元", 24, 99, 150);
        salary = new NumericUpDown { Location = new Point(197, 96), Width = 244, Maximum = 100000000, DecimalPlaces = 2, ThousandsSeparator = true, Value = settings.MonthlySalary };
        Controls.Add(salary);
        LabelAt("每周休息制", 24, 145, 150);
        mode = new ComboBox { Location = new Point(197, 142), Width = 244, DropDownStyle = ComboBoxStyle.DropDownList };
        mode.Items.AddRange(new object[] { "单休", "双休（周六、周日）", "大小周（交替单双休）" });
        mode.SelectedIndex = settings.WorkMode; Controls.Add(mode);
        LabelAt("单休休息日", 24, 190, 150);
        rest = new ComboBox { Location = new Point(197, 187), Width = 244, DropDownStyle = ComboBoxStyle.DropDownList };
        rest.Items.AddRange(new object[] { "周日", "周一", "周二", "周三", "周四", "周五", "周六" });
        rest.SelectedIndex = settings.SingleRest; Controls.Add(rest);
        LabelAt("某个双休周的周六", 24, 235, 170);
        anchor = new DateTimePicker { Location = new Point(197, 232), Width = 244, Format = DateTimePickerFormat.Custom, CustomFormat = "yyyy-MM-dd", Value = settings.DoubleSaturday };
        Controls.Add(anchor);
        manual = new CheckBox { Text = "手动指定本月工作天数", Location = new Point(24, 279), Size = new Size(238, 28), Checked = settings.ManualDays && settings.ManualMonth == DateTime.Today.ToString("yyyy-MM") };
        Controls.Add(manual);
        dayCount = new NumericUpDown { Location = new Point(300, 279), Width = 141, Minimum = 1, Maximum = 31, Value = settings.Days }; Controls.Add(dayCount);
        calculated = LabelAt("", 24, 315, 422); calculated.ForeColor = Color.FromArgb(85, 103, 94);
        LabelAt("上班 / 下班", 24, 361, 150);
        start = TimePicker(settings.Start, 197, 358); end = TimePicker(settings.End, 329, 358);
        lunch = new CheckBox { Text = "扣除午休", Location = new Point(24, 403), Size = new Size(160, 29), Checked = settings.HasBreak }; Controls.Add(lunch);
        pause = TimePicker(settings.BreakStart, 197, 403); resume = TimePicker(settings.BreakEnd, 329, 403);
        LabelAt("挂件大小", 24, 454, 105);
        widgetSize = new ComboBox { Name = "WidgetSize", Location = new Point(130, 451), Width = 175, DropDownStyle = ComboBoxStyle.DropDownList };
        widgetSize.Items.AddRange(new object[] { "紧凑 300×360", "迷你 260×300" }); widgetSize.SelectedIndex = settings.WidgetSize; Controls.Add(widgetSize);
        onTop = new CheckBox { Text = "保持置顶", Location = new Point(315, 454), Size = new Size(126, 29), Checked = settings.AlwaysOnTop }; Controls.Add(onTop);
        reminders = new CheckBox { Text = "开启休息提醒", Location = new Point(24, 491), Size = new Size(280, 29), Checked = settings.RemindersEnabled }; Controls.Add(reminders);
        Button previewReminder = new Button { Text = "预览提醒", Location = new Point(315, 491), Size = new Size(126, 29), FlatStyle = FlatStyle.Flat }; Controls.Add(previewReminder);
        LabelAt("提醒方式", 24, 535, 150);
        reminderMode = new ComboBox { Location = new Point(197, 532), Width = 244, DropDownStyle = ComboBoxStyle.DropDownList };
        reminderMode.Items.AddRange(new object[] { "分别提醒喝水 / 如厕", "只提醒喝水", "只提醒如厕" }); reminderMode.SelectedIndex = settings.ReminderMode; Controls.Add(reminderMode);
        LabelAt("喝水间隔 / 分", 24, 576, 127);
        waterInterval = new NumericUpDown { Name = "WaterInterval", Location = new Point(155, 574), Width = 62, Minimum = 1, Maximum = 1440, Value = settings.WaterIntervalMinutes }; Controls.Add(waterInterval);
        LabelAt("如厕间隔 / 分", 236, 576, 127);
        restroomInterval = new NumericUpDown { Name = "RestroomInterval", Location = new Point(379, 574), Width = 62, Minimum = 1, Maximum = 1440, Value = settings.RestroomIntervalMinutes }; Controls.Add(restroomInterval);
        dailySummary = new CheckBox { Name = "DailySummary", Text = "下班时弹窗汇总今日收入", Location = new Point(24, 617), Size = new Size(410, 29), Checked = settings.DailySummaryEnabled }; Controls.Add(dailySummary);
        Label reminderNote = LabelAt("间隔按上班时间累计，午休暂停，同时到期合并。\n下班总结当天补发一次；保存或重启重计休息间隔。", 24, 655, 422);
        reminderNote.Size = new Size(422, 50); reminderNote.Font = new Font(Font.FontFamily, 9);
        Label note = LabelAt("自动天数不含节假日和调休；手动覆盖仅当月有效。\n手动天数影响日薪；每日是否上班仍按休息制判断。", 24, 718, 422);
        note.Size = new Size(422, 52); note.Font = new Font(Font.FontFamily, 9); note.ForeColor = calculated.ForeColor;
        Button save = new Button { Text = "保存设置", Location = new Point(252, 787), Size = new Size(189, 43), BackColor = Color.FromArgb(31, 94, 74), ForeColor = Color.White, FlatStyle = FlatStyle.Flat };
        save.FlatAppearance.BorderSize = 0; Controls.Add(save);
        Button cancel = new Button { Text = "取消", Location = new Point(142, 787), Size = new Size(94, 43), DialogResult = DialogResult.Cancel }; Controls.Add(cancel);
        AcceptButton = save; CancelButton = cancel;
        if (settings.Theme == 0)
        {
            BackColor = Color.FromArgb(18, 23, 43); ForeColor = Color.FromArgb(223, 232, 250);
            foreach (Control control in Controls)
            {
                if (control is NumericUpDown || control is ComboBox || control is DateTimePicker)
                { control.BackColor = Color.FromArgb(32, 42, 67); control.ForeColor = ForeColor; }
                else if (control is Label || control is CheckBox) control.ForeColor = Color.FromArgb(183, 201, 227);
            }
            save.BackColor = Color.FromArgb(46, 100, 179); cancel.BackColor = Color.FromArgb(32, 42, 67); cancel.ForeColor = ForeColor;
        }
        else
        {
            BackColor = Color.FromArgb(252, 241, 249); ForeColor = Color.FromArgb(86, 55, 91);
            save.BackColor = Color.FromArgb(157, 77, 139);
        }
        // 原生下拉框在深色主题中仍可能使用系统白底，统一绘制选项以保证文字对比度。
        previewReminder.BackColor = BackColor; previewReminder.ForeColor = ForeColor;
        previewReminder.Click += delegate
        {
            string message = reminderMode.SelectedIndex == 0 ? "喝点水；如有需要，起身去趟洗手间。" : reminderMode.SelectedIndex == 2 ? "如有需要，起身去趟洗手间吧。" : "喝水时间到了，给自己补充一点水分。";
            ReminderPopup popup = new ReminderPopup(message, settings.Theme);
            popup.FormClosed += delegate { popup.Dispose(); }; popup.Show();
        };
        foreach (ComboBox combo in new ComboBox[] { mode, rest, reminderMode, widgetSize })
        {
            combo.DrawMode = DrawMode.OwnerDrawFixed; combo.ItemHeight = 24;
            combo.DrawItem += delegate(object sender, DrawItemEventArgs e)
            {
                ComboBox current = (ComboBox)sender;
                if (e.Index < 0) return;
                Color surface = settings.Theme == 0 ? Color.FromArgb(32, 42, 67) : Color.FromArgb(255, 248, 254);
                Color textColor = settings.Theme == 0 ? Color.FromArgb(223, 232, 250) : Color.FromArgb(86, 55, 91);
                using (SolidBrush brush = new SolidBrush(surface)) e.Graphics.FillRectangle(brush, e.Bounds);
                TextRenderer.DrawText(e.Graphics, current.Items[e.Index].ToString(), current.Font, e.Bounds, textColor, TextFormatFlags.Left | TextFormatFlags.VerticalCenter);
                e.DrawFocusRectangle();
            };
        }
        EventHandler update = delegate
        {
            rest.Enabled = mode.SelectedIndex == 0; anchor.Enabled = mode.SelectedIndex == 2;
            dayCount.Enabled = manual.Checked; pause.Enabled = resume.Enabled = lunch.Checked;
            reminderMode.Enabled = reminders.Checked;
            waterInterval.Enabled = reminders.Checked && reminderMode.SelectedIndex != 2;
            restroomInterval.Enabled = reminders.Checked && reminderMode.SelectedIndex != 1;
            Settings preview = original.Copy(); preview.WorkMode = mode.SelectedIndex; preview.SingleRest = rest.SelectedIndex;
            // 预览不修改保存的锚点；日期输入是否合法由保存时统一校验。
            preview.DoubleSaturday = anchor.Value.Date.AddDays((6 - (int)anchor.Value.DayOfWeek + 7) % 7);
        calculated.Text = string.Format("{0:yyyy年M月}按休息制计算：{1} 个工作日", DateTime.Now, WorkClock.CountDays(preview, DateTime.Now));
        };
        mode.SelectedIndexChanged += update; rest.SelectedIndexChanged += update; anchor.ValueChanged += update;
        manual.CheckedChanged += update; lunch.CheckedChanged += update; update(this, EventArgs.Empty);
        reminders.CheckedChanged += update;
        reminderMode.SelectedIndexChanged += update;
        save.Click += delegate
        {
            Settings next = original.Copy();
            next.MonthlySalary = salary.Value; next.WorkMode = mode.SelectedIndex; next.SingleRest = rest.SelectedIndex;
            next.DoubleSaturday = anchor.Value.Date; next.ManualDays = manual.Checked; next.Days = (int)dayCount.Value;
            next.ManualMonth = DateTime.Today.ToString("yyyy-MM");
            next.Start = start.Value.ToString("HH:mm"); next.End = end.Value.ToString("HH:mm");
            next.HasBreak = lunch.Checked; next.BreakStart = pause.Value.ToString("HH:mm"); next.BreakEnd = resume.Value.ToString("HH:mm"); next.AlwaysOnTop = onTop.Checked;
            next.RemindersEnabled = reminders.Checked; next.ReminderMode = reminderMode.SelectedIndex;
            next.WaterIntervalMinutes = (int)waterInterval.Value; next.RestroomIntervalMinutes = (int)restroomInterval.Value;
            next.DailySummaryEnabled = dailySummary.Checked;
            next.WidgetSize = widgetSize.SelectedIndex;
            try { Storage.Save(next); Result = next; DialogResult = DialogResult.OK; Close(); }
            catch (Exception ex) { MessageBox.Show(this, ex.Message, "设置未保存", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
        };
    }
    private Label LabelAt(string text, int x, int y, int width)
    {
        Label label = new Label { Text = text, Location = new Point(x, y), Size = new Size(width, 31), TextAlign = ContentAlignment.MiddleLeft };
        Controls.Add(label); return label;
    }
    private DateTimePicker TimePicker(string time, int x, int y)
    {
        DateTimePicker picker = new DateTimePicker { Location = new Point(x, y), Width = 112, Format = DateTimePickerFormat.Custom, CustomFormat = "HH:mm", ShowUpDown = true, Value = DateTime.Today.Add(TimeSpan.Parse(time)) };
        Controls.Add(picker); return picker;
    }
}

public sealed class ReminderPopup : Form
{
    private readonly System.Windows.Forms.Timer dismiss = new System.Windows.Forms.Timer();
    private readonly Color accent;
    private readonly bool summary;
    public ReminderPopup(string message, int theme, bool dailySummary = false)
    {
        summary = dailySummary;
        bool pink = theme == 1;
        Text = summary ? "下班总结 · 上班鼓励器" : "休息提醒 · 上班鼓励器"; ClientSize = new Size(350, 177);
        AutoScaleMode = AutoScaleMode.None; FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.Manual; ShowInTaskbar = false; TopMost = true;
        BackColor = pink ? Color.FromArgb(253, 239, 250) : Color.FromArgb(20, 30, 53);
        ForeColor = pink ? Color.FromArgb(83, 46, 89) : Color.FromArgb(234, 243, 255);
        accent = pink ? Color.FromArgb(174, 67, 139) : Color.FromArgb(94, 224, 255);
        Font = new Font("Microsoft YaHei UI", 10); DoubleBuffered = true;
        Rectangle area = Screen.FromPoint(Cursor.Position).WorkingArea;
        Location = new Point(area.Right - Width - 20, area.Bottom - Height - 20);
        using (GraphicsPath path = Widget.Rounded(ClientRectangle, 18)) Region = new Region(path);
        Controls.Add(new Label { Text = summary ? "下班啦，今天辛苦了！" : "该给自己一点休息时间了", Font = new Font(Font.FontFamily, 11, FontStyle.Bold), Bounds = new Rectangle(72, 24, 260, 27), ForeColor = ForeColor });
        Controls.Add(new Label { Text = message, Bounds = new Rectangle(72, 61, 258, 47), ForeColor = ForeColor });
        Button close = new Button { Text = "知道了", Bounds = new Rectangle(232, 126, 96, 31), FlatStyle = FlatStyle.Flat, BackColor = BackColor, ForeColor = accent, Cursor = Cursors.Hand };
        close.FlatAppearance.BorderColor = accent; close.Click += delegate { Close(); }; Controls.Add(close);
        Controls.Add(new Label { Text = "15 秒后自动关闭", Bounds = new Rectangle(22, 132, 180, 25), ForeColor = accent, Font = new Font(Font.FontFamily, 8) });
        dismiss.Interval = 15000; dismiss.Tick += delegate { Close(); };
        Shown += delegate { dismiss.Start(); };
    }
    // 提醒可见，但不抢走当前输入焦点，也不阻塞主窗口。
    protected override bool ShowWithoutActivation { get { return true; } }
    protected override CreateParams CreateParams
    {
        get { CreateParams value = base.CreateParams; value.ExStyle |= 0x08000000; return value; }
    }
    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e); Graphics g = e.Graphics; g.SmoothingMode = SmoothingMode.AntiAlias;
        using (Pen pen = new Pen(accent, 2))
        {
            using (GraphicsPath path = Widget.Rounded(new Rectangle(0, 0, Width - 1, Height - 1), 18)) g.DrawPath(pen, path);
            if (summary)
            {
                g.DrawEllipse(pen, 25, 36, 33, 33);
                TextRenderer.DrawText(g, "¥", Font, new Rectangle(25, 36, 33, 33), accent, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            }
            else
            {
                g.DrawLine(pen, 26, 38, 30, 68); g.DrawLine(pen, 30, 68, 49, 68); g.DrawLine(pen, 49, 68, 53, 38); g.DrawLine(pen, 26, 38, 53, 38);
                g.DrawArc(pen, 46, 43, 15, 17, -90, 180); g.DrawLine(pen, 31, 51, 48, 51);
            }
        }
    }
    protected override void Dispose(bool disposing)
    {
        if (disposing) dismiss.Dispose(); base.Dispose(disposing);
    }
}

public sealed class Widget : Form
{
    private Settings settings;
    private readonly System.Windows.Forms.Timer timer = new System.Windows.Forms.Timer();
    private readonly System.Windows.Forms.Timer reminderTimer = new System.Windows.Forms.Timer();
    private readonly ReminderClock reminderClock = new ReminderClock(DateTime.Now);
    private ReminderPopup reminderPopup;
    private NotifyIcon tray;
    private Icon appIcon;
    private bool dragging, exiting;
    private Point dragOffset;
    private Color background, accent, secondary, ink, muted, card;
    private static readonly string[] phrases = { "每一点努力，都在靠近生活的小目标。", "进度慢慢走，属于你的时间会到来。", "工作有进度，生活也要留一点甜。", "认真上班，也记得适时起身喝水。", "今天的付出，正在变成生活的底气。" };

    public Widget(Settings s)
    {
        settings = s;
        Text = "上班鼓励器"; ClientSize = settings.WidgetSize == 1 ? new Size(260, 300) : new Size(300, 360);
        FormBorderStyle = FormBorderStyle.None; StartPosition = FormStartPosition.Manual;
        DoubleBuffered = true; ShowInTaskbar = false;
        ApplyTheme();
        AutoScaleMode = AutoScaleMode.None; TopMost = settings.AlwaysOnTop;
        Rectangle area = Screen.PrimaryScreen.WorkingArea;
        Location = s.X >= 0 ? new Point(s.X, s.Y) : new Point(area.Right - Width - 30, area.Top + 60);
        bool visible = false;
        foreach (Screen screen in Screen.AllScreens) if (Rectangle.Intersect(screen.WorkingArea, Bounds).Width > 80 && Rectangle.Intersect(screen.WorkingArea, Bounds).Height > 50) visible = true;
        if (!visible) Location = new Point(area.Right - Width - 30, area.Top + 60);
        string iconPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "app.ico");
        appIcon = File.Exists(iconPath) ? new Icon(iconPath) : (Icon)SystemIcons.Information.Clone(); Icon = appIcon;
        tray = new NotifyIcon { Icon = appIcon, Text = "上班鼓励器 · 双击显示", Visible = true };
        ContextMenuStrip menu = new ContextMenuStrip();
        menu.Items.Add("显示挂件", null, delegate { Show(); Activate(); });
        menu.Items.Add("设置", null, delegate { OpenSettings(); });
        menu.Items.Add("退出", null, delegate { exiting = true; Close(); });
        tray.ContextMenuStrip = menu; ContextMenuStrip = menu;
        tray.DoubleClick += delegate { Show(); Activate(); };
        AddButton("⚙ 设置", 0, 0, 66, delegate { OpenSettings(); });
        AddButton("收起", 0, 0, 66, delegate { Hide(); });
        AddButton("◇ 换主题", 0, 0, 98, delegate
        {
            Settings next = settings.Copy(); next.Theme = 1 - next.Theme;
            try { Storage.Save(next); settings = next; ApplyTheme(); Invalidate(); }
            catch (Exception ex) { MessageBox.Show(this, ex.Message, "主题未保存"); }
        });
        AddButton(s.AlwaysOnTop ? "已置顶" : "置顶", 0, 0, 66, delegate(object sender, EventArgs e)
        {
            Settings next = settings.Copy(); next.AlwaysOnTop = !next.AlwaysOnTop;
            try { Storage.Save(next); settings = next; TopMost = next.AlwaysOnTop; ((Button)sender).Text = next.AlwaysOnTop ? "已置顶" : "置顶"; }
            catch (Exception ex) { MessageBox.Show(this, ex.Message, "置顶设置未保存"); }
        });
        ApplySize();
        timer.Interval = 100; timer.Tick += delegate { if (Visible) Invalidate(); }; timer.Start();
        reminderTimer.Interval = 1000;
        reminderTimer.Tick += delegate
        {
            DateTime now = DateTime.Now;
            bool summary = WorkClock.SummaryDue(settings, now);
            string message;
            if (summary)
            {
                message = "你今日赚了 ¥" + WorkClock.At(settings, now).Earned.ToString("N2") + "（估算）。";
                settings.LastSummaryDate = now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
                try { Storage.Save(settings); }
                catch (Exception ex) { MessageBox.Show(this, "下班总结状态未能保存，重启后可能再次提示。\n" + ex.Message, "设置未保存"); }
                if (reminderPopup != null) reminderPopup.Close();
            }
            else message = reminderClock.Poll(settings, now);
            if (message == null || (reminderPopup != null && !reminderPopup.IsDisposed)) return;
            ReminderPopup popup = new ReminderPopup(message, settings.Theme, summary); reminderPopup = popup;
            popup.FormClosed += delegate { popup.Dispose(); if (reminderPopup == popup) reminderPopup = null; };
            popup.Show();
        };
        reminderTimer.Start();
        MouseDown += delegate(object sender, MouseEventArgs e) { if (e.Button == MouseButtons.Left) { dragging = true; dragOffset = e.Location; Capture = true; } };
        MouseMove += delegate { if (dragging) Location = new Point(Cursor.Position.X - dragOffset.X, Cursor.Position.Y - dragOffset.Y); };
        MouseUp += delegate { if (dragging) { dragging = false; Capture = false; SaveLocation(); } };
        Shown += delegate { if (!File.Exists(Storage.PathName)) OpenSettings(); };
    }
    private void AddButton(string text, int x, int y, int width, EventHandler handler)
    {
        Button button = new Button { Text = text, AccessibleName = text, Bounds = new Rectangle(x, y, width, 24), FlatStyle = FlatStyle.Flat, BackColor = background, ForeColor = muted, Cursor = Cursors.Hand, Font = new Font("Microsoft YaHei UI", 8) };
        button.FlatAppearance.BorderSize = 0; button.FlatAppearance.MouseOverBackColor = card;
        button.Click += handler; Controls.Add(button);
    }
    private void ApplySize()
    {
        bool mini = settings.WidgetSize == 1;
        SuspendLayout();
        ClientSize = mini ? new Size(260, 300) : new Size(300, 360);
        foreach (Control control in Controls)
        {
            if (control.Text.Contains("设置")) control.Bounds = new Rectangle(mini ? 184 : 222, mini ? 5 : 7, 66, 24);
            else if (control.Text == "收起") control.Bounds = new Rectangle(mini ? 200 : 222, mini ? 272 : 329, mini ? 48 : 66, 24);
            else if (control.Text.Contains("换主题")) control.Bounds = new Rectangle(mini ? 88 : 101, mini ? 272 : 329, mini ? 88 : 98, 24);
            else control.Bounds = new Rectangle(mini ? 12 : 14, mini ? 272 : 329, mini ? 60 : 66, 24);
        }
        Region previous = Region;
        using (GraphicsPath path = Rounded(ClientRectangle, 22)) Region = new Region(path);
        if (previous != null) previous.Dispose();
        // 从迷你切回紧凑时将窗口限制在屏幕可用区，避免按钮落到屏幕外。
        Rectangle area = Screen.FromRectangle(Bounds).WorkingArea;
        Location = new Point(Math.Max(area.Left, Math.Min(Left, area.Right - Width)), Math.Max(area.Top, Math.Min(Top, area.Bottom - Height)));
        ResumeLayout(); Invalidate();
    }
    private void OpenSettings()
    {
        Show(); TopMost = false;
        using (SettingsForm form = new SettingsForm(settings))
        {
            form.TopMost = settings.AlwaysOnTop;
            if (form.ShowDialog(this) == DialogResult.OK) { settings = form.Result; ApplySize(); reminderClock.Reset(DateTime.Now); }
        }
        TopMost = settings.AlwaysOnTop;
        foreach (Control control in Controls) if (control.Text == "已置顶" || control.Text == "置顶") control.Text = TopMost ? "已置顶" : "置顶";
        ApplyTheme();
        Invalidate();
    }
    private void ApplyTheme()
    {
        bool pink = settings.Theme == 1;
        background = pink ? Color.FromArgb(251, 240, 250) : Color.FromArgb(12, 18, 36);
        card = pink ? Color.FromArgb(246, 222, 244) : Color.FromArgb(24, 34, 60);
        accent = pink ? Color.FromArgb(174, 67, 139) : Color.FromArgb(94, 224, 255);
        secondary = pink ? Color.FromArgb(155, 105, 214) : Color.FromArgb(143, 117, 255);
        ink = pink ? Color.FromArgb(77, 45, 84) : Color.FromArgb(233, 244, 255);
        muted = pink ? Color.FromArgb(141, 105, 145) : Color.FromArgb(144, 165, 197);
        BackColor = background;
        foreach (Control control in Controls)
        {
            control.BackColor = background; control.ForeColor = muted;
            Button button = control as Button; if (button != null) button.FlatAppearance.MouseOverBackColor = card;
        }
    }
    private void SaveLocation()
    {
        Settings next = settings.Copy(); next.X = Left; next.Y = Top;
        try { Storage.Save(next); settings = next; }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, "窗口位置未保存"); }
    }
    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (!exiting && e.CloseReason == CloseReason.UserClosing) { e.Cancel = true; Hide(); return; }
        SaveLocation(); base.OnFormClosing(e);
    }
    protected override void Dispose(bool disposing)
    {
        if (disposing) { timer.Dispose(); reminderTimer.Dispose(); if (reminderPopup != null) reminderPopup.Dispose(); if (tray != null) { tray.Visible = false; tray.Dispose(); } if (appIcon != null) appIcon.Dispose(); }
        base.Dispose(disposing);
    }
    public static GraphicsPath Rounded(Rectangle rect, int radius)
    {
        GraphicsPath path = new GraphicsPath(); int size = radius * 2;
        path.AddArc(rect.Left, rect.Top, size, size, 180, 90); path.AddArc(rect.Right - size, rect.Top, size, size, 270, 90);
        path.AddArc(rect.Right - size, rect.Bottom - size, size, size, 0, 90); path.AddArc(rect.Left, rect.Bottom - size, size, size, 90, 90); path.CloseFigure(); return path;
    }
    private void Write(Graphics g, string text, float size, Color color, RectangleF box, FontStyle style, StringAlignment alignment)
    {
        using (Font font = new Font("Microsoft YaHei UI", size, style))
        using (SolidBrush brush = new SolidBrush(color))
        using (StringFormat format = new StringFormat { Alignment = alignment, LineAlignment = StringAlignment.Center, FormatFlags = StringFormatFlags.NoWrap })
            g.DrawString(text, font, brush, box, format);
    }
    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e); Graphics g = e.Graphics; g.SmoothingMode = SmoothingMode.AntiAlias; g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
        DateTime now = DateTime.Now; Snapshot value = WorkClock.At(settings, now);
        bool pink = settings.Theme == 1, mini = Width < 280;
        double phase = now.TimeOfDay.TotalSeconds;
        int pad = mini ? 10 : 12;
        // 紧凑布局重新排布各区域，保留可读字号与真实按钮，避免整窗缩放导致文字模糊。
        using (LinearGradientBrush wash = new LinearGradientBrush(ClientRectangle, background, card, 62f)) g.FillRectangle(wash, ClientRectangle);
        if (!pink) using (Pen grid = new Pen(Color.FromArgb(14, accent)))
        {
            for (int x = 8; x < Width; x += 24) g.DrawLine(grid, x, 0, x, Height);
            for (int y = 8; y < Height; y += 24) g.DrawLine(grid, 0, y, Width, y);
        }
        for (int i = 0; i < 8; i++)
        {
            float x = 14 + (i * 83 % (Width - 28)), y = 45 + (i * 59 % (Height - 70)) + (float)Math.Sin(phase * .8 + i) * 3;
            using (Pen star = new Pen(Color.FromArgb((int)(45 + 17 * (1 + Math.Sin(phase * 1.2 + i))), secondary)))
            { g.DrawLine(star, x - 2, y, x + 2, y); g.DrawLine(star, x, y - 2, x, y + 2); }
        }
        using (Pen border = new Pen(Color.FromArgb(85, secondary))) using (GraphicsPath path = Rounded(new Rectangle(0, 0, Width - 1, Height - 1), 22)) g.DrawPath(border, path);
        using (SolidBrush logo = new SolidBrush(accent)) g.FillEllipse(logo, mini ? 12 : 14, mini ? 17 : 19, 8, 8);
        Write(g, "上班鼓励器", mini ? 9.5f : 10, ink, new RectangleF(mini ? 27 : 30, mini ? 5 : 7, 175, 27), FontStyle.Bold, StringAlignment.Near);
        Write(g, pink ? "STARLIGHT / 樱粉星光" : "NEON SHIFT / 霓虹科技", mini ? 7 : 7.5f, accent, new RectangleF(pad + 2, mini ? 31 : 39, Width - 25, 18), FontStyle.Bold, StringAlignment.Near);
        Rectangle panel = new Rectangle(pad, mini ? 53 : 64, Width - pad * 2, mini ? 83 : 102);
        using (GraphicsPath path = Rounded(panel, 14))
        using (LinearGradientBrush brush = new LinearGradientBrush(panel, pink ? Color.FromArgb(255, 253, 255) : Color.FromArgb(27, 47, 75), pink ? Color.FromArgb(248, 219, 241) : Color.FromArgb(39, 31, 74), 25f))
        using (Pen outline = new Pen(Color.FromArgb(65, accent))) { g.FillPath(brush, path); g.DrawPath(outline, path); }
        Write(g, "今日收入", mini ? 8 : 8.5f, muted, new RectangleF(pad + 12, panel.Y + 6, 140, 21), FontStyle.Regular, StringAlignment.Near);
        Write(g, "时间折算 · 估算", 7, muted, new RectangleF(Width - 108, panel.Y + 7, 84, 19), FontStyle.Regular, StringAlignment.Far);
        float amountSize = value.Earned >= 1000000 ? (mini ? 14 : 17) : value.Earned >= 100000 ? (mini ? 18 : 21) : (mini ? 24 : 28);
        Write(g, settings.MonthlySalary == 0 ? "¥ —" : "¥ " + value.Earned.ToString("N2"), amountSize, ink, new RectangleF(pad + 8, mini ? 80 : 96, Width - 76, mini ? 37 : 43), FontStyle.Bold, StringAlignment.Near);
        int coinSize = mini ? 27 : 32, coinX = Width - pad - coinSize - 8;
        float coinY = (mini ? 88 : 106) + (float)Math.Sin(phase * 1.4) * 2;
        using (SolidBrush shadow = new SolidBrush(Color.FromArgb(24, secondary))) g.FillEllipse(shadow, coinX, coinY + coinSize + 2, coinSize, 5);
        using (LinearGradientBrush gold = new LinearGradientBrush(new Rectangle(coinX, (int)coinY, coinSize, coinSize), pink ? Color.FromArgb(255, 222, 158) : accent, secondary, 50f)) g.FillEllipse(gold, coinX, coinY, coinSize, coinSize);
        using (Pen coin = new Pen(Color.FromArgb(160, Color.White))) g.DrawEllipse(coin, coinX + 3, coinY + 3, coinSize - 6, coinSize - 6);
        Write(g, "¥", mini ? 12 : 14, pink ? Color.FromArgb(133, 77, 110) : Color.FromArgb(15, 37, 66), new RectangleF(coinX, coinY, coinSize, coinSize), FontStyle.Bold, StringAlignment.Center);
        Write(g, settings.MonthlySalary == 0 ? "设置薪资后显示每秒收入" : "每秒收入 ¥ " + value.PerSecond.ToString(value.PerSecond >= 0.0001m ? "0.0000" : "0.0000########"), mini ? 7.5f : 8, accent, new RectangleF(pad + 12, mini ? 118 : 141, Width - 40, 19), FontStyle.Regular, StringAlignment.Near);
        int rx = mini ? 20 : 24, ry = mini ? 154 : 187, diameter = mini ? 66 : 82;
        Rectangle ring = new Rectangle(rx, ry, diameter, diameter);
        using (Pen track = new Pen(Color.FromArgb(30, secondary), mini ? 6 : 7)) g.DrawEllipse(track, ring);
        using (Pen glow = new Pen(Color.FromArgb(35, accent), mini ? 12 : 14)) if (value.Progress > 0) g.DrawArc(glow, ring, -90, (float)value.Progress * 360);
        using (LinearGradientBrush gradient = new LinearGradientBrush(ring, accent, secondary, 70f))
        using (Pen progress = new Pen(gradient, mini ? 6 : 7))
        {
            progress.StartCap = progress.EndCap = LineCap.Round;
            if (value.Progress > 0) g.DrawArc(progress, ring, -90, (float)value.Progress * 360);
        }
        using (Pen orbit = new Pen(Color.FromArgb(150, secondary), 1.5f)) g.DrawArc(orbit, new Rectangle(rx - 8, ry - 8, diameter + 16, diameter + 16), (float)(phase * 16 % 360), 34);
        Write(g, value.Progress.ToString("P1"), mini ? 14 : 17, ink, new RectangleF(rx - 6, mini ? 169 : 208, diameter + 12, 30), FontStyle.Bold, StringAlignment.Center);
        Write(g, "今日进度", mini ? 7 : 7.5f, muted, new RectangleF(rx - 6, mini ? 195 : 239, diameter + 12, 18), FontStyle.Regular, StringAlignment.Center);
        int right = mini ? 105 : 127;
        Write(g, value.Status, mini ? 8 : 8.5f, accent, new RectangleF(right, mini ? 145 : 182, Width - right - 12, 22), FontStyle.Bold, StringAlignment.Near);
        Write(g, "剩余有效工时", mini ? 7 : 7.5f, muted, new RectangleF(right, mini ? 168 : 210, Width - right - 12, 18), FontStyle.Regular, StringAlignment.Near);
        string duration = !value.WorkingDay ? "休息日" : string.Format("{0:00}:{1:00}:{2:00}", (int)(value.Remaining / 3600), (int)(value.Remaining / 60) % 60, (int)value.Remaining % 60);
        Write(g, duration, mini ? 17 : 19, ink, new RectangleF(right - 2, mini ? 186 : 231, Width - right - 10, 31), FontStyle.Bold, StringAlignment.Near);
        Write(g, settings.Start + " — " + settings.End, mini ? 7.5f : 8, muted, new RectangleF(right, mini ? 211 : 266, Width - right - 12, 19), FontStyle.Regular, StringAlignment.Near);
        int quoteY = mini ? 232 : 285;
        using (GraphicsPath quote = Rounded(new Rectangle(pad, quoteY, Width - pad * 2, mini ? 23 : 29), 9)) using (SolidBrush brush = new SolidBrush(Color.FromArgb(pink ? 155 : 115, background))) g.FillPath(brush, quote);
        Write(g, pink ? "✧" : "✦", 11, secondary, new RectangleF(pad + 3, quoteY, 19, mini ? 23 : 29), FontStyle.Regular, StringAlignment.Center);
        Write(g, phrases[(now.DayOfYear + now.Hour / 2) % phrases.Length], mini ? 7 : 7.5f, ink, new RectangleF(pad + 24, quoteY, Width - pad * 2 - 27, mini ? 23 : 29), FontStyle.Regular, StringAlignment.Near);
        Write(g, now.ToString("M月d日 dddd", CultureInfo.GetCultureInfo("zh-CN")) + " · 本月 " + value.MonthDays + " 个工作日", mini ? 7 : 7.5f, muted, new RectangleF(pad + 2, mini ? 255 : 310, Width - pad * 2 - 4, 18), FontStyle.Regular, StringAlignment.Center);
    }
}

public static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false);
        if (args.Length > 0 && args[0] == "--self-test")
        {
            Tests.Run(); return;
        }
        if (args.Length > 0 && args[0] == "--preview")
        {
            for (int theme = 0; theme < 2; theme++)
            for (int size = 0; size < 2; size++)
                using (Widget preview = new Widget(new Settings { MonthlySalary = 8800, Theme = theme, WidgetSize = size, DailySummaryEnabled = false }))
                using (Bitmap bitmap = new Bitmap(preview.Width, preview.Height))
                {
                    preview.Opacity = 0; preview.Show(); Application.DoEvents(); preview.DrawToBitmap(bitmap, preview.ClientRectangle); preview.Hide();
                    bitmap.Save(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, (size == 0 ? "compact-" : "mini-") + (theme == 0 ? "neon.png" : "starlight.png")));
                }
            for (int theme = 0; theme < 2; theme++)
                using (ReminderPopup preview = new ReminderPopup("喝点水；如有需要，起身去趟洗手间。", theme))
                using (Bitmap bitmap = new Bitmap(preview.Width, preview.Height))
                {
                    preview.Opacity = 0; preview.Show(); Application.DoEvents(); preview.DrawToBitmap(bitmap, preview.ClientRectangle); preview.Hide();
                    bitmap.Save(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, theme == 0 ? "reminder-neon.png" : "reminder-starlight.png"));
                }
            for (int theme = 0; theme < 2; theme++)
                using (ReminderPopup preview = new ReminderPopup("你今日赚了 ¥400.00（估算）。", theme, true))
                using (Bitmap bitmap = new Bitmap(preview.Width, preview.Height))
                {
                    preview.Opacity = 0; preview.Show(); Application.DoEvents(); preview.DrawToBitmap(bitmap, preview.ClientRectangle); preview.Hide();
                    bitmap.Save(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, theme == 0 ? "summary-neon.png" : "summary-starlight.png"));
                }
            using (SettingsForm preview = new SettingsForm(new Settings { MonthlySalary = 8800 }))
            {
                preview.Opacity = 0; preview.Show(); Application.DoEvents();
                using (Bitmap bitmap = new Bitmap(preview.Width, preview.Height))
                {
                    preview.DrawToBitmap(bitmap, new Rectangle(0, 0, preview.Width, preview.Height));
                    bitmap.Save(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "settings-preview.png"));
                }
                preview.Hide();
            }
            return;
        }
        bool first;
        using (Mutex mutex = new Mutex(true, "Local\\WorkEncourager_DCodex", out first))
        {
            if (!first) { MessageBox.Show("上班鼓励器已在运行。请双击任务栏右下角的托盘图标显示挂件。", "上班鼓励器"); return; }
            Settings settings;
            try { settings = Storage.Load(); }
            catch (Exception ex)
            {
                MessageBox.Show("设置读取失败，暂用初始设置。原设置文件保留，保存新设置时会备份原文件。\n" + ex.Message, "上班鼓励器");
                settings = new Settings();
            }
            using (Widget widget = new Widget(settings)) Application.Run(widget);
        }
    }
}

public static class Tests
{
    public static void Run()
    {
        StringBuilder report = new StringBuilder(); int checks = 0;
        Action<bool, string> check = delegate(bool ok, string name)
        {
            checks++; report.AppendLine((ok ? "PASS " : "FAIL ") + name);
            if (!ok) throw new Exception(name);
        };
        try
        {
            Settings s = new Settings { MonthlySalary = 8800, ManualDays = true, Days = 22, ManualMonth = "2026-11", DailySummaryEnabled = false };
            s.Validate();
            Snapshot v = WorkClock.At(s, new DateTime(2026, 11, 2, 8, 0, 0));
            check(v.Progress == 0 && v.Earned == 0, "Before work stays at zero");
            v = WorkClock.At(s, new DateTime(2026, 11, 2, 9, 0, 0));
            check(v.Progress == 0 && v.Remaining == 28800, "Start boundary and 8 effective hours");
            Snapshot noon = WorkClock.At(s, new DateTime(2026, 11, 2, 12, 0, 0));
            Snapshot lunch = WorkClock.At(s, new DateTime(2026, 11, 2, 12, 45, 0));
            check(noon.Elapsed == 10800 && lunch.Elapsed == noon.Elapsed && lunch.Earned == 150, "Lunch pauses time and income");
            v = WorkClock.At(s, new DateTime(2026, 11, 2, 13, 0, 0));
            check(v.Elapsed == noon.Elapsed, "Lunch end boundary");
            v = WorkClock.At(s, new DateTime(2026, 11, 2, 14, 0, 0));
            check(v.Progress == 0.5 && v.Earned == 200, "Half day produces half daily salary");
            check(v.PerSecond == 400m / 28800m, "Per-second amount uses daily salary and effective working seconds");
            v = WorkClock.At(s, new DateTime(2026, 11, 2, 18, 0, 0));
            check(v.Progress == 1 && v.Earned == 400 && v.Remaining == 0, "End boundary caps income");
            v = WorkClock.At(s, new DateTime(2026, 11, 2, 23, 59, 59));
            check(v.Progress == 1 && v.Earned == 400, "After hours stays capped");
            check(v.PerSecond == 400m / 28800m, "Display rate does not change after clock-out");
            v = WorkClock.At(s, new DateTime(2026, 11, 3, 0, 0, 0));
            check(v.Progress == 0 && v.Earned == 0, "Midnight resets today's income");
            v = WorkClock.At(s, new DateTime(2026, 11, 1, 14, 0, 0));
            check(!v.WorkingDay && v.Earned == 0, "Rest day never accrues");
            check(WorkClock.CountDays(s, new DateTime(2026, 11, 1)) == 21, "November double-rest workdays");
            check(WorkClock.CountDays(s, new DateTime(2024, 2, 1)) == 21, "Leap February workdays");
            check(WorkClock.At(s, new DateTime(2026, 12, 1)).MonthDays == 23, "Manual override expires next month");
            s.WorkMode = 0;
            check(WorkClock.CountDays(s, new DateTime(2026, 11, 1)) == 25, "Single-rest workdays");
            s.SingleRest = 3;
            check(!WorkClock.IsWorkingDay(s, new DateTime(2026, 11, 4)) && WorkClock.IsWorkingDay(s, new DateTime(2026, 11, 1)), "Custom single-rest weekday");
            s.WorkMode = 2; s.SingleRest = 0;
            check(!WorkClock.IsWorkingDay(s, new DateTime(2026, 10, 31)), "Anchor double Saturday is off");
            check(WorkClock.IsWorkingDay(s, new DateTime(2026, 11, 7)), "Following single Saturday works");
            check(!WorkClock.IsWorkingDay(s, new DateTime(2026, 11, 14)), "Two weeks later is off");
            check(WorkClock.IsWorkingDay(s, new DateTime(2026, 10, 24)), "Negative week offset works");
            check(WorkClock.CountDays(s, new DateTime(2026, 11, 1)) == 23, "Alternating schedule survives month boundary");
            s.WorkMode = 1; s.ManualDays = false;
            check(WorkClock.At(s, new DateTime(2026, 11, 2)).MonthDays == 21 && WorkClock.At(s, new DateTime(2026, 12, 1)).MonthDays == 23, "New month recalculates divisor");
            s.HasBreak = false;
            check(WorkClock.At(s, new DateTime(2026, 11, 2, 12, 0, 0)).Total == 32400, "No lunch yields nine hours");
            s.Start = "08:30"; s.End = "17:15";
            check(WorkClock.At(s, new DateTime(2026, 11, 2, 17, 15, 0)).Progress == 1, "Custom minute-based schedule");
            Settings invalid = s.Copy(); invalid.End = "07:00"; bool rejected = false;
            try { invalid.Validate(); } catch { rejected = true; } check(rejected, "Invalid shift rejected");
            invalid = s.Copy(); invalid.HasBreak = true; invalid.BreakStart = "06:00"; rejected = false;
            try { invalid.Validate(); } catch { rejected = true; } check(rejected, "Outside-shift lunch rejected");
            invalid = s.Copy(); invalid.Days = 0; rejected = false;
            try { invalid.Validate(); } catch { rejected = true; } check(rejected, "Zero divisor rejected");
            s.MonthlySalary = 0;
            check(WorkClock.At(s, new DateTime(2026, 11, 2, 14, 0, 0)).Earned == 0, "Zero salary supported");
            s.MonthlySalary = 8800; Storage.Save(s); Settings loaded = Storage.Load();
            check(loaded.MonthlySalary == s.MonthlySalary && loaded.Start == s.Start && loaded.End == s.End && loaded.DoubleSaturday == s.DoubleSaturday, "Settings persist across reload");
            s.MonthlySalary = 9000; Storage.Save(s);
            check(Storage.Load().MonthlySalary == 9000 && File.Exists(Storage.PathName + ".bak"), "Atomic update and backup");
            Snapshot beforeTheme = WorkClock.At(s, new DateTime(2026, 11, 2, 14, 0, 0));
            s.Theme = 1; Storage.Save(s);
            Snapshot afterTheme = WorkClock.At(s, new DateTime(2026, 11, 2, 14, 0, 0));
            check(Storage.Load().Theme == 1, "Theme selection persists");
            check(beforeTheme.Earned == afterTheme.Earned && beforeTheme.Progress == afterTheme.Progress, "Themes never change income logic");
            using (StringReader legacy = new StringReader("<Settings><MonthlySalary>8800</MonthlySalary></Settings>"))
                check(((Settings)new XmlSerializer(typeof(Settings)).Deserialize(legacy)).Theme == 0, "Existing configuration without theme stays compatible");
            using (Widget themed = new Widget(s))
            {
                Button themeButton = null;
                foreach (Control control in themed.Controls) if (control.Text.Contains("换主题")) themeButton = control as Button;
                check(themeButton != null, "Theme switch button exists");
                themed.Opacity = 0; themed.Show(); Application.DoEvents();
                themeButton.PerformClick();
                check(Storage.Load().Theme == 0, "Theme button switches to neon");
                themeButton.PerformClick();
                check(Storage.Load().Theme == 1, "Theme button switches to starlight");
                Button settingsButton = null;
                foreach (Control control in themed.Controls) if (control.Text.Contains("设置")) settingsButton = control as Button;
                for (int target = 1; target >= 0; target--)
                {
                    int selected = target;
                    using (System.Windows.Forms.Timer interact = new System.Windows.Forms.Timer())
                    {
                        interact.Interval = 100;
                        interact.Tick += delegate
                        {
                            foreach (Form form in Application.OpenForms)
                            {
                                SettingsForm dialog = form as SettingsForm;
                                if (dialog == null) continue;
                                interact.Stop(); ((ComboBox)dialog.Controls["WidgetSize"]).SelectedIndex = selected;
                                if (selected == 1)
                                {
                                    ((NumericUpDown)dialog.Controls["WaterInterval"]).Value = 17;
                                    ((NumericUpDown)dialog.Controls["RestroomInterval"]).Value = 53;
                                }
                                ((Button)dialog.AcceptButton).PerformClick(); break;
                            }
                        };
                        interact.Start(); settingsButton.PerformClick();
                    }
                    check(themed.ClientSize == (target == 1 ? new Size(260, 300) : new Size(300, 360)), "Settings dialog switches actual widget size: " + target);
                    check(Storage.Load().WidgetSize == target, "Widget size survives settings reload: " + target);
                    bool buttonsInside = true;
                    foreach (Control control in themed.Controls) if (!themed.ClientRectangle.Contains(control.Bounds)) buttonsInside = false;
                    check(buttonsInside, "All feature buttons remain inside widget: " + target);
                }
                check(Storage.Load().MonthlySalary == s.MonthlySalary && Storage.Load().Theme == s.Theme && Storage.Load().RemindersEnabled == s.RemindersEnabled, "Size switching preserves salary, theme and reminder options");
                check(Storage.Load().WaterIntervalMinutes == 17 && Storage.Load().RestroomIntervalMinutes == 53, "Custom intervals save through actual settings dialog and survive size changes");
                using (Widget restarted = new Widget(Storage.Load()))
                    check(restarted.ClientSize == new Size(300, 360), "Saved size applies on restart");
                themed.Hide();
            }
            using (StringReader legacy = new StringReader("<Settings><MonthlySalary>8800</MonthlySalary></Settings>"))
                check(((Settings)new XmlSerializer(typeof(Settings)).Deserialize(legacy)).WidgetSize == 0, "Legacy settings default to compact size");
            using (SettingsForm dialog = new SettingsForm(s))
            {
                dialog.CreateControl();
                check(dialog.Controls.Count > 20, "Settings window builds all fields");
            }
            Settings remind = new Settings { RemindersEnabled = true };
            DateTime morning = new DateTime(2026, 11, 2, 9, 0, 0);
            ReminderClock clock = new ReminderClock(morning);
            check(clock.Poll(remind, morning.AddMinutes(29)) == null, "No early reminder");
            string together = clock.Poll(remind, morning.AddMinutes(30));
            check(together.Contains("喝") && together.Contains("洗手间"), "Equal intervals merge both reminders at thirty working minutes");
            check(clock.Poll(remind, morning.AddMinutes(30).AddSeconds(1)) == null, "No duplicate reminder");
            check(clock.Poll(remind, morning.AddMinutes(60)).Contains("洗手间"), "Both intervals run again after another thirty minutes");
            check(clock.Poll(remind, morning.AddMinutes(90)).Contains("喝"), "Repeated reminder keeps independent clocks running");
            clock.Reset(new DateTime(2026, 11, 2, 11, 45, 0));
            check(clock.Poll(remind, new DateTime(2026, 11, 2, 12, 0, 0)) == null, "No popup at lunch start");
            check(clock.Poll(remind, new DateTime(2026, 11, 2, 12, 50, 0)) == null, "Lunch does not accrue time");
            check(clock.Poll(remind, new DateTime(2026, 11, 2, 13, 14, 59)) == null, "Paused partial interval resumes accurately");
            check(clock.Poll(remind, new DateTime(2026, 11, 2, 13, 15, 0)).Contains("喝"), "Fifteen minutes on each side of lunch triggers reminder");
            clock.Reset(morning.AddHours(8.5));
            check(clock.Poll(remind, morning.AddHours(9)) == null, "No popup at end of shift");
            clock.Reset(morning.AddDays(-1));
            check(clock.Poll(remind, morning.AddDays(-1).AddHours(3)) == null, "Rest day never reminds");
            clock.Reset(morning.AddHours(-1));
            check(clock.Poll(remind, morning.AddMinutes(-1)) == null, "Before-shift time excluded");
            check(clock.Poll(remind, morning.AddMinutes(29)) == null, "Only working portion counts before first reminder");
            check(clock.Poll(remind, morning.AddMinutes(30)) != null, "First workday half hour includes late poll across start");
            clock.Reset(morning); remind.RemindersEnabled = false;
            check(clock.Poll(remind, morning.AddHours(2)) == null, "Disabled reminder is silent");
            remind.RemindersEnabled = true; clock.Reset(morning);
            check(clock.Poll(remind, morning.AddHours(2)) != null && clock.Poll(remind, morning.AddHours(2).AddSeconds(1)) == null, "Wake from sleep produces at most one popup");
            check(clock.Poll(remind, morning.AddDays(1)) == null, "New day discards overdue interval");
            clock.Reset(morning); clock.Poll(remind, morning.AddMinutes(20));
            check(clock.Poll(remind, morning.AddMinutes(10)) == null && clock.Poll(remind, morning.AddMinutes(39)) == null, "Clock rollback resets interval safely");
            remind.ReminderMode = 1; clock.Reset(morning);
            check(clock.Poll(remind, morning.AddMinutes(30)).Contains("喝水") && clock.Poll(remind, morning.AddMinutes(60)).Contains("喝水"), "Water-only mode");
            remind.ReminderMode = 2; clock.Reset(morning);
            check(clock.Poll(remind, morning.AddMinutes(30)).Contains("洗手间"), "Restroom-only mode");
            remind.ReminderMode = 0; remind.WaterIntervalMinutes = 20; remind.RestroomIntervalMinutes = 45; clock.Reset(morning);
            check(clock.Poll(remind, morning.AddMinutes(19)) == null, "Custom intervals do not remind early");
            string waterOnly = clock.Poll(remind, morning.AddMinutes(20));
            check(waterOnly.Contains("喝水") && !waterOnly.Contains("洗手间"), "Water interval fires independently at twenty minutes");
            check(clock.Poll(remind, morning.AddMinutes(40)).Contains("喝水"), "Water repeats before first restroom reminder");
            string restroomOnly = clock.Poll(remind, morning.AddMinutes(45));
            check(restroomOnly.Contains("洗手间") && !restroomOnly.Contains("喝"), "Restroom clock is not reset by water reminders");
            check(clock.Poll(remind, morning.AddMinutes(60)).Contains("喝水") && clock.Poll(remind, morning.AddMinutes(65)) == null, "Restroom reminder does not reset water clock");
            remind.WaterIntervalMinutes = 30; remind.RestroomIntervalMinutes = 60; clock.Reset(morning);
            check(!clock.Poll(remind, morning.AddMinutes(30)).Contains("洗手间"), "Thirty-minute water reminder leaves sixty-minute restroom clock running");
            string merged = clock.Poll(remind, morning.AddMinutes(60));
            check(merged.Contains("喝") && merged.Contains("洗手间"), "Different intervals due together merge into one popup");
            remind.WaterIntervalMinutes = 15; remind.RestroomIntervalMinutes = 40; clock.Reset(new DateTime(2026, 11, 2, 11, 50, 0));
            check(clock.Poll(remind, new DateTime(2026, 11, 2, 12, 30, 0)) == null, "Lunch pauses both custom intervals");
            check(clock.Poll(remind, new DateTime(2026, 11, 2, 13, 5, 0)).Contains("喝水"), "Custom water interval resumes after lunch");
            check(clock.Poll(remind, new DateTime(2026, 11, 2, 13, 20, 0)).Contains("喝水"), "Short custom interval repeats after lunch");
            check(!clock.Poll(remind, new DateTime(2026, 11, 2, 13, 30, 0)).Contains("喝"), "Long custom interval remains independent across lunch");
            remind.ReminderMode = 1; remind.WaterIntervalMinutes = 1; clock.Reset(morning);
            check(clock.Poll(remind, morning.AddSeconds(59)) == null && clock.Poll(remind, morning.AddMinutes(1)).Contains("喝水"), "Minimum one-minute interval boundary");
            invalid = remind.Copy(); invalid.WaterIntervalMinutes = 0; rejected = false;
            try { invalid.Validate(); } catch { rejected = true; } check(rejected, "Zero water interval rejected");
            invalid = remind.Copy(); invalid.RestroomIntervalMinutes = 1441; rejected = false;
            try { invalid.Validate(); } catch { rejected = true; } check(rejected, "Excessive restroom interval rejected");
            using (StringReader legacy = new StringReader("<Settings><MonthlySalary>8800</MonthlySalary><ReminderMode>2</ReminderMode><RemindersEnabled>true</RemindersEnabled></Settings>"))
            {
                Settings old = (Settings)new XmlSerializer(typeof(Settings)).Deserialize(legacy);
                check(old.WaterIntervalMinutes == 30 && old.RestroomIntervalMinutes == 30 && old.MonthlySalary == 8800 && old.ReminderMode == 2 && old.RemindersEnabled, "Legacy settings receive thirty-minute defaults without losing saved values");
            }
            remind.ReminderMode = 2;
            Storage.Save(remind);
            check(Storage.Load().RemindersEnabled && Storage.Load().ReminderMode == 2, "Reminder options persist");
            check(Storage.Load().WaterIntervalMinutes == 1 && Storage.Load().RestroomIntervalMinutes == 40, "Distinct custom intervals survive storage reload");
            Settings summary = new Settings { MonthlySalary = 8800, ManualDays = true, Days = 22, ManualMonth = "2026-11" };
            DateTime offWork = new DateTime(2026, 11, 2, 18, 0, 0);
            check(!WorkClock.SummaryDue(summary, offWork.AddSeconds(-1)), "No daily summary before clock-out");
            check(WorkClock.SummaryDue(summary, offWork), "Daily summary is due at clock-out");
            check(WorkClock.SummaryDue(summary, offWork.AddHours(3)), "Reopening later that day catches up once");
            check(WorkClock.At(summary, offWork).Earned == 400, "Summary uses completed day's estimated earnings");
            check(!summary.RemindersEnabled && WorkClock.SummaryDue(summary, offWork), "Daily summary works independently of rest reminder switch");
            summary.LastSummaryDate = "2026-11-02";
            check(!WorkClock.SummaryDue(summary, offWork.AddHours(4)), "Same-day summary never repeats");
            Storage.Save(summary);
            check(!WorkClock.SummaryDue(Storage.Load(), offWork.AddHours(2)), "Summary deduplication survives restart");
            check(WorkClock.SummaryDue(summary, offWork.AddDays(1)), "Next workday receives its own summary");
            check(!WorkClock.SummaryDue(summary, new DateTime(2026, 11, 8, 19, 0, 0)), "Rest days never receive earnings summary");
            summary.DailySummaryEnabled = false;
            check(!WorkClock.SummaryDue(summary, offWork.AddDays(1)), "Daily summary can be disabled");
            summary.DailySummaryEnabled = true; summary.MonthlySalary = 0;
            check(!WorkClock.SummaryDue(summary, offWork.AddDays(1)), "Unconfigured salary does not produce summary popup");
            using (StringReader legacy = new StringReader("<Settings><MonthlySalary>8800</MonthlySalary></Settings>"))
            {
                Settings old = (Settings)new XmlSerializer(typeof(Settings)).Deserialize(legacy);
                check(old.DailySummaryEnabled && old.LastSummaryDate == "", "Legacy settings support new daily summary without losing salary");
            }
            summary.MonthlySalary = 8800; summary.LastSummaryDate = "";
            using (SettingsForm dialog = new SettingsForm(summary))
            {
                dialog.Opacity = 0; dialog.Show(); Application.DoEvents();
                summary.LastSummaryDate = "2026-11-02";
                ((Button)dialog.AcceptButton).PerformClick();
                check(Storage.Load().LastSummaryDate == "2026-11-02", "Saving an open settings dialog retains summary state updated meanwhile");
            }
            using (StringReader legacy = new StringReader("<Settings><MonthlySalary>8800</MonthlySalary></Settings>"))
                check(!((Settings)new XmlSerializer(typeof(Settings)).Deserialize(legacy)).RemindersEnabled, "Legacy settings keep reminders opt-in");
            using (ReminderPopup popup = new ReminderPopup("喝水时间到了，给自己补充一点水分。", 0))
            {
                popup.Opacity = 0; popup.Show(); Application.DoEvents();
                foreach (Control control in popup.Controls) if (control is Button) ((Button)control).PerformClick();
                check(popup.IsDisposed, "Dismiss button closes and disposes popup");
            }
            using (ReminderPopup popup = new ReminderPopup("如有需要，起身去趟洗手间吧。", 1))
            {
                popup.Opacity = 0; popup.Show(); Application.DoEvents();
                System.Diagnostics.Stopwatch waiting = System.Diagnostics.Stopwatch.StartNew();
                while (!popup.IsDisposed && waiting.Elapsed.TotalSeconds < 17) { Application.DoEvents(); Thread.Sleep(25); }
                check(popup.IsDisposed && waiting.Elapsed.TotalSeconds >= 14, "Popup automatically dismisses after fifteen seconds");
            }
            report.AppendLine("All " + checks + " checks passed.");
        }
        catch (Exception ex) { report.AppendLine(ex.ToString()); Environment.ExitCode = 1; }
        File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "test-report.txt"), report.ToString(), Encoding.UTF8);
    }
}
