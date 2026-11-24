using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Text;
using System.Windows.Forms;

public sealed class LedgerRow
{
    public DateTime Date;
    public string Status;
    public AbsenceDay Settlement;
    public int OvertimeMinutes;
    public decimal NormalIncome, TotalIncome;
}
public sealed class LedgerReport
{
    public DateTime Month, Cutoff;
    public bool Future;
    public List<LedgerRow> Rows = new List<LedgerRow>();
    public double LeaveSeconds;
    public int AutoMinutes, ManualMinutes, OvertimeMinutes, BalanceMinutes;
    public decimal NormalSum, OvertimeSum, IncomeSum, RegularMonthIncome;
}
public static class MonthlyLedger
{
    public const string Rule = "按当前薪资、作息和已登记记录估算，非历史工资流水；已计入截至所选月份统计时点的后续补抵。日合计含未用加班，挂件月累计沿用正常工资封顶；金额显示可有舍入尾差。";
    public static LedgerReport Build(Settings source, DateTime month, DateTime now)
    {
        DateTime start = new DateTime(month.Year, month.Month, 1), end = start.AddMonths(1).AddTicks(-1);
        LedgerReport report = new LedgerReport { Month = start, Cutoff = now < end ? now : end, Future = now < start };
        Dictionary<DateTime, AbsenceDay> daily = new Dictionary<DateTime, AbsenceDay>();
        for (int n = 1; n <= DateTime.DaysInMonth(start.Year, start.Month); n++) daily.Add(start.AddDays(n - 1), new AbsenceDay());
        Settings regular = source.Copy(); regular.Absences.Clear(); regular.OvertimeEntries.Clear(); regular.LeaveEntries.Clear();
        if (!report.Future)
        {
            // 统一结算一次，再按请假发生日和加班赚取日归属；不能逐日截断后续补抵。
            AbsenceSnapshot settled = AbsenceClock.At(source, report.Cutoff, daily);
            report.BalanceMinutes = settled.BalanceMinutes;
            report.RegularMonthIncome = Math.Max(0, WorkClock.At(regular, report.Cutoff).MonthEarned - settled.MonthLoss);
        }
        foreach (KeyValuePair<DateTime, AbsenceDay> item in daily)
        {
            DateTime date = item.Key; AbsenceDay day = item.Value;
            bool future = date > report.Cutoff.Date || report.Future;
            LedgerRow row = new LedgerRow { Date = date, Settlement = day, Status = future ? "未发生" : WorkClock.IsWorkingDay(source, date) ? "工作日" : "休息日" };
            if (!future)
            {
                DateTime moment = date == report.Cutoff.Date ? report.Cutoff : date.AddDays(1).AddTicks(-1);
                row.NormalIncome = Math.Max(0, WorkClock.At(regular, moment).NormalEarned - day.Loss);
                foreach (OvertimeEntry entry in source.OvertimeEntries) if (entry.Start.Date == date)
                {
                    double seconds = Math.Max(0, Math.Min((entry.End - entry.Start).TotalSeconds, (report.Cutoff - entry.Start).TotalSeconds));
                    row.OvertimeMinutes += (int)(seconds / 1800) * 30;
                }
                row.TotalIncome = row.NormalIncome + day.OvertimeValue;
            }
            report.LeaveSeconds += day.LeaveSeconds; report.AutoMinutes += day.AutoMinutes; report.ManualMinutes += day.ManualMinutes; report.OvertimeMinutes += row.OvertimeMinutes;
            report.NormalSum += row.NormalIncome; report.OvertimeSum += day.OvertimeValue; report.IncomeSum += row.TotalIncome;
            report.Rows.Add(row);
        }
        report.Rows.Sort(delegate(LedgerRow a, LedgerRow b) { return a.Date.CompareTo(b.Date); });
        return report;
    }
    public static string Money(decimal value, bool privacy) { return privacy ? "已隐藏" : value.ToString("0.00", CultureInfo.InvariantCulture); }
    public static string Hours(double seconds) { return (seconds / 3600).ToString("0.##", CultureInfo.InvariantCulture); }
    public static string[] Cells(LedgerRow row, bool privacy)
    {
        AbsenceDay day = row.Settlement;
        return new[] { row.Date.ToString("yyyy-MM-dd"), row.Status, Hours(day.LeaveSeconds), Hours(day.AutoMinutes * 60), Hours(day.RetroMinutes * 60), Hours(day.ManualMinutes * 60), Hours(row.OvertimeMinutes * 60), Money(row.NormalIncome, privacy), Money(day.OvertimeValue, privacy), Money(row.TotalIncome, privacy) };
    }
    public static string Describe(Settings settings, LedgerReport report, LedgerRow row, bool privacy)
    {
        StringBuilder text = new StringBuilder(row.Date.ToString("yyyy-MM-dd") + " · " + row.Status + "\r\n");
        foreach (AbsenceEntry entry in settings.Absences) if (entry.From < row.Date.AddDays(1) && entry.Until > row.Date)
            text.AppendLine(AbsenceClock.Names[entry.Kind] + "：" + entry.From.ToString("yyyy-MM-dd HH:mm") + " 至 " + entry.Until.ToString("yyyy-MM-dd HH:mm") + "；计薪 " + entry.PayPercent.ToString("0.##") + "%" + (entry.UseTimeOff ? "；优先调休" : "；不使用调休"));
        foreach (OvertimeEntry entry in settings.OvertimeEntries) if (entry.Start.Date == row.Date)
            text.AppendLine("加班：" + entry.Start.ToString("HH:mm") + " 至 " + entry.End.ToString("HH:mm") + (entry.Finished ? "（已提前结束）" : "（登记时段）") + "；登记时薪 " + Money(entry.HourlyRate, privacy));
        if (row.Settlement.AutoMinutes > 0) text.AppendLine("自动抵扣 " + Hours(row.Settlement.AutoMinutes * 60) + " 小时，其中后续加班补抵 " + Hours(row.Settlement.RetroMinutes * 60) + " 小时（已包含，未重复计数）。");
        if (row.Settlement.ManualMinutes > 0) text.AppendLine("手动登记已用调休 " + Hours(row.Settlement.ManualMinutes * 60) + " 小时，仅冲抵加班余额，不再次调整普通工资。");
        text.AppendLine("当日请假未计薪部分：" + Money(row.Settlement.Loss, privacy) + "；未抵扣非全薪请假 " + Hours(row.Settlement.UncoveredSeconds) + " 小时。");
        text.Append("已发生工时统计至 " + report.Cutoff.ToString("yyyy-MM-dd HH:mm") + "；未来计划不提前计收入。更新或补录记录后可刷新重算。");
        return text.ToString();
    }
    public static string Csv(LedgerReport report, bool privacy)
    {
        List<string[]> records = new List<string[]>();
        records.Add(new[] { "月份", report.Month.ToString("yyyy-MM"), "统计截至", report.Cutoff.ToString("yyyy-MM-dd HH:mm:ss"), report.Future ? "该月尚未开始" : "已发生记录" });
        records.Add(new[] { "口径", Rule });
        records.Add(new[] { "挂件月累计（正常工资封顶，不含加班）", Money(report.RegularMonthIncome, privacy), "当前/期末可调休小时", report.Future ? "未发生" : Hours(report.BalanceMinutes * 60) });
        records.Add(new[] { "日期", "状态", "已发生请假小时", "自动调休小时", "其中补抵小时（含于自动）", "手动调休小时", "已完成加班小时", "正常工资估算", "未调休加班折算", "当日合计估算" });
        foreach (LedgerRow row in report.Rows) records.Add(Cells(row, privacy));
        int retro = 0; foreach (LedgerRow row in report.Rows) retro += row.Settlement.RetroMinutes;
        records.Add(new[] { "合计", "", Hours(report.LeaveSeconds), Hours(report.AutoMinutes * 60), Hours(retro * 60), Hours(report.ManualMinutes * 60), Hours(report.OvertimeMinutes * 60), Money(report.NormalSum, privacy), Money(report.OvertimeSum, privacy), Money(report.IncomeSum, privacy) });
        StringBuilder csv = new StringBuilder();
        foreach (string[] record in records)
        {
            for (int n = 0; n < record.Length; n++) { if (n > 0) csv.Append(','); csv.Append('"').Append(record[n].Replace("\"", "\"\"")).Append('"'); }
            csv.Append("\r\n");
        }
        return csv.ToString();
    }
    public static string Export(LedgerReport report, string directory, bool privacy)
    {
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, "月度明细账-" + report.Month.ToString("yyyy-MM") + "-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + "-" + Guid.NewGuid().ToString("N").Substring(0, 6) + ".csv");
        using (FileStream stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write))
        using (StreamWriter writer = new StreamWriter(stream, new UTF8Encoding(true))) writer.Write(Csv(report, privacy));
        return path;
    }
}

