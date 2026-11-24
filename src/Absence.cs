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
    public double TodaySeconds, TodayElapsed, MonthElapsedSeconds, MonthUncoveredSeconds;
    public decimal TodayLoss, MonthLoss, ActivePayPercent = 100;
    public bool Active, Deficit;
    public int ActiveKind, UsedToday, BalanceMinutes, MonthRetroMinutes, MonthUsedMinutes;
    public decimal BalanceValue, TodayOvertimeValue;
}
// 明细账按同一结算时点汇集每日结果，不另行计算调休，以免漏掉之后的补抵。
public sealed class AbsenceDay
{
    public double LeaveSeconds, UncoveredSeconds;
    public int AutoMinutes, RetroMinutes, ManualMinutes;
    public decimal Loss, OvertimeValue;
}
public static class AbsenceClock
{
    public static readonly string[] Names = { "事假", "病假", "年假" };
    private sealed class Piece { public DateTime From, Until; }
    private sealed class Block
    {
        public AbsenceEntry Entry; public List<Piece> Pieces = new List<Piece>();
        public double Seconds; public bool Covered, Retro;
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
        return At(s, now, null);
    }
    public static AbsenceSnapshot At(Settings s, DateTime now, Dictionary<DateTime, AbsenceDay> daily)
    {
        AbsenceSnapshot result = new AbsenceSnapshot(); List<Event> events = new List<Event>(); List<Block> blocks = new List<Block>();
        foreach (OvertimeEntry record in s.OvertimeEntries)
            for (DateTime earned = record.Start.AddMinutes(30); earned <= record.End && earned <= now; earned = earned.AddMinutes(30)) events.Add(new Event { When = earned, Type = 0, Minutes = 30, Rate = record.HourlyRate });
        foreach (LeaveEntry record in s.LeaveEntries)
        {
            DateTime when = record.RegisteredAt == DateTime.MinValue ? record.Date.Date : record.RegisteredAt.Date == record.Date.Date ? record.RegisteredAt : record.Date.Date.AddDays(1).AddTicks(-1);
            if (when <= now)
            {
                events.Add(new Event { When = when, Type = 1, Minutes = record.Minutes });
                if (record.Date.Year == now.Year && record.Date.Month == now.Month) result.MonthUsedMinutes += record.Minutes;
                AbsenceDay day; if (daily != null && daily.TryGetValue(record.Date.Date, out day)) day.ManualMinutes += record.Minutes;
            }
        }
        foreach (AbsenceEntry entry in s.Absences)
        {
            List<Block> entryBlocks = Blocks(s, entry); blocks.AddRange(entryBlocks);
            foreach (Block block in entryBlocks) if (entry.UseTimeOff && block.Seconds == 1800 && block.Pieces[0].From <= now)
                events.Add(new Event { When = block.Pieces[0].From, Type = 2, Minutes = 30, Block = block });
        }
        // 已登记手动调休是已经使用的额度，先保留；自动抵扣只能使用其余信用。
        events.Sort(delegate(Event a, Event b) { int order = a.When.CompareTo(b.When); return order == 0 ? a.Type.CompareTo(b.Type) : order; });
        Queue<Event> manualCredits = new Queue<Event>(); int debt = 0;
        foreach (Event item in events)
        {
            if (item.Type == 0)
            {
                int repayment = Math.Min(debt, item.Minutes); debt -= repayment; item.Minutes -= repayment;
                if (item.Minutes > 0) manualCredits.Enqueue(item);
            }
            else if (item.Type == 1)
            {
                int needed = item.Minutes;
                while (needed > 0 && manualCredits.Count > 0)
                {
                    Event credit = manualCredits.Peek(); int consumed = Math.Min(needed, credit.Minutes);
                    credit.Minutes -= consumed; needed -= consumed;
                    if (credit.Minutes == 0) manualCredits.Dequeue();
                }
                debt += needed;
            }
        }
        // 每笔加班完成时就补抵此前等待的同月请假，不能等所有未来请假处理后才补抵。
        // 否则较晚请假会重新抢走较早请假已经获得的信用，导致旧日扣薪反复变化。
        Queue<Event> credits = new Queue<Event>(); List<Block> waiting = new List<Block>(); int available = 0;
        foreach (Event item in events)
        {
            if (item.Type == 0 && item.Minutes > 0)
            {
                credits.Enqueue(item); available += item.Minutes;
                foreach (Block block in waiting)
                {
                    DateTime begin = block.Pieces[0].From, end = block.Pieces[block.Pieces.Count - 1].Until;
                    if (item.Minutes < 30) break;
                    if (block.Covered || item.When < end || item.When.Year != begin.Year || item.When.Month != begin.Month) continue;
                    block.Covered = true; block.Retro = true; item.Minutes -= 30; available -= 30;
                    if (begin.Date == now.Date) result.UsedToday += 30;
                    if (begin.Year == now.Year && begin.Month == now.Month) result.MonthRetroMinutes += 30;
                }
            }
            else if (item.Type == 2)
            {
                if (available < 30) { if (item.Block.Entry.PayPercent < 100) waiting.Add(item.Block); continue; }
                item.Block.Covered = true; if (item.When.Date == now.Date) result.UsedToday += 30;
                int needed = 30;
                while (needed > 0 && credits.Count > 0)
                {
                    Event credit = credits.Peek(); int consumed = Math.Min(needed, credit.Minutes);
                    credit.Minutes -= consumed; needed -= consumed; available -= consumed;
                    if (credit.Minutes == 0) credits.Dequeue();
                }
            }
        }
        result.Deficit = debt > 0; result.BalanceMinutes = available;
        foreach (Event credit in credits)
        {
            decimal value = credit.Rate * credit.Minutes / 60m; result.BalanceValue += value;
            if (credit.When.Date == now.Date) result.TodayOvertimeValue += value;
            AbsenceDay day; if (daily != null && daily.TryGetValue(credit.When.Date, out day)) day.OvertimeValue += value;
        }
        // Covered统一包含发生时抵扣与之后补抵；每块只统计一次，补抵不再另加到已用调休。
        foreach (Block block in blocks) if (block.Covered)
        {
            if (block.Pieces[0].From.Year == now.Year && block.Pieces[0].From.Month == now.Month) result.MonthUsedMinutes += 30;
            AbsenceDay day;
            if (daily != null && daily.TryGetValue(block.Pieces[0].From.Date, out day)) { day.AutoMinutes += 30; if (block.Retro) day.RetroMinutes += 30; }
        }
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
            result.MonthElapsedSeconds += elapsed;
            if (!block.Covered && block.Entry.PayPercent < 100) result.MonthUncoveredSeconds += elapsed;
            int days = s.ManualDays && s.ManualMonth == piece.From.ToString("yyyy-MM") ? s.Days : WorkClock.CountDays(s, piece.From);
            decimal loss = (s.MonthlySalary / days) * (decimal)elapsed / (decimal)dailySeconds * (100 - paid) / 100m;
            result.MonthLoss += loss; if (piece.From.Date == now.Date) result.TodayLoss += loss;
            AbsenceDay reportDay;
            if (daily != null && daily.TryGetValue(piece.From.Date, out reportDay))
            {
                reportDay.LeaveSeconds += elapsed; reportDay.Loss += loss;
                if (!block.Covered && block.Entry.PayPercent < 100) reportDay.UncoveredSeconds += elapsed;
            }
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
    private readonly Button add;
    private int editingIndex = -1;
    private bool fillingRecords, loadingEditor, editorDirty;
    public Settings Result;
    public AbsenceForm(Settings source, Func<Settings> getCurrent = null)
    {
        draft = source.Copy(); Text = "请假管理 · 上班鼓励器"; Font = new Font("Microsoft YaHei UI", 9);
        StartPosition = FormStartPosition.CenterParent; FormBorderStyle = FormBorderStyle.FixedDialog; MaximizeBox = false; MinimizeBox = false;
        ClientSize = new Size(620, Math.Min(640, Screen.FromPoint(Cursor.Position).WorkingArea.Height - 100)); AutoScroll = true;
        Controls.Add(new Label { Text = "事假 / 病假 / 年假", Bounds = new Rectangle(20, 18, 560, 28), Font = new Font(Font, FontStyle.Bold) });
        ComboBox kind = new ComboBox { Name = "AbsenceKind", DropDownStyle = ComboBoxStyle.DropDownList, Bounds = new Rectangle(20, 56, 130, 28) }; kind.Items.AddRange(AbsenceClock.Names); Controls.Add(kind);
        Controls.Add(new Label { Text = "发薪比例 %", Bounds = new Rectangle(170, 60, 100, 25) });
        TextBox percent = new TextBox { Name = "AbsencePercent", Bounds = new Rectangle(275, 56, 90, 28) }; Controls.Add(percent);
        CheckBox bank = new CheckBox { Name = "UseTimeOffFirst", Text = "优先使用调休（1:1）", Checked = true, Bounds = new Rectangle(390, 56, 215, 28) }; Controls.Add(bank);
        kind.SelectedIndexChanged += delegate { decimal value = kind.SelectedIndex == 0 ? draft.PersonalLeavePercent : kind.SelectedIndex == 1 ? draft.SickLeavePercent : draft.AnnualLeavePercent; percent.Text = value < 0 ? "" : value.ToString(CultureInfo.CurrentCulture); }; kind.SelectedIndex = 0;
        Controls.Add(new Label { Text = "0%=无薪；100%=全薪不扣款。选中记录可修改，保存并应用后生效。", Bounds = new Rectangle(20, 86, 580, 17), Font = new Font(Font.FontFamily, 8) });
        Controls.Add(new Label { Text = "开始", Bounds = new Rectangle(20, 108, 65, 26) });
        DateTimePicker from = new DateTimePicker { Name = "AbsenceFrom", Format = DateTimePickerFormat.Custom, CustomFormat = "yyyy-MM-dd HH:mm", Value = DateTime.Today.Add(TimeSpan.Parse(source.Start)), Bounds = new Rectangle(90, 104, 215, 28) }; Controls.Add(from);
        Controls.Add(new Label { Text = "结束", Bounds = new Rectangle(320, 108, 65, 26) });
        DateTimePicker until = new DateTimePicker { Name = "AbsenceUntil", Format = DateTimePickerFormat.Custom, CustomFormat = "yyyy-MM-dd HH:mm", Value = DateTime.Today.Add(TimeSpan.Parse(source.End)), Bounds = new Rectangle(385, 104, 215, 28) }; Controls.Add(until);
        Button whole = new Button { Text = "按所选日期整天", Bounds = new Rectangle(20, 151, 175, 32) }; Controls.Add(whole);
        whole.Click += delegate { from.Value = from.Value.Date.Add(TimeSpan.Parse(draft.Start)); until.Value = until.Value.Date.Add(TimeSpan.Parse(draft.End)); editorDirty = true; };
        add = new Button { Name = "AddAbsence", Text = "添加请假", Bounds = new Rectangle(205, 151, 120, 32) }; Controls.Add(add);
        Button newEntry = new Button { Name = "NewAbsence", Text = "新建请假", Bounds = new Rectangle(335, 151, 105, 32) }; Controls.Add(newEntry);
        Button remove = new Button { Name = "RemoveAbsence", Text = "删除选中记录", Bounds = new Rectangle(450, 151, 150, 32) }; Controls.Add(remove);
        records = new ListBox { Name = "AbsenceEntries", Bounds = new Rectangle(20, 200, 580, 185), HorizontalScrollbar = true }; Controls.Add(records);
        info = new Label { Name = "AbsenceInfo", Bounds = new Rectangle(20, 402, 580, 52) }; Controls.Add(info);
        Controls.Add(new Label { Text = "可提前安排或补录，扣除午休和休息日。计薪0%=无薪，100%=全额发薪。\n调休不预留；之后完成的加班可补抵同月较早、勾选优先调休的非全薪请假。\n已使用的调休先保留；有效尾数不足半小时不扣调休，历史修改会重新计算。\n此处自动扣除的调休，请勿再到加班窗口重复登记。", Bounds = new Rectangle(20, 463, 580, 90) });
        Button save = new Button { Name = "SaveAbsence", Text = "保存并应用", Bounds = new Rectangle(410, 570, 190, 36) }; Controls.Add(save);
        Button cancel = new Button { Text = "取消", DialogResult = DialogResult.Cancel, Bounds = new Rectangle(290, 570, 100, 36) }; Controls.Add(cancel); AcceptButton = save; CancelButton = cancel;
        EventHandler changed = delegate { if (!loadingEditor) editorDirty = true; };
        kind.SelectedIndexChanged += changed; percent.TextChanged += changed; from.ValueChanged += changed; until.ValueChanged += changed; bank.CheckedChanged += changed;
        Func<bool> commitRecord = delegate
        {
            try
            {
                decimal ratio; if (!decimal.TryParse(percent.Text, out ratio) || ratio < 0 || ratio > 100) throw new Exception("请明确填写0%至100%的计薪比例。");
                if (ratio == 100 && kind.SelectedIndex != 2 && MessageBox.Show(this, "当前是100%发薪，这段" + AbsenceClock.Names[kind.SelectedIndex] + "不会扣工资。\n确认按全薪登记吗？无薪请假应填0%。", "确认全薪请假", MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2) != DialogResult.Yes) return false;
                Settings next = draft.Copy();
                AbsenceEntry entry = new AbsenceEntry { From = from.Value.AddTicks(-(from.Value.Ticks % TimeSpan.TicksPerMinute)), Until = until.Value.AddTicks(-(until.Value.Ticks % TimeSpan.TicksPerMinute)), Kind = kind.SelectedIndex, PayPercent = ratio, UseTimeOff = bank.Checked };
                if (editingIndex >= 0) next.Absences[editingIndex] = entry; else next.Absences.Add(entry);
                next.Validate(); draft.Absences = next.Absences;
                if (kind.SelectedIndex == 0) draft.PersonalLeavePercent = ratio; else if (kind.SelectedIndex == 1) draft.SickLeavePercent = ratio; else draft.AnnualLeavePercent = ratio;
                if (editingIndex < 0) editingIndex = draft.Absences.Count - 1;
                editorDirty = false; RefreshRecords(); return true;
            }
            catch (Exception ex) { MessageBox.Show(this, ex.Message, "请假未保存"); return false; }
        };
        add.Click += delegate { commitRecord(); };
        records.SelectedIndexChanged += delegate
        {
            if (fillingRecords || records.SelectedIndex < 0) return;
            int selected = records.SelectedIndex;
            if (editorDirty && !commitRecord()) { fillingRecords = true; records.SelectedIndex = editingIndex; fillingRecords = false; return; }
            fillingRecords = loadingEditor = true; editingIndex = selected; records.SelectedIndex = selected;
            AbsenceEntry entry = draft.Absences[selected]; kind.SelectedIndex = entry.Kind; percent.Text = entry.PayPercent.ToString(CultureInfo.CurrentCulture);
            from.Value = entry.From; until.Value = entry.Until; bank.Checked = entry.UseTimeOff;
            editorDirty = false; fillingRecords = loadingEditor = false; add.Text = "更新选中";
        };
        newEntry.Click += delegate
        {
            if (editorDirty && !commitRecord()) return;
            loadingEditor = true; editingIndex = -1; records.ClearSelected(); kind.SelectedIndex = 0; percent.Text = draft.PersonalLeavePercent.ToString(CultureInfo.CurrentCulture);
            from.Value = DateTime.Today.Add(TimeSpan.Parse(draft.Start)); until.Value = DateTime.Today.Add(TimeSpan.Parse(draft.End)); bank.Checked = true;
            editorDirty = false; loadingEditor = false; add.Text = "添加请假";
        };
        remove.Click += delegate { if (records.SelectedIndex >= 0) { draft.Absences.RemoveAt(records.SelectedIndex); editingIndex = -1; editorDirty = false; newEntry.PerformClick(); RefreshRecords(); } };
        save.Click += delegate
        {
            try
            {
                // 编辑后直接点底部保存也要提交当前条目，不能让输入框和真正保存的记录不同。
                if (editorDirty && !commitRecord()) return;
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
        fillingRecords = true; records.Items.Clear();
        foreach (AbsenceEntry entry in draft.Absences) records.Items.Add(AbsenceClock.Names[entry.Kind] + " 发薪" + entry.PayPercent + "%" + (entry.PayPercent == 0 ? "（无薪）" : entry.PayPercent == 100 ? "（全薪不扣款）" : "") + " " + entry.From.ToString("yyyy-MM-dd HH:mm") + "—" + entry.Until.ToString("yyyy-MM-dd HH:mm") + (entry.UseTimeOff ? " 优先调休" : " 不用调休"));
        if (editingIndex >= 0 && editingIndex < records.Items.Count) records.SelectedIndex = editingIndex;
        add.Text = editingIndex >= 0 ? "更新选中" : "添加请假"; fillingRecords = false;
        AbsenceSnapshot status = AbsenceClock.At(draft, DateTime.Now);
        info.Text = "今日计划请假 " + (status.TodaySeconds / 3600).ToString("0.##") + " 小时；自动调休 " + (status.UsedToday / 60m).ToString("0.#") + " 小时；本月补抵 " + (status.MonthRetroMinutes / 60m).ToString("0.#") + " 小时\n" + (draft.PrivacyMode ? "请假扣款已隐藏" : "今日请假扣薪 ¥ " + status.TodayLoss.ToString("N2")) + "（已扣除调休覆盖；未来请假不提前扣薪）";
    }
}
