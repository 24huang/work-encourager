using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Windows.Forms;

public class AbsenceEntry
{
    public DateTime From, Until;
    public int Kind;
    public decimal PayPercent;
    public bool UseTimeOff = true;
    public AbsenceEntry Copy() { return (AbsenceEntry)MemberwiseClone(); }
}
public sealed class AbsenceSnapshot
{
    public double TodaySeconds, TodayElapsed;
    public decimal TodayLoss, MonthLoss, ActivePayPercent = 100;
    public bool Active, Deficit;
    public int ActiveKind, UsedToday, BalanceMinutes;
    public decimal BalanceValue;
}
public static class AbsenceClock
{
    public static readonly string[] Names = { "事假", "病假", "年假" };
    private sealed class Piece { public DateTime From, Until; }
    private sealed class Block
    {
        public AbsenceEntry Entry; public List<Piece> Pieces = new List<Piece>();
        public double Seconds; public bool Covered;
    }
    private sealed class Event
    {
        public DateTime When; public int Type, Minutes; public decimal Rate; public Block Block;
    }
    // 只生成作息中的有效区间，午休、夜间和休息日不会产生请假扣款或调休消耗。
    private static List<Block> Blocks(Settings s, AbsenceEntry entry)
    {
        List<Block> result = new List<Block>();
        for (DateTime day = entry.From.Date; day <= entry.Until.Date; day = day.AddDays(1))
        {
            if (!WorkClock.IsWorkingDay(s, day)) continue;
            DateTime begin = day.Add(TimeSpan.Parse(s.Start)), end = day.Add(TimeSpan.Parse(s.End));
            DateTime[] edges = s.HasBreak ? new[] { begin, day.Add(TimeSpan.Parse(s.BreakStart)), day.Add(TimeSpan.Parse(s.BreakEnd)), end } : new[] { begin, end };
            Block block = new Block { Entry = entry };
            for (int interval = 0; interval < edges.Length; interval += 2)
            {
                DateTime cursor = entry.From > edges[interval] ? entry.From : edges[interval];
                DateTime until = entry.Until < edges[interval + 1] ? entry.Until : edges[interval + 1];
                while (cursor < until)
                {
                    double seconds = Math.Min(1800 - block.Seconds, (until - cursor).TotalSeconds);
                    DateTime next = cursor.AddSeconds(seconds); block.Pieces.Add(new Piece { From = cursor, Until = next }); block.Seconds += seconds; cursor = next;
                    if (block.Seconds >= 1800) { result.Add(block); block = new Block { Entry = entry }; }
                }
            }
            if (block.Seconds > 0) result.Add(block);
        }
        return result;
    }
    public static AbsenceSnapshot At(Settings s, DateTime now)
    {
        AbsenceSnapshot result = new AbsenceSnapshot(); List<Event> events = new List<Event>(); List<Block> blocks = new List<Block>();
        foreach (OvertimeEntry record in s.OvertimeEntries)
            for (DateTime earned = record.Start.AddMinutes(30); earned <= record.End && earned <= now; earned = earned.AddMinutes(30)) events.Add(new Event { When = earned, Type = 0, Minutes = 30, Rate = record.HourlyRate });
        foreach (LeaveEntry record in s.LeaveEntries)
        {
            DateTime when = record.RegisteredAt == DateTime.MinValue ? record.Date.Date : record.RegisteredAt.Date == record.Date.Date ? record.RegisteredAt : record.Date.Date.AddDays(1).AddTicks(-1);
            if (when <= now) events.Add(new Event { When = when, Type = 1, Minutes = record.Minutes });
        }
        foreach (AbsenceEntry entry in s.Absences)
        {
            List<Block> entryBlocks = Blocks(s, entry); blocks.AddRange(entryBlocks);
            foreach (Block block in entryBlocks) if (entry.UseTimeOff && block.Seconds == 1800 && block.Pieces[0].From <= now)
                events.Add(new Event { When = block.Pieces[0].From, Type = 2, Minutes = 30, Block = block });
        }
        // 按发生时间统一结算；未来加班只能增加未来余额，不能补付过去未被调休覆盖的请假。
        events.Sort(delegate(Event a, Event b) { int order = a.When.CompareTo(b.When); return order == 0 ? a.Type.CompareTo(b.Type) : order; });
        Queue<Event> credits = new Queue<Event>(); int available = 0, debt = 0;
        foreach (Event item in events)
        {
            if (item.Type == 0)
            {
                int repayment = Math.Min(debt, item.Minutes); debt -= repayment; item.Minutes -= repayment;
                if (item.Minutes > 0) { credits.Enqueue(item); available += item.Minutes; }
            }
            else
            {
                if (item.Type == 2 && available < 30) continue;
                if (item.Type == 2) { item.Block.Covered = true; if (item.When.Date == now.Date) result.UsedToday += 30; }
                int needed = item.Minutes;
                while (needed > 0 && credits.Count > 0)
                {
                    Event credit = credits.Peek(); int consumed = Math.Min(needed, credit.Minutes);
                    credit.Minutes -= consumed; needed -= consumed; available -= consumed;
                    if (credit.Minutes == 0) credits.Dequeue();
                }
                if (item.Type == 1) debt += needed;
            }
        }
        result.Deficit = debt > 0; result.BalanceMinutes = available;
        foreach (Event credit in credits) result.BalanceValue += credit.Rate * credit.Minutes / 60m;
        double dailySeconds = (TimeSpan.Parse(s.End) - TimeSpan.Parse(s.Start)).TotalSeconds - (s.HasBreak ? (TimeSpan.Parse(s.BreakEnd) - TimeSpan.Parse(s.BreakStart)).TotalSeconds : 0);
        foreach (Block block in blocks) foreach (Piece piece in block.Pieces)
        {
            decimal paid = block.Covered ? 100m : block.Entry.PayPercent;
            double elapsed = Math.Max(0, Math.Min((piece.Until - piece.From).TotalSeconds, (now - piece.From).TotalSeconds));
            if (piece.From.Date == now.Date)
            {
                result.TodaySeconds += (piece.Until - piece.From).TotalSeconds; result.TodayElapsed += elapsed;
                if (now >= piece.From && now < piece.Until) { result.Active = true; result.ActiveKind = block.Entry.Kind; result.ActivePayPercent = paid; }
            }
            if (elapsed == 0 || piece.From.Year != now.Year || piece.From.Month != now.Month) continue;
            int days = s.ManualDays && s.ManualMonth == piece.From.ToString("yyyy-MM") ? s.Days : WorkClock.CountDays(s, piece.From);
            decimal loss = (s.MonthlySalary / days) * (decimal)elapsed / (decimal)dailySeconds * (100 - paid) / 100m;
            result.MonthLoss += loss; if (piece.From.Date == now.Date) result.TodayLoss += loss;
        }
        return result;
    }
    public static void Validate(Settings s)
    {
        if (s.Absences == null || s.PersonalLeavePercent < 0 || s.PersonalLeavePercent > 100 || s.AnnualLeavePercent < 0 || s.AnnualLeavePercent > 100 || (s.SickLeavePercent != -1 && s.SickLeavePercent < 0) || s.SickLeavePercent > 100) throw new Exception("请假计薪比例须为0%至100%，病假可暂不设置。");
        List<AbsenceEntry> ordered = new List<AbsenceEntry>(s.Absences);
        foreach (AbsenceEntry entry in ordered)
        {
            if (entry == null || entry.Until <= entry.From || (entry.Until - entry.From).TotalDays > 366 || entry.Kind < 0 || entry.Kind > 2 || entry.PayPercent < 0 || entry.PayPercent > 100 || entry.From.Ticks % TimeSpan.TicksPerMinute != 0 || entry.Until.Ticks % TimeSpan.TicksPerMinute != 0) throw new Exception("请假日期、类型或计薪比例无效，单条最长366天。");
            bool wholeDays = entry.From.TimeOfDay == TimeSpan.Parse(s.Start) && entry.Until.TimeOfDay == TimeSpan.Parse(s.End);
            if (!wholeDays && (entry.Until - entry.From).Ticks % (30 * TimeSpan.TicksPerMinute) != 0) throw new Exception("请假按整半小时登记，整天请假按当前作息。");
            if (Blocks(s, entry).Count == 0) throw new Exception("该时段没有有效工作时间，请检查休息日和作息。");
        }
        ordered.Sort(delegate(AbsenceEntry a, AbsenceEntry b) { return a.From.CompareTo(b.From); });
        for (int i = 1; i < ordered.Count; i++) if (ordered[i].From < ordered[i - 1].Until) throw new Exception("请假记录不能重叠，请先删除或调整原记录。");
    }
}