public sealed class MonthlyLedgerForm : Form
{
    private Settings settings;
    private readonly Func<Settings> getCurrent;
    private readonly DateTimePicker month;
    private readonly DataGridView grid;
    private readonly Label summary, status;
    private readonly TextBox details;
    private LedgerReport report;
    public MonthlyLedgerForm(Settings source, Func<Settings> current = null)
    {
        settings = source.Copy(); getCurrent = current;
        Text = "月度明细账 · 上班鼓励器"; Font = new Font("Microsoft YaHei UI", 9); StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(740, 540); ClientSize = new Size(1090, 680);
        Rectangle area = Screen.FromPoint(Cursor.Position).WorkingArea; Size = new Size(Math.Min(Width, area.Width), Math.Min(Height, area.Height));
        BackColor = source.Theme == 0 ? Color.FromArgb(18, 23, 43) : Color.FromArgb(252, 241, 249); ForeColor = source.Theme == 0 ? Color.FromArgb(223, 232, 250) : Color.FromArgb(86, 55, 91);
        Color surface = source.Theme == 0 ? Color.FromArgb(32, 42, 67) : Color.FromArgb(255, 248, 254);
        TableLayoutPanel layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 6, Padding = new Padding(12) };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 42)); layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 65)); layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 44)); layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 115)); layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 31)); Controls.Add(layout);
        Panel toolbar = new Panel { Dock = DockStyle.Fill }; layout.Controls.Add(toolbar, 0, 0);
        toolbar.Controls.Add(new Label { Text = "选择月份", Bounds = new Rectangle(0, 7, 75, 25) });
        month = new DateTimePicker { Name = "LedgerMonth", Format = DateTimePickerFormat.Custom, CustomFormat = "yyyy年MM月", ShowUpDown = true, Bounds = new Rectangle(77, 3, 150, 29), Value = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1), MaxDate = new DateTime(9998, 12, 1) }; toolbar.Controls.Add(month);
        Button refresh = new Button { Name = "LedgerRefresh", Text = "刷新", Bounds = new Rectangle(240, 2, 80, 31), BackColor = surface, ForeColor = ForeColor }; toolbar.Controls.Add(refresh);
        Button export = new Button { Name = "LedgerExport", Text = "导出 CSV", Bounds = new Rectangle(332, 2, 110, 31), BackColor = surface, ForeColor = ForeColor }; toolbar.Controls.Add(export);
        Button directory = new Button { Text = "打开导出目录", Bounds = new Rectangle(454, 2, 135, 31), BackColor = surface, ForeColor = ForeColor }; toolbar.Controls.Add(directory);
        summary = new Label { Name = "LedgerSummary", Dock = DockStyle.Fill, Padding = new Padding(0, 4, 0, 0) }; layout.Controls.Add(summary, 0, 1);
        layout.Controls.Add(new Label { Text = MonthlyLedger.Rule, Dock = DockStyle.Fill }, 0, 2);
        grid = new DataGridView { Name = "LedgerRows", Dock = DockStyle.Fill, ReadOnly = true, AllowUserToAddRows = false, AllowUserToDeleteRows = false, AllowUserToResizeRows = false, AutoGenerateColumns = false, RowHeadersVisible = false, SelectionMode = DataGridViewSelectionMode.FullRowSelect, MultiSelect = false, BackgroundColor = surface, BorderStyle = BorderStyle.None, EnableHeadersVisualStyles = false, ColumnHeadersHeight = 42, ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing };
        grid.DefaultCellStyle.BackColor = surface; grid.DefaultCellStyle.ForeColor = ForeColor; grid.DefaultCellStyle.SelectionBackColor = source.Theme == 0 ? Color.FromArgb(47, 80, 125) : Color.FromArgb(235, 200, 230); grid.DefaultCellStyle.SelectionForeColor = ForeColor;
        grid.ColumnHeadersDefaultCellStyle.BackColor = BackColor; grid.ColumnHeadersDefaultCellStyle.ForeColor = ForeColor;
        string[] headers = { "日期", "状态", "请假\n小时", "自动调休\n小时", "其中补抵\n小时", "手动调休\n小时", "已完成加班\n小时", "正常工资\n估算", "未调休加班\n折算", "当日合计\n估算" };
        for (int n = 0; n < headers.Length; n++) grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "LedgerColumn" + n, HeaderText = headers[n], Width = n == 0 ? 108 : n == 1 ? 68 : n >= 7 ? 117 : 84, SortMode = DataGridViewColumnSortMode.NotSortable });
        layout.Controls.Add(grid, 0, 3);
        details = new TextBox { Name = "LedgerDetails", Dock = DockStyle.Fill, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, BackColor = surface, ForeColor = ForeColor }; layout.Controls.Add(details, 0, 4);
        status = new Label { Name = "LedgerStatus", Text = "导出至程序目录的“导出”文件夹；隐私模式同步遮住界面及CSV金额。", Dock = DockStyle.Fill, Padding = new Padding(0, 5, 0, 0) }; layout.Controls.Add(status, 0, 5);
        month.ValueChanged += delegate { RefreshReport(); }; refresh.Click += delegate { RefreshReport(); };
        // SelectionChanged会早于CurrentRow更新，详情跟随已切换的当前单元格。
        grid.CurrentCellChanged += delegate { ShowDetails(); };
        export.Click += delegate
        {
            try
            {
                RefreshReport(); string path = MonthlyLedger.Export(report, Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "导出"), settings.PrivacyMode);
                status.Text = "已导出 " + Path.GetFileName(path) + (settings.PrivacyMode ? "（金额已隐藏）" : "（包含金额）");
            }
            catch (Exception ex) { MessageBox.Show(this, "导出失败：\n" + ex.Message, "月度明细账"); }
        };
        directory.Click += delegate
        {
            try { string path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "导出"); Directory.CreateDirectory(path); Process.Start(path); }
            catch (Exception ex) { MessageBox.Show(this, ex.Message, "无法打开导出目录"); }
        };
        Activated += delegate { RefreshReport(); }; RefreshReport();
    }
    public void ApplyPrivacy(bool enabled) { settings.PrivacyMode = enabled; RefreshReport(); }
    private void RefreshReport()
    {
        if (getCurrent != null) settings = getCurrent().Copy();
        report = MonthlyLedger.Build(settings, month.Value, DateTime.Now);
        bool privacy = settings.PrivacyMode; int selected = grid.CurrentRow == null ? -1 : grid.CurrentRow.Index;
        grid.Rows.Clear();
        foreach (LedgerRow row in report.Rows) { int index = grid.Rows.Add(MonthlyLedger.Cells(row, privacy)); grid.Rows[index].Tag = row; }
        if (grid.Rows.Count > 0) grid.CurrentCell = grid.Rows[selected >= 0 && selected < grid.Rows.Count ? selected : Math.Min(DateTime.Today.Day - 1, grid.Rows.Count - 1)].Cells[0];
        summary.Text = report.Month.ToString("yyyy年MM月") + " · 统计至 " + report.Cutoff.ToString("yyyy-MM-dd HH:mm") + (report.Future ? "（该月尚未开始）" : "") + "\n请假 " + MonthlyLedger.Hours(report.LeaveSeconds) + "h · 加班 " + MonthlyLedger.Hours(report.OvertimeMinutes * 60) + "h · 已用调休 " + MonthlyLedger.Hours((report.AutoMinutes + report.ManualMinutes) * 60) + "h · 当前/期末可调休 " + (report.Future ? "未发生" : MonthlyLedger.Hours(report.BalanceMinutes * 60) + "h") + "\n日合计求和（含加班） " + MonthlyLedger.Money(report.IncomeSum, privacy) + " · 挂件月累计（正常工资封顶） " + MonthlyLedger.Money(report.RegularMonthIncome, privacy);
        ShowDetails();
    }
    private void ShowDetails()
    {
        LedgerRow row = grid.CurrentRow == null ? null : grid.CurrentRow.Tag as LedgerRow;
        details.Text = row == null ? "选择一行查看原始登记时段与抵扣说明。" : MonthlyLedger.Describe(settings, report, row, settings.PrivacyMode);
    }
}
