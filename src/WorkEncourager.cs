using System;
using System.Drawing;
using System.Collections.Generic;
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
    public string LastSummaryEnd = "";
    public int OvertimePreset = 120;
    public List<OvertimeEntry> OvertimeEntries = new List<OvertimeEntry>();
    public List<LeaveEntry> LeaveEntries = new List<LeaveEntry>();
    public List<AbsenceEntry> Absences = new List<AbsenceEntry>();
    public decimal PersonalLeavePercent = 0, SickLeavePercent = -1, AnnualLeavePercent = 100;
    public bool PetEnabled = true, PetWalk = false;
    public int PetCharacter = 0, PetX = int.MinValue, PetY = int.MinValue;
    public int PetPose = -1;
    public int PetDockEdge = 0; // 0自由，1左、2右、3上、4下；独立于用户选择的四种姿态。
    public int PetScale = 100;
    public bool PrivacyMode = false;
    public double PetYaw = 25, PetPitch = 12;
    public string DetailOrder = "income,rate,progress,countdown,encouragement,month,overtime,absence";
    public string DetailVisible = "income,rate,progress,countdown,encouragement,month,overtime,absence";
    public bool HoverEnabled = true;
    public string HoverVisible = "income,rate,progress,countdown";
    public string ManualMonth = DateTime.Today.ToString("yyyy-MM");
    public DateTime DoubleSaturday = new DateTime(2026, 10, 31);

    public Settings Copy()
    {
        Settings copy = (Settings)MemberwiseClone();
        copy.OvertimeEntries = OvertimeEntries.ConvertAll(delegate(OvertimeEntry entry) { return entry.Copy(); });
        copy.LeaveEntries = LeaveEntries.ConvertAll(delegate(LeaveEntry entry) { return entry.Copy(); });
        copy.Absences = Absences.ConvertAll(delegate(AbsenceEntry entry) { return entry.Copy(); }); return copy;
    }
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
        if (PetCharacter < 0 || PetCharacter >= PetArt.Names.Length) throw new Exception("桌宠形象设置无效。");
        if (PetDockEdge < 0 || PetDockEdge > 4) throw new Exception("桌宠贴墙位置无效。");
        if (PetScale < 50 || PetScale > 150) throw new Exception("桌宠大小需在50%至150%之间。");
        if (PetPose < -1 || PetPose > 3 || double.IsNaN(PetYaw) || double.IsInfinity(PetYaw) || PetYaw < 0 || PetYaw >= 360 || double.IsNaN(PetPitch) || double.IsInfinity(PetPitch) || PetPitch < -25 || PetPitch > 60)
            throw new Exception("三维视角或姿态设置无效。");
        HashSet<string> order = new HashSet<string>((DetailOrder ?? "").Split(','));
        if (order.Count != DetailCard.Keys.Length || (DetailOrder ?? "").Split(',').Length != DetailCard.Keys.Length) throw new Exception("详情组件顺序无效。");
        foreach (string key in DetailCard.Keys) if (!order.Contains(key)) throw new Exception("详情组件顺序无效。");
        foreach (string key in (DetailVisible ?? "").Split(',')) if (key.Length > 0 && Array.IndexOf(DetailCard.Keys, key) < 0) throw new Exception("详情组件配置无效。");
        foreach (string key in (HoverVisible ?? "").Split(',')) if (key.Length > 0 && Array.IndexOf(DetailCard.Keys, key) < 0) throw new Exception("悬浮信息配置无效。");
        if (DoubleSaturday.DayOfWeek != DayOfWeek.Saturday)
            throw new Exception("大小周的参考日期请选择一个双休周的周六。");
        AbsenceClock.Validate(this); OvertimeClock.Validate(this, DateTime.Now);
    }
}