public sealed class AbsenceForm : Form
{
    private readonly Settings draft;
    private readonly ListBox records;
    private readonly Label info;
    public Settings Result;
    public AbsenceForm(Settings source, Func<Settings> getCurrent = null)
    {
        draft = source.Copy(); Text = "请假管理 · 上班鼓励器"; Font = new Font("Microsoft YaHei UI", 9);
        StartPosition = FormStartPosition.CenterParent; FormBorderStyle = FormBorderStyle.FixedDialog; MaximizeBox = false; MinimizeBox = false;
        ClientSize = new Size(620, Math.Min(640, Screen.FromPoint(Cursor.Position).WorkingArea.Height - 100)); AutoScroll = true;
        Controls.Add(new Label { Text = "事假 / 病假 / 年假", Bounds = new Rectangle(20, 18, 560, 28), Font = new Font(Font, FontStyle.Bold) });
        ComboBox kind = new ComboBox { Name = "AbsenceKind", DropDownStyle = ComboBoxStyle.DropDownList, Bounds = new Rectangle(20, 56, 130, 28) }; kind.Items.AddRange(AbsenceClock.Names); Controls.Add(kind);
        Controls.Add(new Label { Text = "计薪比例 %", Bounds = new Rectangle(170, 60, 100, 25) });
        TextBox percent = new TextBox { Name = "AbsencePercent", Bounds = new Rectangle(275, 56, 90, 28) }; Controls.Add(percent);
        CheckBox bank = new CheckBox { Name = "UseTimeOffFirst", Text = "优先使用调休（1:1）", Checked = true, Bounds = new Rectangle(390, 56, 215, 28) }; Controls.Add(bank);
        kind.SelectedIndexChanged += delegate { decimal value = kind.SelectedIndex == 0 ? draft.PersonalLeavePercent : kind.SelectedIndex == 1 ? draft.SickLeavePercent : draft.AnnualLeavePercent; percent.Text = value < 0 ? "" : value.ToString(CultureInfo.CurrentCulture); }; kind.SelectedIndex = 0;
        Controls.Add(new Label { Text = "开始", Bounds = new Rectangle(20, 108, 65, 26) });
        DateTimePicker from = new DateTimePicker { Name = "AbsenceFrom", Format = DateTimePickerFormat.Custom, CustomFormat = "yyyy-MM-dd HH:mm", Value = DateTime.Today.Add(TimeSpan.Parse(source.Start)), Bounds = new Rectangle(90, 104, 215, 28) }; Controls.Add(from);
        Controls.Add(new Label { Text = "结束", Bounds = new Rectangle(320, 108, 65, 26) });
        DateTimePicker until = new DateTimePicker { Name = "AbsenceUntil", Format = DateTimePickerFormat.Custom, CustomFormat = "yyyy-MM-dd HH:mm", Value = DateTime.Today.Add(TimeSpan.Parse(source.End)), Bounds = new Rectangle(385, 104, 215, 28) }; Controls.Add(until);
        Button whole = new Button { Text = "按所选日期整天", Bounds = new Rectangle(20, 151, 175, 32) }; Controls.Add(whole);
        whole.Click += delegate { from.Value = from.Value.Date.Add(TimeSpan.Parse(draft.Start)); until.Value = until.Value.Date.Add(TimeSpan.Parse(draft.End)); };
        Button add = new Button { Name = "AddAbsence", Text = "添加请假", Bounds = new Rectangle(215, 151, 170, 32) }; Controls.Add(add);
        Button remove = new Button { Name = "RemoveAbsence", Text = "删除选中记录", Bounds = new Rectangle(405, 151, 195, 32) }; Controls.Add(remove);
        records = new ListBox { Name = "AbsenceEntries", Bounds = new Rectangle(20, 200, 580, 185), HorizontalScrollbar = true }; Controls.Add(records);
        info = new Label { Name = "AbsenceInfo", Bounds = new Rectangle(20, 402, 580, 52) }; Controls.Add(info);
        Controls.Add(new Label { Text = "可提前安排或补录，扣除午休和休息日。病假比例未设时需填写。\n调休不预留，每个有效半小时开始时使用当时余额；不足部分按假种计薪。\n有效工时尾数不足半小时不扣调休；修改历史记录会重新计算。\n此处自动扣除的调休，请勿再到加班窗口重复登记。", Bounds = new Rectangle(20, 463, 580, 90) });
        Button save = new Button { Name = "SaveAbsence", Text = "保存并应用", Bounds = new Rectangle(410, 570, 190, 36) }; Controls.Add(save);
        Button cancel = new Button { Text = "取消", DialogResult = DialogResult.Cancel, Bounds = new Rectangle(290, 570, 100, 36) }; Controls.Add(cancel); AcceptButton = save; CancelButton = cancel;
        add.Click += delegate
        {
            try
            {
                decimal ratio; if (!decimal.TryParse(percent.Text, out ratio) || ratio < 0 || ratio > 100) throw new Exception("请明确填写0%至100%的计薪比例。");
                Settings next = draft.Copy(); next.Absences.Add(new AbsenceEntry { From = from.Value.AddTicks(-(from.Value.Ticks % TimeSpan.TicksPerMinute)), Until = until.Value.AddTicks(-(until.Value.Ticks % TimeSpan.TicksPerMinute)), Kind = kind.SelectedIndex, PayPercent = ratio, UseTimeOff = bank.Checked });
                next.Validate(); draft.Absences = next.Absences;
                if (kind.SelectedIndex == 0) draft.PersonalLeavePercent = ratio; else if (kind.SelectedIndex == 1) draft.SickLeavePercent = ratio; else draft.AnnualLeavePercent = ratio;
                RefreshRecords();
            }
            catch (Exception ex) { MessageBox.Show(this, ex.Message, "请假未添加"); }
        };
        remove.Click += delegate { if (records.SelectedIndex >= 0) { draft.Absences.RemoveAt(records.SelectedIndex); RefreshRecords(); } };
        save.Click += delegate
        {
            try
            {
                Settings next = (getCurrent == null ? source : getCurrent()).Copy(); next.Absences = draft.Absences; next.PersonalLeavePercent = draft.PersonalLeavePercent; next.SickLeavePercent = draft.SickLeavePercent; next.AnnualLeavePercent = draft.AnnualLeavePercent;
                if (!next.DetailVisible.Contains("absence")) next.DetailVisible = next.DetailVisible.Length == 0 ? "absence" : next.DetailVisible + ",absence";
                Storage.Save(next); Result = next; DialogResult = DialogResult.OK; Close();
            }
            catch (Exception ex) { MessageBox.Show(this, ex.Message, "请假未保存"); }
        };
        BackColor = source.Theme == 0 ? Color.FromArgb(18, 23, 43) : Color.FromArgb(252, 241, 249); ForeColor = source.Theme == 0 ? Color.FromArgb(223, 232, 250) : Color.FromArgb(86, 55, 91);
        foreach (Control control in Controls) if (control is Button || control is ListBox || control is TextBox) { control.BackColor = source.Theme == 0 ? Color.FromArgb(32, 42, 67) : Color.FromArgb(255, 248, 254); control.ForeColor = ForeColor; }
        // 原生日历同为浅底，假种下拉框明确使用深字，避免继承深色主题的浅字。
        kind.BackColor = Color.White; kind.ForeColor = Color.FromArgb(45, 45, 55);
        RefreshRecords();
    }
    public void ApplyPrivacy(bool enabled) { draft.PrivacyMode = enabled; RefreshRecords(); }
    private void RefreshRecords()
    {
        records.Items.Clear(); foreach (AbsenceEntry entry in draft.Absences) records.Items.Add(AbsenceClock.Names[entry.Kind] + " " + entry.From.ToString("yyyy-MM-dd HH:mm") + "—" + entry.Until.ToString("yyyy-MM-dd HH:mm") + " " + entry.PayPercent + "%" + (entry.UseTimeOff ? " 优先调休" : " 不用调休"));
        AbsenceSnapshot status = AbsenceClock.At(draft, DateTime.Now);
        info.Text = "今日计划请假 " + (status.TodaySeconds / 3600).ToString("0.##") + " 小时；已自动抵扣调休 " + (status.UsedToday / 60m).ToString("0.#") + " 小时\n" + (draft.PrivacyMode ? "请假扣款已隐藏" : "今日截至当前少计 ¥ " + status.TodayLoss.ToString("N2")) + "（未来请假不会提前扣工资或占用调休）";
    }
}
