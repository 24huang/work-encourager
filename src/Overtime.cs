using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Windows.Forms;
using System.Xml.Serialization;

public class OvertimeEntry
{
    public DateTime Start;
    public int Minutes;
    public decimal HourlyRate;
    public bool Finished;
    public DateTime StoppedAt;
    [XmlIgnore] public DateTime End { get { return Finished ? StoppedAt : Start.AddMinutes(Minutes); } }
    public OvertimeEntry Copy() { return (OvertimeEntry)MemberwiseClone(); }
}
public class LeaveEntry
{
    public DateTime Date;
    public int Minutes;
    public LeaveEntry Copy() { return (LeaveEntry)MemberwiseClone(); }
}
public sealed class OvertimeSnapshot
{
    public double ElapsedSeconds;
    public int TodayMinutes, BalanceMinutes;
    public decimal TodayValue, BalanceValue;
    public bool Active, Deficit, Planned;
    public DateTime LastEnd;
}
public static class OvertimeClock
{
    public static OvertimeSnapshot At(Settings settings, DateTime now)
    {
        OvertimeSnapshot result = new OvertimeSnapshot();
        int used = 0;
        foreach (LeaveEntry entry in settings.LeaveEntries) if (entry.Date.Date <= now.Date) used += entry.Minutes;
        List<OvertimeEntry> ordered = new List<OvertimeEntry>(settings.OvertimeEntries);
        ordered.Sort(delegate(OvertimeEntry a, OvertimeEntry b) { return a.Start.CompareTo(b.Start); });
        foreach (OvertimeEntry entry in ordered)
        {
            double seconds = Math.Max(0, Math.Min((entry.End - entry.Start).TotalSeconds, (now - entry.Start).TotalSeconds));
            int credited = (int)(seconds / 1800) * 30;
            if (entry.Start.Date == now.Date)
            {
                result.Planned = true; result.ElapsedSeconds += seconds;
                result.TodayMinutes += credited; result.TodayValue += entry.HourlyRate * credited / 60m;
                result.Active |= now >= entry.Start && now < entry.End;
                if (entry.End > result.LastEnd) result.LastEnd = entry.End;
            }
            // 已用调休按开始时间先后冲抵，保留每笔登记时的时薪，避免历史折算值随月薪变化。
            int consumed = Math.Min(used, credited); used -= consumed;
            result.BalanceMinutes += credited - consumed;
            result.BalanceValue += entry.HourlyRate * (credited - consumed) / 60m;
        }
        result.Deficit = used > 0; return result;
    }
    public static void Validate(Settings settings, DateTime now)
    {
        if (settings.OvertimeEntries == null || settings.LeaveEntries == null || settings.OvertimePreset < 30 || settings.OvertimePreset > 1410 || settings.OvertimePreset % 30 != 0) throw new Exception("加班预设或记录无效。");
        List<OvertimeEntry> ordered = new List<OvertimeEntry>(settings.OvertimeEntries);
        foreach (OvertimeEntry entry in ordered)
        {
            if (entry == null || entry.Start.Second != 0 || entry.Start.Millisecond != 0 || entry.Minutes < 30 || entry.Minutes > 1410 || entry.Minutes % 30 != 0 || entry.HourlyRate < 0 || entry.Start.Date != entry.Start.AddMinutes(entry.Minutes).Date || entry.End < entry.Start || entry.End > entry.Start.AddMinutes(entry.Minutes)) throw new Exception("加班预设须以30分钟为单位，且不能跨午夜；实际结束须在预设时段内。");
            if (entry.Start.Date == now.Date && (!WorkClock.IsWorkingDay(settings, now) || entry.Start.TimeOfDay < TimeSpan.Parse(settings.End))) throw new Exception("今日加班须在工作日正常下班之后，请检查作息和加班时间。");
        }
        ordered.Sort(delegate(OvertimeEntry a, OvertimeEntry b) { return a.Start.CompareTo(b.Start); });
        for (int i = 1; i < ordered.Count; i++) if (ordered[i].Start < ordered[i - 1].End) throw new Exception("加班时段不能重叠。");
        foreach (LeaveEntry entry in settings.LeaveEntries)
        {
            if (entry == null || entry.Date.Date != entry.Date || entry.Date > now.Date || entry.Minutes <= 0 || entry.Minutes % 30 != 0) throw new Exception("已用调休须填写已发生日期和整半小时。");
            DateTime checkpoint = entry.Date.Date == now.Date ? now : entry.Date.Date.AddDays(1).AddTicks(-1);
            if (At(settings, checkpoint).Deficit) throw new Exception("调休余额不足，或所填请假日期早于对应加班。");
        }
        if (At(settings, now).Deficit) throw new Exception("调休余额不足，无法减少已抵扣的加班记录。");
    }
    public static string Text(Settings settings, OvertimeSnapshot value)
    {
        return "今日加班 " + (value.TodayMinutes / 60m).ToString("0.#") + " 小时" + (settings.PrivacyMode ? "" : "，折算 ¥ " + value.TodayValue.ToString("N2")) + "\n可调休 " + (value.BalanceMinutes / 60m).ToString("0.#") + " 小时" + (settings.PrivacyMode ? "（金额已隐藏）" : "，对应 ¥ " + value.BalanceValue.ToString("N2"));
    }
}