public sealed class Snapshot
{
    public bool WorkingDay;
    public double Total, Elapsed, Progress, Remaining;
    public int MonthDays;
    public decimal Daily, Earned, PerSecond, MonthEarned;
    public string Status;
    public OvertimeSnapshot Overtime;
    public AbsenceSnapshot Absence;
    public double ReminderElapsed;
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
        // 月累计是当前配置下的估算，不依赖程序运行历史；封顶不影响既有今日收入。
        int completedDays = 0;
        for (int day = 1; day < now.Day; day++) if (IsWorkingDay(s, new DateTime(now.Year, now.Month, day))) completedDays++;
        result.MonthEarned = completedDays + (decimal)result.Progress >= result.MonthDays ? s.MonthlySalary : Math.Min(s.MonthlySalary, result.Daily * completedDays + result.Earned);
        result.Status = !result.WorkingDay ? "今日休息" : now < start ? "尚未开工" : now >= end ? "今日已下班" : s.HasBreak && now >= pause && now < resume ? "午休充电中" : "正在积累小确幸";
        result.Overtime = OvertimeClock.At(s, now);
        result.Absence = result.Overtime.Absence ?? new AbsenceSnapshot();
        result.ReminderElapsed = Math.Max(0, result.Elapsed - result.Absence.TodayElapsed);
        result.Earned = Math.Max(0, result.Earned - result.Absence.TodayLoss);
        result.MonthEarned = Math.Max(0, result.MonthEarned - result.Absence.MonthLoss);
        if (result.Absence.Active) { result.PerSecond *= result.Absence.ActivePayPercent / 100m; result.Status = AbsenceClock.Names[result.Absence.ActiveKind] + "中"; }
        return result;
    }
    public static bool SummaryDue(Settings settings, DateTime now)
    {
        // 只补发当天的总结，日期随配置保存，重启和重新打开设置都不会重复弹出。
        DateTime end = SummaryEnd(settings, now);
        DateTime delivered;
        if (!DateTime.TryParseExact(settings.LastSummaryEnd, "s", CultureInfo.InvariantCulture, DateTimeStyles.None, out delivered) || delivered.Date != now.Date) delivered = now.Date.Add(TimeSpan.Parse(settings.End));
        return settings.DailySummaryEnabled && settings.MonthlySalary > 0 && IsWorkingDay(settings, now) && now >= end &&
            (settings.LastSummaryDate != now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) || end > delivered);
    }
    public static DateTime SummaryEnd(Settings settings, DateTime now)
    {
        DateTime regular = now.Date.Add(TimeSpan.Parse(settings.End)), overtime = OvertimeClock.At(settings, now).LastEnd;
        return overtime > regular ? overtime : regular;
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
        Snapshot previous = WorkClock.At(settings, last);
        double delta = Math.Max(0, current.ReminderElapsed + current.Overtime.ElapsedSeconds - previous.ReminderElapsed - previous.Overtime.ElapsedSeconds);
        last = now;
        if ((!current.WorkingDay || now.TimeOfDay < TimeSpan.Parse(settings.Start) || now.TimeOfDay >= TimeSpan.Parse(settings.End)) && !current.Overtime.Active)
        { waterSeconds = restroomSeconds = 0; return null; }
        if (settings.ReminderMode != 2) waterSeconds += delta;
        if (settings.ReminderMode != 1) restroomSeconds += delta;
        if (current.Absence.Active) return null;
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
            // 只迁移合法的旧五组件顺序，保留用户显隐和原排序；损坏的配置仍由Validate报错。
            string[] previousOrder = (s.DetailOrder ?? "").Split(',');
            HashSet<string> previousKeys = new HashSet<string>(previousOrder);
            bool legacyOrder = previousOrder.Length == 5 && previousKeys.Count == 5 && !previousKeys.Contains("month");
            foreach (string key in previousOrder) if (Array.IndexOf(DetailCard.Keys, key) < 0) legacyOrder = false;
            if (legacyOrder) s.DetailOrder += ",month";
            string[] six = s.DetailOrder.Split(','); HashSet<string> sixKeys = new HashSet<string>(six);
            bool previousSix = six.Length == 6 && sixKeys.Count == 6 && !sixKeys.Contains("overtime");
            foreach (string key in six) if (Array.IndexOf(DetailCard.Keys, key) < 0) previousSix = false;
            if (previousSix) s.DetailOrder += ",overtime";
            string[] seven = s.DetailOrder.Split(','); HashSet<string> sevenKeys = new HashSet<string>(seven);
            bool previousSeven = seven.Length == 7 && sevenKeys.Count == 7 && !sevenKeys.Contains("absence");
            foreach (string key in seven) if (Array.IndexOf(DetailCard.Keys, key) < 0) previousSeven = false;
            if (previousSeven) s.DetailOrder += ",absence";
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
    private ComboBox mode, rest, reminderMode, widgetSize, petCharacter;
    private DateTimePicker start, end, pause, resume, anchor;
    private CheckBox lunch, manual, onTop, reminders, dailySummary, petEnabled;
    private Label calculated;
    private TrackBar petScale;
    private CheckBox startWithWindows, revealSalary;
    private Label hiddenSalary;
    private readonly Color ink = Color.FromArgb(32, 47, 44);

    public SettingsForm(Settings settings)
    {
        original = settings;
        Text = "设置 · 上班鼓励器";
        Font = new Font("Microsoft YaHei UI", 10);
        BackColor = Color.FromArgb(245, 248, 245);
        ForeColor = ink;
        ClientSize = new Size(470, 898);
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
        // 桌宠配置复用设置页，其余提醒区整体下移，工资和提醒字段不变。
        foreach (Control control in Controls) if (control.Top >= 491) control.Top += 44;
        LabelAt("桌宠形象", 24, 491, 105);
        petCharacter = new ComboBox { Name = "PetCharacter", Location = new Point(130, 488), Width = 175, DropDownStyle = ComboBoxStyle.DropDownList };
        petCharacter.Items.AddRange(PetArt.Names); petCharacter.SelectedIndex = settings.PetCharacter; Controls.Add(petCharacter);
        petEnabled = new CheckBox { Name = "PetEnabled", Text = "启用桌宠", Location = new Point(315, 491), Size = new Size(126, 29), Checked = settings.PetEnabled }; Controls.Add(petEnabled);
        foreach (Control control in Controls) if (control.Top >= 535) control.Top += 86;
        LabelAt("桌宠大小", 24, 535, 105);
        petScale = new TrackBar { Name = "PetScale", Location = new Point(125, 531), Size = new Size(230, 42), Minimum = 50, Maximum = 150, TickFrequency = 25, Value = settings.PetScale }; Controls.Add(petScale);
        Label scaleValue = LabelAt(settings.PetScale + "%", 365, 535, 76);
        petScale.ValueChanged += delegate { scaleValue.Text = petScale.Value + "%"; };
        startWithWindows = new CheckBox { Name = "StartWithWindows", Text = "登录Windows后自动启动", Bounds = new Rectangle(24, 578, 417, 29), Checked = StartupOption.Enabled }; Controls.Add(startWithWindows);
        bool startupTouched = false; startWithWindows.CheckedChanged += delegate { startupTouched = true; };
        // 设置项较多时可滚动，避免保存按钮在较小屏幕上无法操作。
        AutoScroll = true; AutoScrollMargin = new Size(0, 12);
        ClientSize = new Size(470, Math.Min(840, Screen.FromPoint(Cursor.Position).WorkingArea.Height - 100));
        salary.Width = 150;
        revealSalary = new CheckBox { Name = "RevealSalary", Text = "查看", Bounds = new Rectangle(355, 99, 86, 28) }; Controls.Add(revealSalary);
        hiddenSalary = new Label { Name = "HiddenSalary", Text = "金额已隐藏", Bounds = salary.Bounds, TextAlign = ContentAlignment.MiddleLeft }; Controls.Add(hiddenSalary);
        revealSalary.CheckedChanged += delegate { salary.Visible = !original.PrivacyMode || revealSalary.Checked; hiddenSalary.Visible = !salary.Visible; };
        ApplyPrivacy(settings.PrivacyMode);
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
            Widget owner = Owner as Widget;
            if (owner != null) { owner.ShowMessage(message, false); return; }
            ReminderPopup popup = new ReminderPopup(message, settings.Theme);
            popup.FormClosed += delegate { popup.Dispose(); }; popup.Show();
        };
        foreach (ComboBox combo in new ComboBox[] { mode, rest, reminderMode, widgetSize, petCharacter })
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
            next.PetEnabled = petEnabled.Checked; next.PetCharacter = petCharacter.SelectedIndex;
            next.WidgetSize = widgetSize.SelectedIndex;
            next.PetScale = petScale.Value;
            try
            {
                next.Validate(); string previousCommand = StartupOption.CurrentCommand; bool previousStartup = !string.IsNullOrEmpty(previousCommand);
                bool startupChanged = startupTouched && (startWithWindows.Checked ? !StartupOption.UsesCurrentPath : previousStartup);
                if (startupChanged) StartupOption.Apply(startWithWindows.Checked);
                try { Storage.Save(next); } catch { if (startupChanged) StartupOption.Apply(previousStartup, previousCommand); throw; }
                Result = next; DialogResult = DialogResult.OK; Close();
            }
            catch (Exception ex) { MessageBox.Show(this, ex.Message, "设置未保存", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
        };
    }
    public void ApplyPrivacy(bool enabled)
    {
        original.PrivacyMode = enabled; revealSalary.Checked = false;
        revealSalary.Visible = enabled; salary.Visible = !enabled; hiddenSalary.Visible = enabled;
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
    private readonly ToolTip hint = new ToolTip();
    private readonly Button messageButton;
    private int bodyWidth;
    private string content = "";
    private readonly string speakerPrefix;
    private static readonly TextFormatFlags TextFlags = TextFormatFlags.WordBreak | TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix;
    private bool tailLeft;
    public ReminderPopup(string message, int theme, bool dailySummary = false, string petSpeaker = null, bool autoDismiss = true)
    {
        Text = dailySummary ? "下班总结 · 上班鼓励器" : "休息提醒 · 上班鼓励器";
        AutoScaleMode = AutoScaleMode.None; FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.Manual; ShowInTaskbar = false; TopMost = true;
        BackColor = Color.FromArgb(149, 236, 105); ForeColor = Color.FromArgb(22, 37, 17);
        Font = new Font("Microsoft YaHei UI", 10); DoubleBuffered = true;
        speakerPrefix = petSpeaker == null ? "" : petSpeaker + "：";
        messageButton = new Button { Text = "", FlatStyle = FlatStyle.Flat, BackColor = BackColor, ForeColor = ForeColor, Cursor = Cursors.Hand, TabStop = false };
        messageButton.FlatAppearance.BorderSize = 0;
        messageButton.FlatAppearance.MouseOverBackColor = BackColor;
        messageButton.FlatAppearance.MouseDownBackColor = Color.FromArgb(136, 222, 94);
        messageButton.Paint += delegate(object sender, PaintEventArgs e)
        {
            TextRenderer.DrawText(e.Graphics, content, Font, new Rectangle(10, 3, messageButton.Width - 20, messageButton.Height - 6), ForeColor, TextFlags | TextFormatFlags.VerticalCenter);
        };
        messageButton.Click += delegate { Close(); }; Controls.Add(messageButton);
        hint.SetToolTip(messageButton, autoDismiss ? "点击气泡关闭；15秒后自动收起" : "鼠标移开桌宠后收起；点击桌宠展开详情");
        Click += delegate { Close(); };
        SetMessage(message);
        Rectangle area = Screen.FromPoint(Cursor.Position).WorkingArea;
        Location = new Point(area.Right - Width - 20, area.Bottom - Height - 20);
        dismiss.Interval = 15000; dismiss.Tick += delegate { Close(); };
        Shown += delegate { if (autoDismiss) dismiss.Start(); };
    }
    public void SetMessage(string message)
    {
        message = message ?? ""; if (content == message && bodyWidth > 0) return;
        content = message; messageButton.AccessibleName = speakerPrefix + message;
        int lineWidth = Math.Min(220, Math.Max(52, TextRenderer.MeasureText(message, Font, Size.Empty, TextFormatFlags.SingleLine | TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix).Width));
        Size measured = TextRenderer.MeasureText(message, Font, new Size(lineWidth, 1000), TextFlags);
        bodyWidth = Math.Max(76, measured.Width + 24); ClientSize = new Size(bodyWidth + 9, Math.Max(34, measured.Height + 18));
        UpdateBubble(); messageButton.Invalidate();
    }
    // 尖角随桌宠所在侧翻转；窗口区域只包含气泡，尖角周围可透过桌面。
    public void AimAtPet(Rectangle petBounds)
    {
        bool next = Left + Width / 2 > petBounds.Left + petBounds.Width / 2;
        if (next != tailLeft) { tailLeft = next; UpdateBubble(); }
    }
    private void UpdateBubble()
    {
        int x = tailLeft ? 8 : 0, middle = Height / 2;
        using (GraphicsPath shape = Widget.Rounded(new Rectangle(x, 0, bodyWidth, Height), 6))
        {
            shape.FillMode = FillMode.Winding;
            shape.AddPolygon(tailLeft ? new[] { new Point(9, middle + 6), new Point(0, middle), new Point(9, middle - 6) } : new[] { new Point(bodyWidth - 1, middle - 6), new Point(Width - 1, middle), new Point(bodyWidth - 1, middle + 6) });
            Region previous = Region; Region = new Region(shape); if (previous != null) previous.Dispose();
        }
        messageButton.Bounds = new Rectangle(x + 2, 2, bodyWidth - 4, Height - 4); Invalidate();
    }
    protected override bool ShowWithoutActivation { get { return true; } }
    protected override CreateParams CreateParams
    {
        get { CreateParams value = base.CreateParams; value.ExStyle |= 0x08000000; return value; }
    }
    protected override void Dispose(bool disposing)
    {
        if (disposing) { dismiss.Dispose(); hint.Dispose(); } base.Dispose(disposing);
    }
}


public sealed class Widget : Form
{
    private Settings settings;
    private readonly System.Windows.Forms.Timer timer = new System.Windows.Forms.Timer();
    private readonly System.Windows.Forms.Timer reminderTimer = new System.Windows.Forms.Timer();
    private readonly ReminderClock reminderClock = new ReminderClock(DateTime.Now);
    private ReminderPopup reminderPopup;
    private ReminderPopup hoverPopup;
    private ToolStripMenuItem hoverOptions;
    private Button privacyButton;
    private SettingsForm activeSettingsForm;
    private OvertimeForm activeOvertimeForm;
    private AbsenceForm activeAbsenceForm;
    private bool privacyHotkey;
    [DllImport("user32.dll")] private static extern bool RegisterHotKey(IntPtr window, int id, uint modifiers, uint key);
    [DllImport("user32.dll")] private static extern bool UnregisterHotKey(IntPtr window, int id);
    private DesktopPet pet;
    private Panel detailBody;
    private ContextMenuStrip componentMenu;
    private NotifyIcon tray;
    private Icon appIcon;
    private bool dragging, exiting;
    private Point dragOffset;
    private Color background, accent, secondary, ink, muted, card;

    public Widget(Settings s)
    {
        settings = s;
        Text = "上班鼓励器"; ClientSize = settings.WidgetSize == 1 ? new Size(260, 300) : new Size(300, 360);
        FormBorderStyle = FormBorderStyle.None; StartPosition = FormStartPosition.Manual;
        DoubleBuffered = true; ShowInTaskbar = false;
        ApplyTheme();
        AutoScaleMode = AutoScaleMode.None; TopMost = settings.AlwaysOnTop;
        Rectangle area = Screen.PrimaryScreen.WorkingArea;
        Location = s.X != -1 ? new Point(s.X, s.Y) : new Point(area.Right - Width - 30, area.Top + 60);
        bool visible = false;
        foreach (Screen screen in Screen.AllScreens) if (Rectangle.Intersect(screen.WorkingArea, Bounds).Width > 80 && Rectangle.Intersect(screen.WorkingArea, Bounds).Height > 50) visible = true;
        if (!visible) Location = new Point(area.Right - Width - 30, area.Top + 60);
        string iconPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "app.ico");
        appIcon = File.Exists(iconPath) ? new Icon(iconPath) : (Icon)SystemIcons.Information.Clone(); Icon = appIcon;
        tray = new NotifyIcon { Icon = appIcon, Text = "上班鼓励器 · 双击显示", Visible = true };
        ContextMenuStrip menu = new ContextMenuStrip();
        menu.Items.Add("显示桌宠 / 挂件", null, delegate { if (settings.PetEnabled) ApplyPet(); else { Show(); Activate(); } });
        menu.Items.Add("展开 / 收起详情", null, delegate { ToggleDetails(); });
        menu.Items.Add("设置", null, delegate { OpenSettings(); });
        menu.Items.Add("今日加班 / 调休", null, delegate { OpenOvertime(); });
        menu.Items.Add("请假管理", null, delegate { OpenAbsence(); });
        ToolStripMenuItem privacy = new ToolStripMenuItem("隐私模式 · Ctrl+Alt+P");
        privacy.Click += delegate { TogglePrivacy(); }; menu.Items.Add(privacy);
        ToolStripMenuItem characters = new ToolStripMenuItem("切换桌宠形象");
        for (int i = 0; i < PetArt.Names.Length; i++)
        {
            int choice = i;
            ToolStripMenuItem item = new ToolStripMenuItem(PetArt.Names[i]);
            item.Click += delegate
            {
                Settings next = settings.Copy(); next.PetCharacter = choice; next.PetEnabled = true;
                try { Storage.Save(next); settings = next; ApplyPet(); } catch (Exception ex) { MessageBox.Show(this, ex.Message, "桌宠设置未保存"); }
            };
            characters.DropDownItems.Add(item);
        }
        menu.Items.Add(characters);
        ToolStripMenuItem poses = new ToolStripMenuItem("桌宠状态");
        for (int i = -1; i < PetModel3D.PoseNames.Length; i++)
        {
            int pose = i;
            ToolStripMenuItem item = new ToolStripMenuItem(i == -1 ? "随机（10–20秒切换）" : PetModel3D.PoseNames[i]);
            item.Click += delegate
            {
                Settings next = settings.Copy(); next.PetPose = pose;
                try { Storage.Save(next); settings = next; ApplyPet(); } catch (Exception ex) { MessageBox.Show(this, ex.Message, "姿态未保存"); }
            };
            poses.DropDownItems.Add(item);
        }
        menu.Items.Add(poses);
        menu.Items.Add("重置三维视角", null, delegate
        {
            Settings next = settings.Copy(); next.PetYaw = 25; next.PetPitch = 12;
            try { Storage.Save(next); settings = next; ApplyPet(); } catch (Exception ex) { MessageBox.Show(this, ex.Message, "视角未保存"); }
        });
        ToolStripMenuItem petMode = new ToolStripMenuItem("启用桌宠模式");
        petMode.Click += delegate
        {
            Settings next = settings.Copy(); next.PetEnabled = !next.PetEnabled;
            try { Storage.Save(next); settings = next; ApplyPet(); if (next.PetEnabled) Hide(); } catch (Exception ex) { MessageBox.Show(this, ex.Message, "桌宠设置未保存"); }
        };
        menu.Items.Add(petMode);
        ToolStripMenuItem walk = new ToolStripMenuItem("允许桌宠散步");
        walk.Click += delegate
        {
            Settings next = settings.Copy(); next.PetWalk = !next.PetWalk;
            try { Storage.Save(next); settings = next; ApplyPet(); } catch (Exception ex) { MessageBox.Show(this, ex.Message, "桌宠设置未保存"); }
        };
        menu.Items.Add(walk);
        menu.Items.Add("配置详情组件", null, delegate { componentMenu.Show(Cursor.Position); });
        hoverOptions = new ToolStripMenuItem("配置悬浮信息");
        ToolStripMenuItem hoverEnabled = new ToolStripMenuItem("开启悬浮预览") { CheckOnClick = true };
        hoverEnabled.Click += delegate
        {
            Settings next = settings.Copy(); next.HoverEnabled = hoverEnabled.Checked;
            try { Storage.Save(next); settings = next; ShowHoverInfo(false); if (pet != null) pet.ApplySettings(settings); } catch (Exception ex) { MessageBox.Show(this, ex.Message, "悬浮设置未保存"); }
        };
        hoverOptions.DropDownItems.Add(hoverEnabled); hoverOptions.DropDownItems.Add(new ToolStripSeparator());
        for (int i = 0; i < DetailCard.Keys.Length; i++)
        {
            string key = DetailCard.Keys[i]; ToolStripMenuItem item = new ToolStripMenuItem(DetailCard.Titles[i]) { CheckOnClick = true };
            item.Click += delegate { SetHoverComponentVisible(key, item.Checked); }; hoverOptions.DropDownItems.Add(item);
        }
        menu.Items.Add(hoverOptions);
        menu.Items.Add("全部收起到托盘", null, delegate { ShowHoverInfo(false); Hide(); if (pet != null) pet.Hide(); });
        menu.Items.Add("退出", null, delegate { exiting = true; Close(); });
        menu.Opening += delegate
        {
            privacy.Checked = settings.PrivacyMode;
            petMode.Checked = settings.PetEnabled; walk.Checked = settings.PetWalk;
            for (int i = 0; i < characters.DropDownItems.Count; i++) ((ToolStripMenuItem)characters.DropDownItems[i]).Checked = settings.PetCharacter == i;
            for (int i = 0; i < poses.DropDownItems.Count; i++) ((ToolStripMenuItem)poses.DropDownItems[i]).Checked = settings.PetPose == i - 1;
            poses.Text = "桌宠状态" + (pet == null ? "" : " · " + PetModel3D.PoseNames[pet.CurrentPose]);
            hoverEnabled.Checked = settings.HoverEnabled;
            HashSet<string> hoverKeys = new HashSet<string>((settings.HoverVisible ?? "").Split(','));
            for (int i = 0; i < DetailCard.Keys.Length; i++) ((ToolStripMenuItem)hoverOptions.DropDownItems[i + 2]).Checked = hoverKeys.Contains(DetailCard.Keys[i]);
        };
        tray.ContextMenuStrip = menu; ContextMenuStrip = menu;
        tray.DoubleClick += delegate { if (settings.PetEnabled) ApplyPet(); else { Show(); Activate(); } };
        detailBody = new Panel { Name = "DetailBody", AutoScroll = true, BackColor = background }; Controls.Add(detailBody);
        privacyButton = new Button { Name = "PrivacyToggle", Text = "", AccessibleName = settings.PrivacyMode ? "关闭隐私模式" : "开启隐私模式", FlatStyle = FlatStyle.Flat, BackColor = background, ForeColor = muted, Cursor = Cursors.Hand, TabStop = true };
        privacyButton.FlatAppearance.BorderSize = 0;
        privacyButton.Paint += delegate(object sender, PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using (Pen pen = new Pen(settings.PrivacyMode ? accent : muted, 1.5f))
            {
                e.Graphics.DrawArc(pen, 3, 4, 17, 10, 0, 180); e.Graphics.DrawArc(pen, 3, 4, 17, 10, 180, 180);
                e.Graphics.DrawEllipse(pen, 9, 6, 5, 6);
                if (settings.PrivacyMode) e.Graphics.DrawLine(pen, 3, 2, 20, 16);
            }
        };
        privacyButton.Click += delegate { TogglePrivacy(); }; Controls.Add(privacyButton);
        componentMenu = new ContextMenuStrip();
        AddButton("组件", 0, 0, 50, delegate(object sender, EventArgs e) { componentMenu.Show((Control)sender, new Point(0, 25)); });
        AddButton("⚙ 设置", 0, 0, 66, delegate { OpenSettings(); });
        AddButton("收起", 0, 0, 66, delegate { Hide(); });
        AddButton("◇ 换主题", 0, 0, 98, delegate
        {
            Settings next = settings.Copy(); next.Theme = 1 - next.Theme;
            try { Storage.Save(next); settings = next; ApplyTheme(); if (pet != null) pet.ApplySettings(settings); Invalidate(); }
            catch (Exception ex) { MessageBox.Show(this, ex.Message, "主题未保存"); }
        });
        AddButton(s.AlwaysOnTop ? "已置顶" : "置顶", 0, 0, 66, delegate(object sender, EventArgs e)
        {
            Settings next = settings.Copy(); next.AlwaysOnTop = !next.AlwaysOnTop;
            try { Storage.Save(next); settings = next; TopMost = next.AlwaysOnTop; if (pet != null) pet.ApplySettings(settings); ((Button)sender).Text = next.AlwaysOnTop ? "已置顶" : "置顶"; }
            catch (Exception ex) { MessageBox.Show(this, ex.Message, "置顶设置未保存"); }
        });
        ApplySize();
        timer.Interval = 100; timer.Tick += delegate
        {
            if (reminderPopup != null && !reminderPopup.IsDisposed && pet != null && pet.Visible) DesktopPet.PlaceBeside(reminderPopup, pet.Bounds);
            if (hoverPopup != null && !hoverPopup.IsDisposed && pet != null && pet.Visible) DesktopPet.PlaceBeside(hoverPopup, pet.Bounds);
            if (!Visible) return;
            Snapshot current = WorkClock.At(settings, DateTime.Now);
            foreach (Control control in detailBody.Controls) { DetailCard cardControl = control as DetailCard; if (cardControl != null) cardControl.UpdateView(settings, current); }
            Invalidate();
        }; timer.Start();
        reminderTimer.Interval = 1000;
        reminderTimer.Tick += delegate
        {
            DateTime now = DateTime.Now;
            if (hoverPopup != null && !hoverPopup.IsDisposed) hoverPopup.SetMessage(BuildHoverMessage(settings, now));
            bool summary = WorkClock.SummaryDue(settings, now);
            string message;
            if (summary)
            {
                message = SummaryMessage(settings, now);
                settings.LastSummaryDate = now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
                settings.LastSummaryEnd = WorkClock.SummaryEnd(settings, now).ToString("s", CultureInfo.InvariantCulture);
                try { Storage.Save(settings); }
                catch (Exception ex) { MessageBox.Show(this, "下班总结状态未能保存，重启后可能再次提示。\n" + ex.Message, "设置未保存"); }
                if (reminderPopup != null) reminderPopup.Close();
            }
            else message = reminderClock.Poll(settings, now);
            if (message == null || (reminderPopup != null && !reminderPopup.IsDisposed)) return;
            ShowMessage(message, summary);
        };
        reminderTimer.Start();
        MouseDown += delegate(object sender, MouseEventArgs e) { if (e.Button == MouseButtons.Left) { dragging = true; dragOffset = e.Location; Capture = true; } };
        MouseMove += delegate { if (dragging) Location = new Point(Cursor.Position.X - dragOffset.X, Cursor.Position.Y - dragOffset.Y); };
        MouseUp += delegate { if (dragging) { dragging = false; Capture = false; SaveLocation(); } };
        Shown += delegate { if (!File.Exists(Storage.PathName)) OpenSettings(); ApplyPet(); if (settings.PetEnabled) Hide(); };
    }
    public void ShowMessage(string message, bool summary)
    {
        if (summary && settings.PrivacyMode) message = SummaryMessage(settings, DateTime.Now);
        ShowHoverInfo(false);
        if (reminderPopup != null) reminderPopup.Close();
        ReminderPopup popup = new ReminderPopup(message, settings.Theme, summary, settings.PetEnabled ? PetArt.Names[settings.PetCharacter] : null); reminderPopup = popup;
        if (settings.PetEnabled)
        {
            ApplyPet(); pet.Speaking = true; DesktopPet.PlaceBeside(popup, pet.Bounds);
        }
        popup.FormClosed += delegate { popup.Dispose(); if (reminderPopup == popup) reminderPopup = null; if (pet != null) pet.Speaking = false; };
        popup.Show();
    }
    private void ToggleDetails()
    {
        ShowHoverInfo(false);
        if (Visible) { Hide(); return; }
        if (pet != null && pet.Visible) DesktopPet.PlaceBeside(this, pet.Bounds);
        Show(); Activate();
    }
    public static string BuildHoverMessage(Settings value, DateTime now)
    {
        Snapshot current = WorkClock.At(value, now);
        HashSet<string> selected = new HashSet<string>((value.HoverVisible ?? "").Split(','));
        List<string> lines = new List<string>();
        foreach (string key in DetailCard.Keys)
        {
            if (!selected.Contains(key)) continue;
            if (key == "income") lines.Add("今日收入：" + (value.PrivacyMode ? "¥ ****" : value.MonthlySalary == 0 ? "请先设置薪资" : "¥ " + current.Earned.ToString("N2")));
            else if (key == "month") lines.Add("本月累计：" + (value.PrivacyMode ? "¥ ****" : value.MonthlySalary == 0 ? "请先设置薪资" : "¥ " + current.MonthEarned.ToString("N2")));
            else if (key == "overtime") lines.Add(OvertimeClock.Text(value, current.Overtime));
            else if (key == "absence") lines.Add("今日计划请假 " + (current.Absence.TodaySeconds / 3600).ToString("0.##") + " 小时，自动调休 " + (current.Absence.UsedToday / 60m).ToString("0.#") + " 小时" + (value.PrivacyMode ? "（扣款隐藏）" : "，截至当前少计 ¥ " + current.Absence.TodayLoss.ToString("N2")));
            else if (key == "rate") lines.Add("每秒收入：" + (value.PrivacyMode ? "¥ ****" : value.MonthlySalary == 0 ? "—" : "¥ " + current.PerSecond.ToString(current.PerSecond >= .0001m ? "0.0000" : "0.0000########")));
            else if (key == "progress") lines.Add("上班进度：" + current.Progress.ToString("P1"));
            else if (key == "countdown") lines.Add("剩余工时：" + (current.WorkingDay ? string.Format("{0:00}:{1:00}:{2:00}", (int)(current.Remaining / 3600), (int)(current.Remaining / 60) % 60, (int)current.Remaining % 60) : "今日休息"));
            else if (key == "encouragement") lines.Add(DetailCard.EncouragementAt(now));
        }
        return string.Join("\n", lines.ToArray());
    }
    public static string SummaryMessage(Settings value, DateTime now)
    {
        Snapshot current = WorkClock.At(value, now);
        string message = value.PrivacyMode ? "下班啦！今日收入已隐藏，辛苦了。" : "你今日赚了 ¥" + current.Earned.ToString("N2") + "（估算）。";
        if (current.Absence.TodaySeconds > 0) message += "\n今日请假 " + (current.Absence.TodaySeconds / 3600).ToString("0.##") + " 小时，自动调休 " + (current.Absence.UsedToday / 60m).ToString("0.#") + " 小时。";
        return current.Overtime.Planned ? message + "\n" + OvertimeClock.Text(value, current.Overtime) : message;
    }
    public void TogglePrivacy()
    {
        Settings next = settings.Copy(); next.PrivacyMode = !next.PrivacyMode;
        try { Storage.Save(next); }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, "隐私设置未保存"); return; }
        settings = next;
        if (activeSettingsForm != null && !activeSettingsForm.IsDisposed) activeSettingsForm.ApplyPrivacy(next.PrivacyMode);
        if (activeOvertimeForm != null && !activeOvertimeForm.IsDisposed) activeOvertimeForm.ApplyPrivacy(next.PrivacyMode);
        if (activeAbsenceForm != null && !activeAbsenceForm.IsDisposed) activeAbsenceForm.ApplyPrivacy(next.PrivacyMode);
        Snapshot current = WorkClock.At(settings, DateTime.Now);
        foreach (Control control in detailBody.Controls) { DetailCard field = control as DetailCard; if (field != null) field.UpdateView(settings, current); }
        if (hoverPopup != null && !hoverPopup.IsDisposed) hoverPopup.SetMessage(BuildHoverMessage(settings, DateTime.Now));
        if (reminderPopup != null && !reminderPopup.IsDisposed && reminderPopup.Text == "下班总结 · 上班鼓励器") reminderPopup.SetMessage(SummaryMessage(settings, DateTime.Now));
        privacyButton.AccessibleName = next.PrivacyMode ? "关闭隐私模式" : "开启隐私模式";
        privacyButton.Invalidate(); Invalidate();
    }
    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        if (Program.Interactive)
        {
            privacyHotkey = RegisterHotKey(Handle, 0x4345, 0x4003, (uint)Keys.P);
            if (!privacyHotkey) BeginInvoke(new Action(delegate { tray.ShowBalloonTip(5000, "快捷键被占用", "Ctrl+Alt+P不可用，请点击详情顶部的小眼睛或使用右键菜单切换隐私模式。", ToolTipIcon.Info); }));
        }
    }
    protected override void OnHandleDestroyed(EventArgs e)
    {
        if (privacyHotkey) { UnregisterHotKey(Handle, 0x4345); privacyHotkey = false; }
        base.OnHandleDestroyed(e);
    }
    protected override void WndProc(ref Message message)
    {
        if (message.Msg == 0x0312 && message.WParam.ToInt32() == 0x4345) { TogglePrivacy(); return; }
        if (message.Msg == 0x007E && IsHandleCreated && !IsDisposed)
            BeginInvoke(new Action(delegate
            {
                Rectangle area = Screen.FromRectangle(Bounds).WorkingArea;
                Location = new Point(Math.Max(area.Left, Math.Min(Left, area.Right - Width)), Math.Max(area.Top, Math.Min(Top, area.Bottom - Height)));
                foreach (Form dialog in new Form[] { activeSettingsForm, activeOvertimeForm, activeAbsenceForm })
                {
                    if (dialog == null || dialog.IsDisposed) continue;
                    Rectangle available = Screen.FromRectangle(dialog.Bounds).WorkingArea;
                    dialog.Height = Math.Min(dialog.Height, Math.Max(100, available.Height - 20));
                    dialog.Location = new Point(Math.Max(available.Left, Math.Min(dialog.Left, available.Right - dialog.Width)), Math.Max(available.Top, Math.Min(dialog.Top, available.Bottom - dialog.Height)));
                }
            }));
        base.WndProc(ref message);
    }
    public void ShowHoverInfo(bool show)
    {
        if (!show)
        {
            ReminderPopup previous = hoverPopup; hoverPopup = null;
            if (previous != null && !previous.IsDisposed) previous.Close();
            return;
        }
        if (!settings.HoverEnabled || Visible || pet == null || !pet.Visible || reminderPopup != null && !reminderPopup.IsDisposed) return;
        string message = BuildHoverMessage(settings, DateTime.Now); if (message.Length == 0) return;
        if (hoverPopup != null && !hoverPopup.IsDisposed) { hoverPopup.SetMessage(message); return; }
        ReminderPopup popup = new ReminderPopup(message, settings.Theme, false, null, false) { Text = "悬浮信息 · 上班鼓励器", Name = "HoverInfo" }; hoverPopup = popup;
        popup.FormClosed += delegate { popup.Dispose(); if (hoverPopup == popup) hoverPopup = null; };
        DesktopPet.PlaceBeside(popup, pet.Bounds); popup.Show();
    }
    public void SetHoverComponentVisible(string key, bool visible)
    {
        if (Array.IndexOf(DetailCard.Keys, key) < 0) return;
        HashSet<string> selected = new HashSet<string>((settings.HoverVisible ?? "").Split(','));
        if (visible) selected.Add(key); else selected.Remove(key);
        List<string> items = new List<string>(); foreach (string item in DetailCard.Keys) if (selected.Contains(item)) items.Add(item);
        Settings next = settings.Copy(); next.HoverVisible = string.Join(",", items.ToArray());
        try { Storage.Save(next); settings = next; ShowHoverInfo(false); if (pet != null) pet.ApplySettings(settings); } catch (Exception ex) { MessageBox.Show(this, ex.Message, "悬浮内容未保存"); }
    }
    private void ApplyPet()
    {
        if (!settings.PetEnabled)
        {
            ShowHoverInfo(false);
            if (pet != null) { pet.Dispose(); pet = null; }
            if (!Visible) Show(); return;
        }
        if (pet == null || pet.IsDisposed)
            pet = new DesktopPet(settings, ToggleDetails, delegate(Point point)
            {
                settings.PetX = point.X; settings.PetY = point.Y;
                settings.PetDockEdge = pet.DockEdge;
                try { Storage.Save(settings); } catch (Exception ex) { MessageBox.Show(this, ex.Message, "桌宠位置未保存"); }
            }, ContextMenuStrip, delegate(double yaw, double pitch)
            {
                settings.PetYaw = yaw; settings.PetPitch = pitch;
                try { Storage.Save(settings); } catch (Exception ex) { MessageBox.Show(this, ex.Message, "视角未保存"); }
            }, ShowHoverInfo);
        pet.ApplySettings(settings); if (!pet.Visible) pet.Show();
    }
    public void SetComponentVisible(string key, bool visible)
    {
        if (Array.IndexOf(DetailCard.Keys, key) < 0) return;
        HashSet<string> selected = new HashSet<string>((settings.DetailVisible ?? "").Split(','));
        if (visible) selected.Add(key); else selected.Remove(key);
        List<string> chosen = new List<string>(); foreach (string item in DetailCard.Keys) if (selected.Contains(item)) chosen.Add(item);
        Settings next = settings.Copy(); next.DetailVisible = string.Join(",", chosen.ToArray());
        try { Storage.Save(next); settings = next; RebuildComponents(); } catch (Exception ex) { MessageBox.Show(this, ex.Message, "组件配置未保存"); }
    }
    public void MoveComponent(string source, string target)
    {
        List<string> order = new List<string>(settings.DetailOrder.Split(','));
        int at = order.IndexOf(target); if (at < 0 || !order.Contains(source) || source == target) return;
        order.Remove(source); order.Insert(Math.Min(at, order.Count), source);
        Settings next = settings.Copy(); next.DetailOrder = string.Join(",", order.ToArray());
        try { Storage.Save(next); settings = next; RebuildComponents(); } catch (Exception ex) { MessageBox.Show(this, ex.Message, "组件顺序未保存"); }
    }
    private void RebuildComponents()
    {
        if (detailBody == null) return;
        detailBody.SuspendLayout(); detailBody.AutoScrollMinSize = Size.Empty; detailBody.AutoScroll = false;
        while (detailBody.Controls.Count > 0) detailBody.Controls[0].Dispose();
        detailBody.AutoScroll = true;
        detailBody.AutoScrollPosition = Point.Empty;
        bool mini = settings.WidgetSize == 1;
        HashSet<string> selected = new HashSet<string>((settings.DetailVisible ?? "").Split(','));
        int total = 0; foreach (string key in settings.DetailOrder.Split(',')) if (selected.Contains(key)) total += DetailCard.CardHeight(key, mini) + 6;
        int width = detailBody.ClientSize.Width - (total > detailBody.ClientSize.Height ? SystemInformation.VerticalScrollBarWidth : 0), y = 0;
        foreach (string key in settings.DetailOrder.Split(','))
        {
            if (!selected.Contains(key)) continue;
            DetailCard component = new DetailCard(key, settings, MoveComponent, delegate
            {
                Point cursor = detailBody.PointToClient(Cursor.Position);
                int direction = cursor.Y < 24 ? -1 : cursor.Y > detailBody.Height - 24 ? 1 : 0;
                if (direction != 0) detailBody.AutoScrollPosition = new Point(0, Math.Max(0, -detailBody.AutoScrollPosition.Y + direction * 10));
            });
            component.Bounds = new Rectangle(0, y, Math.Max(130, width), DetailCard.CardHeight(key, mini)); detailBody.Controls.Add(component); y += component.Height + 6;
        }
        detailBody.AutoScrollMinSize = new Size(0, Math.Max(0, y - 6)); detailBody.ResumeLayout();
        if (y == 0) detailBody.Controls.Add(new Label { Text = "尚未显示组件\n点击顶部的“组件”勾选要查看的内容", Bounds = new Rectangle(6, 26, detailBody.Width - 12, 100), ForeColor = muted, Font = new Font("Microsoft YaHei UI", 9), TextAlign = ContentAlignment.MiddleCenter });
        componentMenu.Items.Clear();
        for (int i = 0; i < DetailCard.Keys.Length; i++)
        {
            string key = DetailCard.Keys[i]; ToolStripMenuItem item = new ToolStripMenuItem(DetailCard.Titles[i]) { Checked = selected.Contains(key), CheckOnClick = true };
            item.Click += delegate { SetComponentVisible(key, item.Checked); }; componentMenu.Items.Add(item);
        }
        Invalidate();
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
            if (!(control is Button)) continue;
            if (control.Name == "PrivacyToggle") control.Bounds = new Rectangle(Width - 38, mini ? 30 : 35, 24, 18);
            else if (control.Text.Contains("设置")) control.Bounds = new Rectangle(mini ? 184 : 222, mini ? 5 : 7, 66, 24);
            else if (control.Text == "组件") control.Bounds = new Rectangle(mini ? 128 : 166, mini ? 5 : 7, 50, 24);
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
        detailBody.Bounds = new Rectangle(12, mini ? 49 : 55, Width - 24, Height - (mini ? 81 : 91)); RebuildComponents();
    }
    private void OpenSettings()
    {
        if (activeAbsenceForm != null && !activeAbsenceForm.IsDisposed) { activeAbsenceForm.Activate(); return; }
        if (activeOvertimeForm != null && !activeOvertimeForm.IsDisposed) { activeOvertimeForm.Activate(); return; }
        if (activeSettingsForm != null && !activeSettingsForm.IsDisposed) { activeSettingsForm.Activate(); return; }
        ShowHoverInfo(false);
        Show(); TopMost = false;
        if (pet != null) { pet.Paused = true; pet.Enabled = false; }
        using (SettingsForm form = new SettingsForm(settings))
        {
            activeSettingsForm = form;
            form.TopMost = settings.AlwaysOnTop;
            if (form.ShowDialog(this) == DialogResult.OK) { settings = form.Result; ApplySize(); reminderClock.Reset(DateTime.Now); }
            activeSettingsForm = null;
        }
        TopMost = settings.AlwaysOnTop;
        foreach (Control control in Controls) if (control.Text == "已置顶" || control.Text == "置顶") control.Text = TopMost ? "已置顶" : "置顶";
        ApplyTheme();
        if (pet != null) { pet.Paused = false; pet.Enabled = true; }
        ApplyPet();
        Invalidate();
    }
    private void OpenOvertime()
    {
        if (activeAbsenceForm != null && !activeAbsenceForm.IsDisposed) { activeAbsenceForm.Activate(); return; }
        if (activeSettingsForm != null && !activeSettingsForm.IsDisposed) { activeSettingsForm.Activate(); return; }
        if (activeOvertimeForm != null && !activeOvertimeForm.IsDisposed) { activeOvertimeForm.Activate(); return; }
        ShowHoverInfo(false); bool wasVisible = Visible;
        Show(); TopMost = false; if (pet != null) { pet.Paused = true; pet.Enabled = false; }
        try
        {
            using (OvertimeForm form = new OvertimeForm(settings, delegate { return settings; }))
            {
                activeOvertimeForm = form; form.TopMost = settings.AlwaysOnTop;
                if (form.ShowDialog(this) == DialogResult.OK) { settings = form.Result; RebuildComponents(); reminderClock.Reset(DateTime.Now); }
            }
        }
        finally
        {
            activeOvertimeForm = null; TopMost = settings.AlwaysOnTop;
            if (pet != null) { pet.Paused = false; pet.Enabled = true; } ApplyPet();
            if (!wasVisible && settings.PetEnabled) Hide();
        }
    }
    private void OpenAbsence()
    {
        if (activeSettingsForm != null && !activeSettingsForm.IsDisposed) { activeSettingsForm.Activate(); return; }
        if (activeOvertimeForm != null && !activeOvertimeForm.IsDisposed) { activeOvertimeForm.Activate(); return; }
        if (activeAbsenceForm != null && !activeAbsenceForm.IsDisposed) { activeAbsenceForm.Activate(); return; }
        ShowHoverInfo(false); bool wasVisible = Visible;
        Show(); TopMost = false; if (pet != null) { pet.Paused = true; pet.Enabled = false; }
        try
        {
            using (AbsenceForm form = new AbsenceForm(settings, delegate { return settings; }))
            {
                activeAbsenceForm = form; form.TopMost = settings.AlwaysOnTop;
                if (form.ShowDialog(this) == DialogResult.OK) { settings = form.Result; RebuildComponents(); reminderClock.Reset(DateTime.Now); }
            }
        }
        finally
        {
            activeAbsenceForm = null; TopMost = settings.AlwaysOnTop;
            if (pet != null) { pet.Paused = false; pet.Enabled = true; } ApplyPet();
            if (!wasVisible && settings.PetEnabled) Hide();
        }
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
        if (disposing) { timer.Dispose(); reminderTimer.Dispose(); if (hoverPopup != null) hoverPopup.Dispose(); if (pet != null) pet.Dispose(); if (componentMenu != null) componentMenu.Dispose(); if (reminderPopup != null) reminderPopup.Dispose(); if (tray != null) { tray.Visible = false; tray.Dispose(); } if (appIcon != null) appIcon.Dispose(); }
        base.Dispose(disposing);
    }
    public static GraphicsPath Rounded(Rectangle rect, int radius)
    {
        GraphicsPath path = new GraphicsPath(); int size = radius * 2;
        path.AddArc(rect.Left, rect.Top, size, size, 180, 90); path.AddArc(rect.Right - size, rect.Top, size, size, 270, 90);
        path.AddArc(rect.Right - size, rect.Bottom - size, size, size, 0, 90); path.AddArc(rect.Left, rect.Bottom - size, size, size, 90, 90); path.CloseFigure(); return path;
    }
    public static void Write(Graphics g, string text, float size, Color color, RectangleF box, FontStyle style, StringAlignment alignment)
    {
        using (Font font = new Font("Microsoft YaHei UI", size, style))
        using (SolidBrush brush = new SolidBrush(color))
        using (StringFormat format = new StringFormat { Alignment = alignment, LineAlignment = StringAlignment.Center, FormatFlags = StringFormatFlags.NoWrap })
            g.DrawString(text, font, brush, box, format);
    }
    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e); Graphics g = e.Graphics; g.SmoothingMode = SmoothingMode.AntiAlias;
        bool mini = Width < 280;
        using (LinearGradientBrush wash = new LinearGradientBrush(ClientRectangle, background, card, 62f)) g.FillRectangle(wash, ClientRectangle);
        using (Pen border = new Pen(Color.FromArgb(90, secondary))) using (GraphicsPath path = Rounded(new Rectangle(0, 0, Width - 1, Height - 1), 22)) g.DrawPath(border, path);
        using (SolidBrush logo = new SolidBrush(accent)) g.FillEllipse(logo, mini ? 12 : 14, mini ? 17 : 19, 8, 8);
        Write(g, "上班鼓励器", mini ? 9.5f : 10, ink, new RectangleF(mini ? 27 : 30, mini ? 5 : 7, 125, 27), FontStyle.Bold, StringAlignment.Near);
        Write(g, settings.PrivacyMode ? "隐私模式 · 金额已隐藏" : "拖动组件排序 · 可滚动查看", 7, muted, new RectangleF(14, mini ? 31 : 36, Width - 60, 17), FontStyle.Regular, StringAlignment.Near);
    }
}

public static class Program
{
    public static bool Interactive;
    private static void ApplyWindowRegion(Form form, Bitmap image)
    {
        if (form.Region == null) return;
        using (Graphics g = Graphics.FromImage(image))
        using (SolidBrush transparent = new SolidBrush(Color.Transparent))
        {
            g.CompositingMode = CompositingMode.SourceCopy;
            g.SetClip(form.Region, CombineMode.Exclude);
            g.FillRectangle(transparent, 0, 0, image.Width, image.Height);
        }
    }
    [STAThread]
    public static void Main(string[] args)
    {
        Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false);
        if (args.Length > 0 && args[0] == "--self-test")
        {
            Tests.Run(); return;
        }
        if (args.Length > 0 && args[0] == "--leave-preview")
        {
            for (int theme = 0; theme < 2; theme++)
            {
                Settings demo = new Settings { MonthlySalary = 8800, Theme = theme, DailySummaryEnabled = false, PetEnabled = false, WorkMode = 0, SingleRest = ((int)DateTime.Today.DayOfWeek + 1) % 7, DetailOrder = "absence,income,rate,progress,countdown,encouragement,month,overtime", DetailVisible = "absence,income" };
                demo.Absences.Add(new AbsenceEntry { From = DateTime.Today.AddHours(9), Until = DateTime.Today.AddHours(11), Kind = 0, PayPercent = 0 });
                demo.OvertimeEntries.Add(new OvertimeEntry { Start = DateTime.Today.AddDays(-1).AddHours(18), Minutes = 60, HourlyRate = 50 });
                using (AbsenceForm preview = new AbsenceForm(demo))
                {
                    preview.Opacity = 0; preview.Show(); preview.Hide(); preview.Show(); Application.DoEvents();
                    using (Bitmap bitmap = new Bitmap(preview.Width, preview.Height)) { preview.DrawToBitmap(bitmap, new Rectangle(0, 0, preview.Width, preview.Height)); bitmap.Save(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "leave-settings-" + theme + ".png")); }
                    preview.Hide();
                }
                using (Widget preview = new Widget(demo))
                {
                    preview.Opacity = 0; preview.Show(); Application.DoEvents();
                    foreach (Control card in preview.Controls.Find("Detail_absence", true)) ((DetailCard)card).UpdateView(demo, WorkClock.At(demo, DateTime.Today.AddHours(18)));
                    foreach (Control card in preview.Controls.Find("Detail_income", true)) ((DetailCard)card).UpdateView(demo, WorkClock.At(demo, DateTime.Today.AddHours(18)));
                    using (Bitmap bitmap = new Bitmap(preview.Width, preview.Height)) { preview.DrawToBitmap(bitmap, preview.ClientRectangle); ApplyWindowRegion(preview, bitmap); bitmap.Save(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "leave-component-" + theme + ".png")); }
                    preview.Hide();
                }
            }
            return;
        }
        if (args.Length > 0 && args[0] == "--preview")
        {
            Settings overtimeDemo = new Settings { MonthlySalary = 8800, DailySummaryEnabled = false, PetEnabled = false, DetailOrder = "overtime,income,rate,progress,countdown,encouragement,month,absence", DetailVisible = "overtime,income" };
            overtimeDemo.OvertimeEntries.Add(new OvertimeEntry { Start = DateTime.Today.AddDays(-1).AddHours(18), Minutes = 120, HourlyRate = 50 });
            using (OvertimeForm preview = new OvertimeForm(overtimeDemo))
            {
                preview.Opacity = 0; preview.Show(); Application.DoEvents();
                using (Bitmap image = new Bitmap(preview.Width, preview.Height)) { preview.DrawToBitmap(image, new Rectangle(0, 0, preview.Width, preview.Height)); image.Save(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "overtime-settings-preview.png")); }
                preview.Hide();
            }
            using (Widget preview = new Widget(overtimeDemo))
            {
                preview.Opacity = 0; preview.Show(); Application.DoEvents();
                using (Bitmap image = new Bitmap(preview.Width, preview.Height)) { preview.DrawToBitmap(image, preview.ClientRectangle); ApplyWindowRegion(preview, image); image.Save(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "overtime-preview.png")); }
                preview.Hide();
            }
            for (int privacy = 0; privacy < 2; privacy++)
            using (Widget preview = new Widget(new Settings { MonthlySalary = 8800, PrivacyMode = privacy == 1, DailySummaryEnabled = false, PetEnabled = false, DetailOrder = "month,income,rate,progress,countdown,encouragement,overtime,absence", DetailVisible = "month,income,progress" }))
            {
                preview.Opacity = 0; preview.Show(); Application.DoEvents();
                using (Bitmap image = new Bitmap(preview.Width, preview.Height)) { preview.DrawToBitmap(image, preview.ClientRectangle); ApplyWindowRegion(preview, image); image.Save(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, privacy == 0 ? "monthly-preview.png" : "monthly-private-preview.png")); }
                preview.Hide();
            }
            using (Bitmap bread = new Bitmap(1000, 524))
            using (Graphics g = Graphics.FromImage(bread))
            {
                g.Clear(Color.FromArgb(246, 247, 252));
                string[] labels = { "面包猫 · 正面", "面包猫 · 举臂", "面包猫 · 侧面", "面包猫 · 背面", "睡觉", "摸鱼", "工作", "被拎起" };
                double[] angles = { 0, 25, 90, 180 };
                for (int i = 0; i < labels.Length; i++)
                {
                    using (Bitmap image = PetModel3D.Render(5, 0, 18, i < 4 || i == 7 ? 0 : i - 3, i < 4 ? angles[i] : 25, 12, false, false, i == 7, 250)) g.DrawImage(image, i % 4 * 250, i / 4 * 262);
                    Widget.Write(g, labels[i], 10, Color.FromArgb(64, 69, 88), new RectangleF(i % 4 * 250, i / 4 * 262 + 237, 250, 25), FontStyle.Regular, StringAlignment.Center);
                }
                bread.Save(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "bread-cat-preview.png"));
            }
            using (Bitmap expressions = new Bitmap(PetArt.Names.Length * 200, 430))
            using (Graphics g = Graphics.FromImage(expressions))
            {
                g.Clear(Color.FromArgb(246, 247, 252));
                for (int row = 0; row < 2; row++) for (int character = 0; character < PetArt.Names.Length; character++)
                {
                    using (Bitmap image = PetModel3D.Render(character, 0, 18, 0, 25, 12, false, false, row == 1)) g.DrawImage(image, character * 200, row * 215);
                    Widget.Write(g, PetArt.Names[character] + (row == 0 ? " · 平时" : " · 拖动惊讶"), 9, Color.FromArgb(64, 69, 88), new RectangleF(character * 200, row * 215 + 190, 200, 25), FontStyle.Regular, StringAlignment.Center);
                }
                expressions.Save(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "drag-preview.png"));
            }
            using (Widget preview = new Widget(new Settings { MonthlySalary = 8800, PrivacyMode = true, DailySummaryEnabled = false, PetEnabled = false }))
            {
                preview.Opacity = 0; preview.Show(); Application.DoEvents();
                using (Bitmap image = new Bitmap(preview.Width, preview.Height)) { preview.DrawToBitmap(image, preview.ClientRectangle); ApplyWindowRegion(preview, image); image.Save(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "privacy-preview.png")); }
                preview.Hide();
            }
            using (Bitmap docks = new Bitmap(PetArt.Names.Length * 200, 1075))
            using (Graphics g = Graphics.FromImage(docks))
            {
                g.Clear(Color.FromArgb(246, 247, 252));
                string[] edges = { "左边", "右边", "上边", "下边", "悬停展开" };
                for (int row = 0; row < 5; row++) for (int character = 0; character < PetArt.Names.Length; character++)
                {
                    using (Bitmap image = PetModel3D.RenderDocked(character, 0, 18, row < 4 ? row + 1 : 4, row == 4 ? 1 : 0)) g.DrawImage(image, character * 200, row * 215);
                    Widget.Write(g, PetArt.Names[character] + " · " + edges[row], 9, Color.FromArgb(64, 69, 88), new RectangleF(character * 200, row * 215 + 190, 200, 25), FontStyle.Regular, StringAlignment.Center);
                }
                docks.Save(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "dock-preview.png"));
            }
            using (ReminderPopup preview = new ReminderPopup(Widget.BuildHoverMessage(new Settings { MonthlySalary = 8800 }, DateTime.Now), 0, false, null, false))
            using (Bitmap bitmap = new Bitmap(preview.Width, preview.Height))
            {
                preview.Opacity = 0; preview.Show(); Application.DoEvents(); preview.DrawToBitmap(bitmap, preview.ClientRectangle); preview.Hide(); ApplyWindowRegion(preview, bitmap);
                bitmap.Save(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "hover-preview.png"));
            }
            using (Bitmap poses = new Bitmap(PetArt.Names.Length * 200, 860))
            using (Graphics g = Graphics.FromImage(poses))
            {
                g.Clear(Color.FromArgb(246, 247, 252));
                for (int pose = 0; pose < 4; pose++) for (int character = 0; character < PetArt.Names.Length; character++)
                {
                    using (Bitmap image = PetModel3D.Render(character, 0, 18, pose, 25, 12, false, false)) g.DrawImage(image, character * 200, pose * 215);
                    Widget.Write(g, PetArt.Names[character] + " · " + PetModel3D.PoseNames[pose], 9, Color.FromArgb(64, 69, 88), new RectangleF(character * 200, pose * 215 + 187, 200, 25), FontStyle.Bold, StringAlignment.Center);
                }
                poses.Save(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "poses-3d-preview.png"));
            }
            using (Bitmap views = new Bitmap(1000, 220))
            using (Graphics g = Graphics.FromImage(views))
            {
                g.Clear(Color.FromArgb(246, 247, 252)); double[] angles = { 0, 45, 90, 180, 270 };
                for (int index = 0; index < angles.Length; index++)
                {
                    using (Bitmap image = PetModel3D.Render(4, 0, 18, 0, angles[index], 12, false, false)) g.DrawImage(image, index * 200, 0);
                    Widget.Write(g, angles[index] + "°", 9, Color.FromArgb(64, 69, 88), new RectangleF(index * 200, 187, 200, 25), FontStyle.Bold, StringAlignment.Center);
                }
                views.Save(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "views-3d-preview.png"));
            }
            using (Bitmap sheet = new Bitmap(PetArt.Names.Length * 200, 460))
            using (Graphics g = Graphics.FromImage(sheet))
            {
                g.Clear(Color.FromArgb(246, 247, 252));
                for (int theme = 0; theme < 2; theme++) for (int character = 0; character < PetArt.Names.Length; character++)
                {
                    using (Bitmap pet = PetArt.Render(character, theme, 16, false, false))
                    {
                        pet.Save(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "pet-" + character + "-" + theme + ".png"));
                        g.DrawImage(pet, character * 200, theme * 230);
                    }
                    Widget.Write(g, PetArt.Names[character], 10, Color.FromArgb(64, 69, 88), new RectangleF(character * 200, theme * 230 + 190, 200, 25), FontStyle.Bold, StringAlignment.Center);
                }
                sheet.Save(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "pets-preview.png"));
            }
            using (ReminderPopup preview = new ReminderPopup("喝点水；如有需要，起身去趟洗手间。", 0, false, "猫咪"))
            using (Bitmap bitmap = new Bitmap(preview.Width, preview.Height))
            {
                preview.Opacity = 0; preview.Show(); Application.DoEvents(); preview.DrawToBitmap(bitmap, preview.ClientRectangle); preview.Hide();
                ApplyWindowRegion(preview, bitmap);
                bitmap.Save(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "pet-bubble.png"));
            }
            for (int theme = 0; theme < 2; theme++)
            for (int size = 0; size < 2; size++)
                using (Widget preview = new Widget(new Settings { MonthlySalary = 8800, Theme = theme, WidgetSize = size, DailySummaryEnabled = false, PetEnabled = false }))
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
                    ApplyWindowRegion(preview, bitmap);
                    bitmap.Save(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, theme == 0 ? "reminder-neon.png" : "reminder-starlight.png"));
                }
            for (int theme = 0; theme < 2; theme++)
                using (ReminderPopup preview = new ReminderPopup("你今日赚了 ¥400.00（估算）。", theme, true))
                using (Bitmap bitmap = new Bitmap(preview.Width, preview.Height))
                {
                    preview.Opacity = 0; preview.Show(); Application.DoEvents(); preview.DrawToBitmap(bitmap, preview.ClientRectangle); preview.Hide();
                    ApplyWindowRegion(preview, bitmap);
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
            if (!first) { if (Array.IndexOf(args, "--startup") < 0) MessageBox.Show("上班鼓励器已在运行。请双击任务栏右下角的托盘图标显示挂件。", "上班鼓励器"); return; }
            Settings settings;
            try { settings = Storage.Load(); }
            catch (Exception ex)
            {
                MessageBox.Show("设置读取失败，暂用初始设置。原设置文件保留，保存新设置时会备份原文件。\n" + ex.Message, "上班鼓励器");
                settings = new Settings();
            }
            Interactive = true;
            using (Widget widget = new Widget(settings)) Application.Run(widget);
        }
    }
}