public sealed class OvertimeForm : Form
{
    private readonly Settings draft;
    private readonly ListBox overtimeList, leaveList;
    private readonly DateTimePicker start, leaveDate;
    private readonly NumericUpDown duration, leaveMinutes;
    private readonly Label balance, finish;
    public Settings Result;
    public OvertimeForm(Settings source, Func<Settings> getCurrent = null)
    {
        draft = source.Copy(); Text = "加班与调休 · 上班鼓励器";
        Font = new Font("Microsoft YaHei UI", 9); StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog; MaximizeBox = false; MinimizeBox = false;
        ClientSize = new Size(600, Math.Min(700, Screen.FromPoint(Cursor.Position).WorkingArea.Height - 100)); AutoScroll = true;
        Controls.Add(new Label { Text = DateTime.Today.ToString("yyyy年M月d日") + " · 今日加班", Bounds = new Rectangle(20, 15, 560, 28), Font = new Font(Font, FontStyle.Bold) });
        Controls.Add(new Label { Text = "开始时间", Bounds = new Rectangle(20, 53, 90, 26) });
        start = new DateTimePicker { Name = "OvertimeStart", Format = DateTimePickerFormat.Custom, CustomFormat = "HH:mm", ShowUpDown = true, Bounds = new Rectangle(115, 50, 110, 28), Value = DateTime.Today.Add(TimeSpan.Parse(source.End)) }; Controls.Add(start);
        Controls.Add(new Label { Text = "预设时长 / 分钟", Bounds = new Rectangle(250, 53, 135, 26) });
        duration = new NumericUpDown { Name = "OvertimeDuration", Minimum = 30, Maximum = 1410, Increment = 30, Value = source.OvertimePreset, Bounds = new Rectangle(400, 50, 120, 28) }; Controls.Add(duration);
        finish = new Label { Bounds = new Rectangle(20, 90, 560, 25) }; Controls.Add(finish);
        duration.ValueChanged += delegate { finish.Text = "预计结束 " + DateTime.Today.Add(start.Value.TimeOfDay).AddMinutes((double)duration.Value).ToString("MM-dd HH:mm") + "；不足整半小时不计入"; };
        start.ValueChanged += delegate { finish.Text = "预计结束 " + DateTime.Today.Add(start.Value.TimeOfDay).AddMinutes((double)duration.Value).ToString("MM-dd HH:mm") + "；不足整半小时不计入"; };
        finish.Text = "开始默认正常下班时间，可调整；时长按整半小时填写";
        Button add = new Button { Name = "AddOvertime", Text = "添加今日加班", Bounds = new Rectangle(20, 124, 170, 32) }; Controls.Add(add);
        Button remove = new Button { Text = "删除选中加班", Bounds = new Rectangle(205, 124, 170, 32) }; Controls.Add(remove);
        Button stop = new Button { Name = "StopOvertime", Text = "现在结束选中段", Bounds = new Rectangle(390, 124, 190, 32) }; Controls.Add(stop);
        overtimeList = new ListBox { Name = "OvertimeEntries", Bounds = new Rectangle(20, 166, 560, 120), HorizontalScrollbar = true }; Controls.Add(overtimeList);
        Controls.Add(new Label { Text = "已用调休（只冲抵加班余额，不改变正常工资估算）", Bounds = new Rectangle(20, 300, 560, 27) });
        leaveDate = new DateTimePicker { Name = "LeaveDate", Format = DateTimePickerFormat.Short, MaxDate = DateTime.Today, Value = DateTime.Today, Bounds = new Rectangle(20, 334, 150, 28) }; Controls.Add(leaveDate);
        leaveMinutes = new NumericUpDown { Name = "LeaveMinutes", Minimum = 30, Maximum = 10000000, Increment = 30, Value = 30, Bounds = new Rectangle(185, 334, 100, 28) }; Controls.Add(leaveMinutes);
        Controls.Add(new Label { Text = "分钟", Bounds = new Rectangle(290, 337, 50, 24) });
        Button use = new Button { Name = "UseLeave", Text = "登记已用调休", Bounds = new Rectangle(345, 330, 140, 32) }; Controls.Add(use);
        Button undo = new Button { Text = "删除选中", Bounds = new Rectangle(490, 330, 90, 32) }; Controls.Add(undo);
        leaveList = new ListBox { Name = "LeaveEntries", Bounds = new Rectangle(20, 375, 560, 90) }; Controls.Add(leaveList);
        balance = new Label { Name = "OvertimeBalance", Bounds = new Rectangle(20, 478, 560, 54) }; Controls.Add(balance);
        Controls.Add(new Label { Text = "按填写的时段估算，关闭程序也计入。金额按登记时正常时薪折算。\n较早的加班优先抵扣；加班结束后发总结，健康提醒继续生效。", Bounds = new Rectangle(20, 540, 560, 46) });
        Button save = new Button { Name = "SaveOvertime", Text = "保存并应用", Bounds = new Rectangle(395, 610, 185, 36) }; Controls.Add(save);
        Button cancel = new Button { Text = "取消", DialogResult = DialogResult.Cancel, Bounds = new Rectangle(280, 610, 100, 36) }; Controls.Add(cancel); AcceptButton = save; CancelButton = cancel;
        add.Click += delegate
        {
            try
            {
                DateTime from = DateTime.Today.Add(start.Value.TimeOfDay); from = from.AddTicks(-(from.Ticks % TimeSpan.TicksPerMinute));
                if (!WorkClock.IsWorkingDay(draft, DateTime.Today) || from.TimeOfDay < TimeSpan.Parse(draft.End)) throw new Exception("本版只支持工作日正常下班后的加班。");
                if (draft.MonthlySalary <= 0) throw new Exception("请先设置月薪，才能折算正常时薪。");
                Snapshot normal = WorkClock.At(draft, DateTime.Today);
                Settings next = draft.Copy(); next.OvertimeEntries.Add(new OvertimeEntry { Start = from, Minutes = (int)duration.Value, HourlyRate = normal.Daily / ((decimal)normal.Total / 3600m) });
                OvertimeClock.Validate(next, DateTime.Now); draft.OvertimeEntries = next.OvertimeEntries; RefreshRecords(); overtimeList.SelectedIndex = draft.OvertimeEntries.Count - 1;
            }
            catch (Exception ex) { MessageBox.Show(this, ex.Message, "加班未添加"); }
        };
        remove.Click += delegate
        {
            if (overtimeList.SelectedIndex < 0) return;
            try { Settings next = draft.Copy(); next.OvertimeEntries.RemoveAt(overtimeList.SelectedIndex); OvertimeClock.Validate(next, DateTime.Now); draft.OvertimeEntries = next.OvertimeEntries; RefreshRecords(); }
            catch (Exception ex) { MessageBox.Show(this, ex.Message, "记录未删除"); }
        };
        stop.Click += delegate
        {
            if (overtimeList.SelectedIndex < 0) return;
            try
            {
                Settings next = draft.Copy(); OvertimeEntry entry = next.OvertimeEntries[overtimeList.SelectedIndex]; DateTime now = DateTime.Now;
                if (entry.Start.Date != now.Date || now < entry.Start || now >= entry.End) throw new Exception("只能提前结束今天正在进行的加班。");
                entry.Finished = true; entry.StoppedAt = now.AddTicks(-(now.Ticks % TimeSpan.TicksPerMinute));
                OvertimeClock.Validate(next, now); draft.OvertimeEntries = next.OvertimeEntries; RefreshRecords();
            }
            catch (Exception ex) { MessageBox.Show(this, ex.Message, "加班未结束"); }
        };
        use.Click += delegate
        {
            try { Settings next = draft.Copy(); next.LeaveEntries.Add(new LeaveEntry { Date = leaveDate.Value.Date, Minutes = (int)leaveMinutes.Value }); OvertimeClock.Validate(next, DateTime.Now); draft.LeaveEntries = next.LeaveEntries; RefreshRecords(); }
            catch (Exception ex) { MessageBox.Show(this, ex.Message, "调休未登记"); }
        };
        undo.Click += delegate { if (leaveList.SelectedIndex >= 0) { draft.LeaveEntries.RemoveAt(leaveList.SelectedIndex); RefreshRecords(); } };
        save.Click += delegate
        {
            try
            {
                draft.OvertimePreset = (int)duration.Value;
                // 保留弹窗计时期间更新的总结状态与快捷键切换的隐私状态。
                Settings next = (getCurrent == null ? source : getCurrent()).Copy();
                next.OvertimeEntries = draft.OvertimeEntries; next.LeaveEntries = draft.LeaveEntries; next.OvertimePreset = draft.OvertimePreset;
                if (!next.DetailVisible.Contains("overtime")) next.DetailVisible = next.DetailVisible.Length == 0 ? "overtime" : next.DetailVisible + ",overtime";
                Storage.Save(next); Result = next; DialogResult = DialogResult.OK; Close();
            }
            catch (Exception ex) { MessageBox.Show(this, ex.Message, "加班设置未保存"); }
        };
        BackColor = source.Theme == 0 ? Color.FromArgb(18, 23, 43) : Color.FromArgb(252, 241, 249); ForeColor = source.Theme == 0 ? Color.FromArgb(223, 232, 250) : Color.FromArgb(86, 55, 91);
        foreach (Control control in Controls) if (control is Button || control is ListBox || control is NumericUpDown) { control.BackColor = source.Theme == 0 ? Color.FromArgb(32, 42, 67) : Color.FromArgb(255, 248, 254); control.ForeColor = ForeColor; }
        RefreshRecords();
    }
    public void ApplyPrivacy(bool enabled) { draft.PrivacyMode = enabled; RefreshRecords(); }
    private void RefreshRecords()
    {
        overtimeList.Items.Clear(); leaveList.Items.Clear();
        foreach (OvertimeEntry entry in draft.OvertimeEntries) overtimeList.Items.Add(entry.Start.ToString("yyyy-MM-dd HH:mm") + "—" + entry.End.ToString("HH:mm") + (entry.Finished ? " 已结束" : " 预设" + (entry.Minutes / 60m).ToString("0.#") + "小时") + (draft.PrivacyMode ? " · 金额隐藏" : " · 时薪 ¥" + entry.HourlyRate.ToString("N2")));
        foreach (LeaveEntry entry in draft.LeaveEntries) leaveList.Items.Add(entry.Date.ToString("yyyy-MM-dd") + " 已用 " + (entry.Minutes / 60m).ToString("0.#") + " 小时");
        balance.Text = OvertimeClock.Text(draft, OvertimeClock.At(draft, DateTime.Now));
    }
}