public static class Tests
{
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
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
            Settings s = new Settings { MonthlySalary = 8800, ManualDays = true, Days = 22, ManualMonth = "2026-11", DailySummaryEnabled = false, PetEnabled = false };
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
                Panel body = (Panel)themed.Controls["DetailBody"];
                check(body.Controls["Detail_income"] is DetailCard && body.Controls["Detail_rate"] is DetailCard, "Detail panel exposes separate income and rate components");
                themed.SetComponentVisible("rate", false);
                check(body.Controls["Detail_rate"] == null && !Storage.Load().DetailVisible.Contains("rate"), "Hiding a component updates view and saved settings");
                DetailCard dropTarget = (DetailCard)body.Controls["Detail_countdown"];
                DataObject drag = new DataObject(DataFormats.Text, "WorkEncouragerCard:income");
                System.Reflection.MethodInfo drop = typeof(Control).GetMethod("OnDragDrop", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
                drop.Invoke(dropTarget, new object[] { new DragEventArgs(drag, 0, 0, 0, DragDropEffects.Move, DragDropEffects.Move) });
                Application.DoEvents();
                check(Storage.Load().DetailOrder.IndexOf("income") > Storage.Load().DetailOrder.IndexOf("countdown"), "Drag payload moves component through real drop event and persists order");
                using (Widget restored = new Widget(Storage.Load()))
                    check(((Panel)restored.Controls["DetailBody"]).Controls["Detail_rate"] == null, "Component visibility survives restarting detail panel");
                foreach (string key in DetailCard.Keys) themed.SetComponentVisible(key, false);
                bool recover = false; foreach (Control control in themed.Controls) if (control.Text == "组件") recover = true;
                check(body.Controls.Count == 1 && body.Controls[0] is Label && recover, "Hiding all components retains an explanation and configuration entry");
                foreach (string key in DetailCard.Keys) themed.SetComponentVisible(key, true);
                check(body.Controls.Count == DetailCard.Keys.Length && Storage.Load().MonthlySalary == s.MonthlySalary, "Components can be restored without changing salary");
                themed.Hide();
            }
            using (StringReader legacy = new StringReader("<Settings><MonthlySalary>8800</MonthlySalary></Settings>"))
                check(((Settings)new XmlSerializer(typeof(Settings)).Deserialize(legacy)).WidgetSize == 0, "Legacy settings default to compact size");
            using (SettingsForm dialog = new SettingsForm(s))
            {
                dialog.CreateControl();
                check(dialog.Controls.Count > 20, "Settings window builds all fields");
            }
            using (StringReader legacy = new StringReader("<Settings><MonthlySalary>8800</MonthlySalary></Settings>"))
            {
                Settings old = (Settings)new XmlSerializer(typeof(Settings)).Deserialize(legacy); old.Validate();
                check(old.PetEnabled && old.PetCharacter == 0 && old.DetailOrder.Split(',').Length == DetailCard.Keys.Length, "Legacy configuration enables default pet and all detail components");
            }
            Settings petSettings = new Settings { PetCharacter = 3, PetX = 100, PetY = 200, PetWalk = true, DailySummaryEnabled = false };
            Storage.Save(petSettings);
            check(Storage.Load().PetCharacter == 3 && Storage.Load().PetX == 100 && Storage.Load().PetY == 200 && Storage.Load().PetWalk, "Pet character, position and walking option persist");
            DateTime hoverTime = new DateTime(2026, 11, 2, 10, 0, 0);
            PetHoverState hoverState = new PetHoverState(); hoverState.Enter(hoverTime);
            check(!hoverState.ShouldShow(hoverTime.AddMilliseconds(449), false) && hoverState.ShouldShow(hoverTime.AddMilliseconds(450), false), "Hover preview waits for a short dwell");
            hoverState.Leave(); check(!hoverState.ShouldShow(hoverTime.AddSeconds(2), false), "Leaving the pet hides hover preview");
            hoverState.Enter(hoverTime); hoverState.Suppress(); check(!hoverState.ShouldShow(hoverTime.AddSeconds(2), false), "Clicking or dragging suppresses preview for the current hover");
            hoverState.Leave(); hoverState.Enter(hoverTime); check(hoverState.ShouldShow(hoverTime.AddSeconds(1), false), "Reentering the pet permits hover preview again");
            check(!hoverState.ShouldShow(hoverTime.AddSeconds(2), true) && !hoverState.ShouldShow(hoverTime.AddMilliseconds(2200), false) && hoverState.ShouldShow(hoverTime.AddMilliseconds(2500), false), "Preview waits again after a reminder or interaction blocks it");
            Settings hoverConfig = new Settings { MonthlySalary = 8800, DailySummaryEnabled = false, HoverVisible = "rate,progress" };
            string hoverText = Widget.BuildHoverMessage(hoverConfig, new DateTime(2026, 11, 2, 14, 0, 0));
            check(hoverText.Contains("每秒收入") && hoverText.Contains("上班进度") && !hoverText.Contains("今日收入") && !hoverText.Contains("剩余工时") && !hoverText.Contains("月薪"), "Hover content respects its own selection without displaying monthly salary");
            hoverConfig.HoverVisible = ""; check(Widget.BuildHoverMessage(hoverConfig, hoverTime) == "", "No selected hover fields produces no message");
            hoverConfig.HoverVisible = "progress,encouragement"; hoverConfig.HoverEnabled = false; Storage.Save(hoverConfig);
            check(!Storage.Load().HoverEnabled && Storage.Load().HoverVisible == hoverConfig.HoverVisible && Storage.Load().DetailVisible == hoverConfig.DetailVisible, "Hover configuration persists independently of detail components");
            using (StringReader legacy = new StringReader("<Settings><MonthlySalary>8800</MonthlySalary></Settings>"))
            {
                Settings old = (Settings)new XmlSerializer(typeof(Settings)).Deserialize(legacy);
                check(old.HoverEnabled && old.HoverVisible.Contains("income"), "Older configurations receive hover defaults");
            }
            hoverConfig.HoverEnabled = true; hoverConfig.HoverVisible = "income,rate,progress,countdown";
            using (Widget hoverOwner = new Widget(hoverConfig))
            {
                hoverOwner.Show(); Application.DoEvents();
                // 此用例验证详情/预览的分离，避免真实鼠标移动干扰手动调用。
                foreach (Form form in Application.OpenForms) { DesktopPet hoverPet = form as DesktopPet; if (hoverPet != null) hoverPet.Paused = true; }
                hoverOwner.ShowHoverInfo(true); Application.DoEvents();
                Form shownHover = null; foreach (Form form in Application.OpenForms) if (form.Name == "HoverInfo") shownHover = form;
                if (shownHover == null || !shownHover.Visible || hoverOwner.Visible)
                    foreach (Form form in Application.OpenForms) report.AppendLine("Hover diagnostic: " + form.GetType().Name + " visible=" + form.Visible + " disposed=" + form.IsDisposed);
                check(shownHover != null && shownHover.Visible && !hoverOwner.Visible, "Hover opens a separate preview without opening full details");
                ToolStripMenuItem toggleDetails = null;
                foreach (ToolStripItem item in hoverOwner.ContextMenuStrip.Items) if (item.Text == "展开 / 收起详情") toggleDetails = item as ToolStripMenuItem;
                toggleDetails.PerformClick(); Application.DoEvents();
                check(hoverOwner.Visible && shownHover.IsDisposed, "Click detail action replaces hover preview with pinned full details");
                hoverOwner.ShowHoverInfo(false); check(hoverOwner.Visible, "Mouse leave does not close click-opened details");
                hoverOwner.SetHoverComponentVisible("income", false);
                check(!Storage.Load().HoverVisible.Contains("income") && Storage.Load().DetailVisible.Contains("income"), "Changing hover selection leaves detail components unchanged");
                toggleDetails.PerformClick(); hoverOwner.ShowHoverInfo(true); Application.DoEvents();
                hoverOwner.ShowMessage("测试提醒", false); Application.DoEvents();
                bool previewRemains = false; foreach (Form form in Application.OpenForms) if (form.Name == "HoverInfo") previewRemains = true;
                check(!previewRemains, "Reminder notifications take priority over hover preview");
            }
            petSettings.PetYaw = 145; petSettings.PetPitch = 35; petSettings.PetPose = 3; Storage.Save(petSettings);
            check(Storage.Load().PetYaw == 145 && Storage.Load().PetPitch == 35 && Storage.Load().PetPose == 3, "Three-dimensional camera and manual pose persist");
            DateTime behaviorTime = new DateTime(2026, 11, 2, 10, 0, 0);
            PetBehavior behavior = new PetBehavior(behaviorTime, 17); HashSet<int> seenPoses = new HashSet<int>();
            check((behavior.NextChange - behaviorTime).TotalSeconds >= 10 && (behavior.NextChange - behaviorTime).TotalSeconds <= 20, "Automatic pose initially waits ten to twenty seconds");
            check(behavior.Update(-1, behavior.NextChange.AddMilliseconds(-1), false) == 0, "Pose does not change before its deadline");
            bool correctIntervals = true, noImmediateRepeat = true;
            for (int i = 0; i < 40; i++)
            {
                int before = behavior.Current; DateTime due = behavior.NextChange;
                int after = behavior.Update(-1, due, false); seenPoses.Add(after);
                if (after == before) noImmediateRepeat = false;
                double seconds = (behavior.NextChange - due).TotalSeconds; if (seconds < 10 || seconds > 20) correctIntervals = false;
            }
            check(correctIntervals && noImmediateRepeat && seenPoses.Count == 4, "Random behavior covers all four poses without immediate repeats and uses ten-to-twenty-second intervals");
            int pausedPose = behavior.Current; DateTime pausedAt = behavior.NextChange.AddSeconds(5);
            check(behavior.Update(-1, pausedAt, true) == pausedPose, "Speaking or interaction holds the current pose");
            check(behavior.Update(1, pausedAt, false) == 1 && behavior.Update(1, pausedAt.AddMinutes(5), false) == 1, "Manual sleeping pose stays selected");
            check(behavior.Update(-1, pausedAt.AddMinutes(6), false) == 1, "Returning to automatic mode starts a new waiting interval");
            using (Bitmap front = PetModel3D.Render(4, 0, 10, 0, 0, 12, false, false))
            using (Bitmap back = PetModel3D.Render(4, 0, 10, 0, 180, 12, false, false))
            {
                int differences = 0;
                for (int y = 0; y < front.Height; y += 2) for (int x = 0; x < front.Width; x += 2) if (front.GetPixel(x, y) != back.GetPixel(x, y)) differences++;
                check(differences > 400, "Camera rotation reveals a different three-dimensional back view");
            }
            for (int pose = 0; pose < 4; pose++)
            using (Bitmap view = PetModel3D.Render(0, 0, 10, pose, 35, 20, false, false))
            {
                int count = 0; for (int y = 0; y < view.Height; y += 3) for (int x = 0; x < view.Width; x += 3) if (view.GetPixel(x, y).A > 100) count++;
                check(count > 150 && view.GetPixel(0, 0).A == 0, "Three-dimensional pose renders a visible model on transparent background: " + pose);
            }
            System.Diagnostics.Stopwatch renderWatch = System.Diagnostics.Stopwatch.StartNew();
            for (int i = 0; i < 20; i++) using (Bitmap rendered = PetModel3D.Render(0, 0, i, 0, 25, 12, false, false)) { }
            report.AppendLine("Render benchmark: " + (renderWatch.Elapsed.TotalMilliseconds / 20).ToString("F1", CultureInfo.InvariantCulture) + " ms/frame (warm renderer)");
            renderWatch.Restart();
            for (int i = 0; i < 20; i++) using (Bitmap rendered = PetModel3D.Render(0, 0, i + 48, 0, 25, 12, false, false)) { }
            report.AppendLine("Cached animation benchmark: " + (renderWatch.Elapsed.TotalMilliseconds / 20).ToString("F2", CultureInfo.InvariantCulture) + " ms/frame");
            for (int character = 0; character < PetArt.Names.Length; character++)
            using (Bitmap petImage = PetArt.Render(character, 0, 10, false, false))
            {
                check(petImage.GetPixel(0, 0).A == 0 && petImage.GetPixel(100, 100).A > 0, "Pet silhouette has transparent surroundings and opaque body: " + character);
            }
            using (ContextMenuStrip menu = new ContextMenuStrip())
            using (DesktopPet desktop = new DesktopPet(petSettings, delegate { }, delegate(Point position) { }, menu))
            {
                petSettings.PetWalk = false;
                IntPtr focused = GetForegroundWindow(); desktop.Show(); Application.DoEvents();
                check(desktop.Visible && desktop.IsHandleCreated, "Native alpha-composited pet window renders successfully");
                check(GetForegroundWindow() == focused || GetForegroundWindow() != desktop.Handle, "Showing desktop pet does not steal keyboard focus");
                for (int character = 0; character < PetArt.Names.Length; character++) { petSettings.PetCharacter = character; desktop.ApplySettings(petSettings); }
                check(desktop.Visible && desktop.ClientSize == new Size(200, 190), "All characters switch in the same small native window");
                using (ReminderPopup bubble = new ReminderPopup("喝水时间到了", 0, false, "猫咪"))
                {
                    DesktopPet.PlaceBeside(bubble, desktop.Bounds);
                    check(Screen.FromRectangle(desktop.Bounds).WorkingArea.Contains(bubble.Bounds), "Pet speech bubble stays within screen working area");
                }
                desktop.Hide();
            }
            Rectangle dockArea = new Rectangle(-1600, -200, 1600, 900);
            Point[] dockPoints = { new Point(-1590, 100), new Point(-210, 100), new Point(-900, -190), new Point(-900, 500) };
            for (int edge = 1; edge <= 4; edge++)
            using (ContextMenuStrip menu = new ContextMenuStrip())
            using (DesktopPet desktop = new DesktopPet(petSettings, delegate { }, delegate(Point point) { }, menu))
            {
                desktop.DockAt(dockPoints[edge - 1], dockArea, false);
                check(desktop.DockEdge == edge && dockArea.Contains(desktop.Bounds), "Four-edge docking keeps the whole native window on a negative-coordinate monitor: " + edge);
                using (ReminderPopup bubble = new ReminderPopup("测试提醒", 0))
                { DesktopPet.PlaceBeside(bubble, desktop.Bounds); check(Screen.FromRectangle(desktop.Bounds).WorkingArea.Contains(bubble.Bounds), "Docked reminders remain within the available screen: " + edge); }
                desktop.DockAt(new Point(-900, 100), dockArea, false);
                check(desktop.DockEdge == 0 && desktop.Location == new Point(-900, 100), "Dragging away releases docking without altering the free position: " + edge);
            }
            check(DesktopPet.FindDockEdge(new Rectangle(-900, 100, 200, 190), dockArea) == 0, "Interior positions do not trigger docking");
            Settings dockSettings = petSettings.Copy(); dockSettings.PetDockEdge = 4; Storage.Save(dockSettings);
            check(Storage.Load().PetDockEdge == 4 && Storage.Load().PetPose == petSettings.PetPose && Storage.Load().HoverVisible == petSettings.HoverVisible, "Dock edge persists independently of pose and hover configuration");
            for (int character = 0; character < PetArt.Names.Length; character++)
            using (Bitmap tucked = PetModel3D.RenderDocked(character, 0, 10, 4, 0))
            using (Bitmap expanded = PetModel3D.RenderDocked(character, 0, 10, 4, 1))
            {
                int small = 0, full = 0;
                for (int y = 0; y < 190; y++) for (int x = 0; x < 200; x++) { if (tucked.GetPixel(x, y).A > 30) small++; if (expanded.GetPixel(x, y).A > 30) full++; }
                check(small > 250 && small < full / 2 && tucked.GetPixel(100, 30).A == 0, "Docking shows a visible partial head; expanding reveals the whole character: " + character);
            }
            invalid = s.Copy(); invalid.DetailOrder = "income,income,progress,countdown,encouragement"; rejected = false;
            try { invalid.Validate(); } catch { rejected = true; } check(rejected, "Duplicate component order is rejected");
            invalid = s.Copy(); invalid.PetPose = 4; rejected = false;
            try { invalid.Validate(); } catch { rejected = true; } check(rejected, "Unknown three-dimensional pose rejected");
            invalid = s.Copy(); invalid.PetYaw = double.NaN; rejected = false;
            try { invalid.Validate(); } catch { rejected = true; } check(rejected, "Invalid camera value rejected");
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
            check(DesktopPet.SizeFor(50, 96, new Size(1920, 1080)) == new Size(100, 95) && DesktopPet.SizeFor(150, 96, new Size(1920, 1080)) == new Size(300, 285), "Pet scale covers both slider limits");
            check(DesktopPet.SizeFor(100, 144, new Size(1920, 1080)) == new Size(300, 285) && DesktopPet.SizeFor(75, 192, new Size(1920, 1080)) == new Size(300, 285), "User scale composes with different monitor DPI values");
            Size tiny = DesktopPet.SizeFor(150, 288, new Size(240, 180));
            check(tiny.Width < 240 && tiny.Height < 180, "Large scaled pets fit into a small working area");
            invalid = s.Copy(); invalid.PetScale = 49; rejected = false; try { invalid.Validate(); } catch { rejected = true; } check(rejected, "Out-of-range pet scale is rejected");
            using (StringReader legacy = new StringReader("<Settings><MonthlySalary>8800</MonthlySalary></Settings>"))
            { Settings old = (Settings)new XmlSerializer(typeof(Settings)).Deserialize(legacy); check(old.PetScale == 100 && !old.PrivacyMode, "Legacy settings preserve size and visibility defaults"); }
            Settings privateSettings = new Settings { MonthlySalary = 8800, ManualDays = true, Days = 22, ManualMonth = "2026-11", PrivacyMode = true, PetEnabled = false, DailySummaryEnabled = false };
            string masked = Widget.BuildHoverMessage(privateSettings, new DateTime(2026, 11, 2, 18, 0, 0));
            check(masked.Contains("¥ ****") && !masked.Contains("400.00") && masked.Contains("上班进度"), "Privacy masks hover amounts while retaining progress");
            check(!Widget.SummaryMessage(privateSettings, new DateTime(2026, 11, 2, 18, 0, 0)).Contains("400.00") && Widget.SummaryMessage(privateSettings, DateTime.Now).Contains("已隐藏"), "Private daily summaries never contain an amount");
            check(WorkClock.At(privateSettings, new DateTime(2026, 11, 2, 18, 0, 0)).Earned == 400, "Privacy leaves salary calculations unchanged");
            Storage.Save(privateSettings);
            using (Widget owner = new Widget(privateSettings))
            {
                owner.Opacity = 0; owner.Show(); Application.DoEvents();
                owner.ShowMessage("你今日赚了 ¥400.00（估算）。", true); Application.DoEvents();
                ReminderPopup summaryPopup = null; foreach (Form form in Application.OpenForms) if (form.Text == "下班总结 · 上班鼓励器") summaryPopup = form as ReminderPopup;
                check(summaryPopup != null && !summaryPopup.Controls[0].AccessibleName.Contains("400.00"), "Showing a summary enforces privacy even for preformatted messages");
                Button eye = owner.Controls["PrivacyToggle"] as Button; eye.PerformClick(); Application.DoEvents();
                check(!Storage.Load().PrivacyMode && Storage.Load().MonthlySalary == privateSettings.MonthlySalary && summaryPopup.Controls[0].AccessibleName.Contains("¥"), "Eye button reveals amounts without changing salary configuration");
                eye.PerformClick(); Application.DoEvents();
                check(Storage.Load().PrivacyMode && !summaryPopup.Controls[0].AccessibleName.Contains("¥"), "Eye button immediately redacts an already-open summary");
                summaryPopup.Close(); owner.Hide();
            }
            using (SettingsForm dialog = new SettingsForm(privateSettings))
            {
                dialog.Opacity = 0; dialog.Show(); Application.DoEvents();
                NumericUpDown salaryField = null; foreach (Control control in dialog.Controls) if (control is NumericUpDown && ((NumericUpDown)control).Maximum == 100000000) salaryField = (NumericUpDown)control;
                CheckBox reveal = (CheckBox)dialog.Controls["RevealSalary"];
                check(salaryField != null && !salaryField.Visible && dialog.Controls["HiddenSalary"].Visible, "Privacy hides salary when opening settings");
                reveal.Checked = true; check(salaryField.Visible, "Salary can be explicitly revealed for editing");
                dialog.ApplyPrivacy(true); check(!salaryField.Visible && !reveal.Checked, "Toggling privacy hides a revealed salary again");
                ((TrackBar)dialog.Controls["PetScale"]).Value = 75; ((Button)dialog.AcceptButton).PerformClick();
                check(Storage.Load().PetScale == 75 && Storage.Load().PrivacyMode, "Scale and privacy survive saving through the settings dialog");
            }
            using (ContextMenuStrip menu = new ContextMenuStrip())
            using (DesktopPet desktop = new DesktopPet(new Settings { PetScale = 50 }, delegate { }, delegate(Point p) { }, menu))
            {
                desktop.Show(); Application.DoEvents(); Size small = desktop.ClientSize;
                desktop.ApplySettings(new Settings { PetScale = 150 }); Application.DoEvents();
                check(Math.Abs(desktop.Width - small.Width * 3) <= 2, "Native transparent pet resizes without losing its handle");
                Rectangle area = Screen.FromRectangle(desktop.Bounds).WorkingArea;
                desktop.Location = new Point(area.Right + 5000, area.Bottom + 5000); desktop.FitDisplay();
                check(Screen.FromRectangle(desktop.Bounds).WorkingArea.Contains(desktop.Bounds), "An unreachable saved monitor position returns to an available display");
                desktop.DockAt(new Point(area.Right - desktop.Width, area.Top + 30), area, false);
                desktop.ApplySettings(new Settings { PetScale = 75 });
                check(desktop.DockEdge == 2 && desktop.Right == Screen.FromRectangle(desktop.Bounds).WorkingArea.Right, "Changing pet size retains docking at the screen edge");
                desktop.Hide();
            }
            for (int character = 0; character < PetArt.Names.Length; character++)
            using (Bitmap normal = PetModel3D.Render(character, 0, 18, 0, 25, 12, false, false))
            using (Bitmap surprised = PetModel3D.Render(character, 0, 18, 0, 25, 12, false, false, true))
            using (Bitmap restored = PetModel3D.Render(character, 0, 18, 0, 25, 12, false, false))
            {
                int changed = 0, changedBack = 0;
                for (int y = 0; y < 190; y++) for (int x = 0; x < 200; x++) { if (normal.GetPixel(x, y) != surprised.GetPixel(x, y)) changed++; if (normal.GetPixel(x, y) != restored.GetPixel(x, y)) changedBack++; }
                check(changed > 50 && changedBack == 0, "Drag expression changes the model and restores without polluting cached idle frames: " + character);
            }
            string registryTest = @"Software\WorkEncouragerVerification_" + Guid.NewGuid().ToString("N");
            string startupBefore = StartupOption.CurrentCommand;
            try
            {
                using (Microsoft.Win32.RegistryKey key = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(registryTest))
                {
                    string examplePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "folder with spaces", "上班鼓励器.exe");
                    StartupOption.Write(key, true, examplePath);
                    check((string)key.GetValue(StartupOption.ValueName) == "\"" + examplePath + "\" --startup", "Startup commands quote Unicode and space-containing executable paths");
                    StartupOption.Write(key, false, examplePath); check(key.GetValue(StartupOption.ValueName) == null, "Disabling startup removes the app entry");
                }
            }
            finally { Microsoft.Win32.Registry.CurrentUser.DeleteSubKey(registryTest, false); }
            check(StartupOption.CurrentCommand == startupBefore, "Startup verification leaves real Windows startup settings untouched");
            PetDragMotion pickup = new PetDragMotion(); DateTime pickupTime = new DateTime(2026, 11, 3, 9, 0, 0);
            pickup.Update(false, pickupTime, 0); pickup.Update(true, pickupTime.AddMilliseconds(40), 10);
            check(pickup.Amount > 0 && pickup.Amount < 1, "Picking up eases into the drag pose instead of snapping");
            for (int tick = 2; tick <= 16; tick++) pickup.Update(true, pickupTime.AddMilliseconds(tick * 40), 100);
            check(pickup.Amount == 1 && pickup.Lean > 0 && pickup.Lean <= 10, "Held pose reaches full strength while movement lean stays bounded");
            double initialLean = pickup.Lean; pickup.Update(true, pickupTime.AddMilliseconds(680), -100);
            check(pickup.Lean < initialLean && pickup.Lean > -10, "Reversing drag direction changes lean gradually");
            pickup.Update(false, pickupTime.AddMilliseconds(720), 0);
            check(pickup.Amount > 0 && pickup.Amount < 1, "Releasing retains a brief settling transition");
            for (int tick = 19; tick <= 60; tick++) pickup.Update(false, pickupTime.AddMilliseconds(tick * 40), 0);
            check(pickup.Amount == 0 && pickup.Lean == 0, "Interrupted or released drags always return to idle");
            PetDragMotion frequent = new PetDragMotion(), sparse = new PetDragMotion();
            frequent.Update(false, pickupTime, 0); sparse.Update(false, pickupTime, 0);
            for (int tick = 1; tick <= 12; tick++) frequent.Update(true, pickupTime.AddMilliseconds(tick * 10), 0);
            for (int tick = 1; tick <= 3; tick++) sparse.Update(true, pickupTime.AddMilliseconds(tick * 40), 0);
            check(Math.Abs(frequent.Amount - sparse.Amount) < .001, "Animation transition is independent of mouse message frequency");
            for (int character = 0; character < PetArt.Names.Length; character++)
            using (Bitmap first = PetModel3D.Render(character, 0, 0, 0, 25, 12, false, false, true))
            using (Bitmap next = PetModel3D.Render(character, 0, 6, 0, 25, 12, false, false, true))
            {
                int changedFeet = 0;
                for (int y = 120; y < 190; y++) for (int x = 0; x < 200; x++) if (first.GetPixel(x, y) != next.GetPixel(x, y)) changedFeet++;
                check(changedFeet > 80, "Held animation moves feet and body rather than only a fixed face: " + character);
            }
            using (Bitmap sitting = PetModel3D.Render(3, 0, 6, 0, 25, 12, false, false, true))
            using (Bitmap working = PetModel3D.Render(3, 0, 6, 3, 25, 12, false, false, true))
            {
                bool same = true;
                for (int y = 0; y < 190; y++) for (int x = 0; x < 200; x++) if (sitting.GetPixel(x, y) != working.GetPixel(x, y)) same = false;
                check(same, "Temporary picked-up pose has no floating work desk and does not depend on saved activity");
            }
            check(PetArt.Names.Length == 6 && PetArt.Names[4] == "招财猫" && PetArt.Names[5] == "面包猫", "Bread cat appends a character without changing existing saved character IDs");
            Settings breadConfig = new Settings { MonthlySalary = 8800, PetCharacter = 5, PetEnabled = false, DailySummaryEnabled = false, PrivacyMode = true };
            Storage.Save(breadConfig);
            using (SettingsForm dialog = new SettingsForm(breadConfig))
            {
                dialog.Opacity = 0; dialog.Show(); Application.DoEvents();
                ComboBox choices = (ComboBox)dialog.Controls["PetCharacter"];
                check(choices.Items.Count == 6 && choices.SelectedIndex == 5 && choices.Text == "面包猫", "Bread cat appears and remains selected in the real settings form");
                ((Button)dialog.AcceptButton).PerformClick();
                check(Storage.Load().PetCharacter == 5 && Storage.Load().PrivacyMode && Storage.Load().MonthlySalary == 8800, "Saving bread cat preserves salary and privacy preferences");
            }
            using (Widget owner = new Widget(breadConfig))
            {
                owner.Opacity = 0; owner.Show(); Application.DoEvents(); ToolStripMenuItem characters = null;
                foreach (ToolStripItem item in owner.ContextMenuStrip.Items) if (item.Text == "切换桌宠形象") characters = item as ToolStripMenuItem;
                check(characters != null && characters.DropDownItems.Count == 6 && characters.DropDownItems[5].Text == "面包猫", "Bread cat is available from the existing character menu");
                characters.DropDownItems[5].PerformClick(); Application.DoEvents();
                check(Storage.Load().PetCharacter == 5 && Storage.Load().PetEnabled, "Selecting bread cat from the menu enables the new pet through the original flow"); owner.Hide();
            }
            using (Bitmap front = PetModel3D.Render(5, 0, 18, 0, 0, 12, false, false))
            using (Bitmap back = PetModel3D.Render(5, 0, 18, 0, 180, 12, false, false))
            {
                int difference = 0;
                for (int y = 0; y < 190; y++) for (int x = 0; x < 200; x++) if (front.GetPixel(x, y) != back.GetPixel(x, y)) difference++;
                check(difference > 1000, "Bread cat rotates as a real model with a distinct front and back");
            }
            Settings monthly = new Settings { MonthlySalary = 8800, ManualDays = true, Days = 22, ManualMonth = "2026-11", DailySummaryEnabled = false, PetEnabled = false };
            check(WorkClock.At(monthly, new DateTime(2026, 11, 1, 18, 0, 0)).MonthEarned == 0 && WorkClock.At(monthly, new DateTime(2026, 11, 2, 8, 0, 0)).MonthEarned == 0, "Month starts at zero, including a resting first day");
            check(WorkClock.At(monthly, new DateTime(2026, 11, 5, 14, 0, 0)).MonthEarned == 1400, "Monthly estimate includes prior workdays and today's partial progress without an execution history");
            check(WorkClock.At(monthly, new DateTime(2026, 11, 5, 12, 0, 0)).MonthEarned == WorkClock.At(monthly, new DateTime(2026, 11, 5, 13, 0, 0)).MonthEarned, "Monthly income pauses for lunch");
            check(WorkClock.At(monthly, new DateTime(2026, 11, 7, 14, 0, 0)).MonthEarned == 2000, "Rest days retain accumulated earnings without adding income");
            monthly.WorkMode = 2;
            check(WorkClock.At(monthly, new DateTime(2026, 11, 8, 18, 0, 0)).MonthEarned == 2400, "Monthly income follows the alternating-week Saturday anchor");
            monthly.WorkMode = 0; monthly.SingleRest = 3;
            check(WorkClock.At(monthly, new DateTime(2026, 11, 4, 18, 0, 0)).MonthEarned == 1200, "Monthly income follows a custom single-rest weekday");
            monthly.WorkMode = 1; monthly.ManualDays = false;
            check(WorkClock.At(monthly, new DateTime(2026, 11, 30, 18, 0, 0)).MonthEarned == 8800 && WorkClock.At(monthly, new DateTime(2028, 2, 29, 18, 0, 0)).MonthEarned == 8800, "Automatic monthly income reaches the salary at month-end, including leap February");
            check(WorkClock.At(monthly, new DateTime(2026, 12, 1, 8, 0, 0)).MonthEarned == 0 && WorkClock.At(monthly, new DateTime(2027, 1, 1, 8, 0, 0)).MonthEarned == 0, "Month and year changes start a fresh monthly estimate");
            monthly.ManualDays = true; monthly.Days = 1;
            Snapshot capped = WorkClock.At(monthly, new DateTime(2026, 11, 5, 14, 0, 0));
            check(capped.MonthEarned == 8800 && capped.Earned == 4400, "Monthly cap does not change existing daily income calculations");
            monthly.Days = 31;
            check(WorkClock.At(monthly, new DateTime(2026, 11, 30, 18, 0, 0)).MonthEarned < 8800, "A larger manual day divisor is respected rather than forcing a full monthly salary");
            monthly.MonthlySalary = 0; check(WorkClock.At(monthly, new DateTime(2026, 11, 5, 18, 0, 0)).MonthEarned == 0, "Unconfigured salary produces zero monthly income");
            string oldOrder = "countdown,progress,rate,income,encouragement";
            File.WriteAllText(Storage.PathName, "<Settings><MonthlySalary>8800</MonthlySalary><DetailOrder>" + oldOrder + "</DetailOrder><DetailVisible>rate,progress</DetailVisible><HoverVisible>countdown</HoverVisible></Settings>");
            Settings migrated = Storage.Load();
            check(migrated.DetailOrder == oldOrder + ",month,overtime,absence" && migrated.DetailVisible == "rate,progress" && migrated.HoverVisible == "countdown", "Legacy component migration preserves custom order, hidden fields and hover selection");
            Storage.Save(migrated); check(Storage.Load().DetailOrder == migrated.DetailOrder, "Monthly component migration is idempotent after saving");
            monthly = new Settings { MonthlySalary = 8800, PetEnabled = false, DailySummaryEnabled = false, PrivacyMode = true, HoverVisible = "month,progress" };
            Storage.Save(monthly);
            check(Widget.BuildHoverMessage(monthly, new DateTime(2026, 11, 5, 14, 0, 0)).Contains("本月累计：¥ ****"), "Monthly hover amounts obey privacy mode");
            using (Widget owner = new Widget(monthly))
            {
                owner.Opacity = 0; owner.Show(); Application.DoEvents(); Panel body = (Panel)owner.Controls["DetailBody"];
                check(body.Controls["Detail_month"] is DetailCard, "Monthly income is an independent detail component");
                owner.MoveComponent("month", "income"); check(Storage.Load().DetailOrder.StartsWith("month,"), "Monthly component can move to the front and persist its order");
                owner.SetComponentVisible("month", false); check(body.Controls["Detail_month"] == null, "Monthly component can be hidden without removing daily income");
                owner.SetComponentVisible("month", true); owner.TogglePrivacy();
                check(!Storage.Load().PrivacyMode && Widget.BuildHoverMessage(Storage.Load(), new DateTime(2026, 11, 5, 14, 0, 0)).Contains("本月累计：¥ ") && !Widget.BuildHoverMessage(Storage.Load(), DateTime.Now).Contains("****"), "Eye button reveals monthly hover income along with other amounts"); owner.Hide();
            }
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
            OvertimeTests.Run(check); AbsenceTests.Run(check); PetInputTests.Run(check);
            report.AppendLine("All " + checks + " checks passed.");
        }
        catch (Exception ex) { report.AppendLine(ex.ToString()); Environment.ExitCode = 1; }
        File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "test-report.txt"), report.ToString(), Encoding.UTF8);
    }
}
