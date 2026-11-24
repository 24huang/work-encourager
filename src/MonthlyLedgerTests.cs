using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Text;
using System.Windows.Forms;
using System.Xml.Serialization;

public static class MonthlyLedgerTests
{
    public static void Run(Action<bool, string> check)
    {
        DateTime day = new DateTime(2026, 11, 18), cutoff = day.AddHours(19);
        Settings s = new Settings { MonthlySalary = 8800, ManualDays = true, Days = 22, ManualMonth = "2026-11", DailySummaryEnabled = false, PetEnabled = false };
        s.Absences.Add(new AbsenceEntry { From = day.AddDays(-1).AddHours(9), Until = day.AddDays(-1).AddHours(12), PayPercent = 0, UseTimeOff = true });
        s.OvertimeEntries.Add(new OvertimeEntry { Start = day.AddHours(18), Minutes = 60, HourlyRate = 50 });
        string original; using (StringWriter writer = new StringWriter()) { new XmlSerializer(typeof(Settings)).Serialize(writer, s); original = writer.ToString(); }
        LedgerReport report = MonthlyLedger.Build(s, day, cutoff); LedgerRow leave = report.Rows[16], overtime = report.Rows[17];
        check(report.Rows.Count == 30 && report.Rows[0].Date.Day == 1 && report.Rows[29].Date.Day == 30, "Ledger contains every calendar day in chronological order");
        check(leave.Settlement.LeaveSeconds == 10800 && leave.Settlement.AutoMinutes == 60 && leave.Settlement.RetroMinutes == 60 && leave.NormalIncome == 300, "Later overtime is settled back into the original leave date instead of leaving its old deduction in the ledger");
        check(overtime.OvertimeMinutes == 60 && overtime.Settlement.OvertimeValue == 0 && overtime.TotalIncome == 400, "Overtime already used for leave is not counted again in that day's extra income");
        Snapshot snapshot = WorkClock.At(s, cutoff);
        check(report.RegularMonthIncome == snapshot.MonthEarned && overtime.TotalIncome == snapshot.Earned && report.AutoMinutes + report.ManualMinutes == snapshot.Overtime.MonthUsedMinutes, "Ledger matches the widget's current monthly income, daily income and used time-off");
        check(report.LeaveSeconds == 10800 && report.OvertimeMinutes == 60 && report.IncomeSum == 5100, "Monthly ledger totals reconcile the three-hour leave and one-hour overtime example");
        check(report.Rows[18].Status == "未发生" && report.Rows[18].TotalIncome == 0 && report.Rows[18].Settlement.LeaveSeconds == 0, "Future days cannot accrue estimated income or time-off prematurely");
        LedgerReport unfinished = MonthlyLedger.Build(s, day, day.AddHours(18).AddMinutes(29));
        check(unfinished.OvertimeMinutes == 0 && unfinished.Rows[16].NormalIncome == 250, "Unfinished overtime does not appear as completed credit in the ledger");
        Settings partial = s.Copy(); partial.Absences[0].PayPercent = 50;
        check(MonthlyLedger.Build(partial, day, cutoff).Rows[16].NormalIncome == 350, "Ledger preserves each recorded leave-pay percentage after retroactive time-off");
        Settings capped = s.Copy(); capped.Days = 20;
        LedgerReport cappedReport = MonthlyLedger.Build(capped, day, new DateTime(2026, 11, 30, 19, 0, 0));
        check(cappedReport.RegularMonthIncome == WorkClock.At(capped, new DateTime(2026, 11, 30, 19, 0, 0)).MonthEarned && cappedReport.NormalSum > cappedReport.RegularMonthIncome, "Daily sums and the separately labelled capped monthly widget amount retain their original meanings");
        Settings crossing = s.Copy(); crossing.Absences.Clear(); crossing.OvertimeEntries.Clear();
        crossing.Absences.Add(new AbsenceEntry { From = new DateTime(2026, 10, 30, 9, 0, 0), Until = new DateTime(2026, 11, 2, 18, 0, 0), PayPercent = 0 });
        LedgerReport october = MonthlyLedger.Build(crossing, new DateTime(2026, 10, 1), cutoff);
        LedgerReport november = MonthlyLedger.Build(crossing, day, cutoff);
        check(october.LeaveSeconds == 28800 && november.LeaveSeconds == 28800 && november.Rows[0].Settlement.LeaveSeconds == 0, "Cross-month leave is split by effective day, excluding the weekend");
        check(october.Cutoff == new DateTime(2026, 11, 1).AddTicks(-1), "Past-month reports close at month end instead of including later-month activity");
        LedgerReport future = MonthlyLedger.Build(s, day.AddMonths(1), cutoff);
        check(future.Future && future.NormalSum == 0 && future.OvertimeSum == 0 && future.RegularMonthIncome == 0, "A future month contains no earned amounts");
        check(MonthlyLedger.Build(s, new DateTime(2024, 2, 1), cutoff).Rows.Count == 29, "Ledger handles leap-year February");
        Settings manual = s.Copy(); manual.Absences.Clear(); manual.OvertimeEntries.Clear();
        manual.OvertimeEntries.Add(new OvertimeEntry { Start = day.AddMonths(-1).AddHours(18), Minutes = 120, HourlyRate = 50 });
        manual.LeaveEntries.Add(new LeaveEntry { Date = day.AddDays(-1), Minutes = 30, RegisteredAt = day.AddDays(-1).AddHours(14) });
        LedgerReport manualReport = MonthlyLedger.Build(manual, day, cutoff);
        check(manualReport.ManualMinutes == 30 && manualReport.AutoMinutes == 0 && manualReport.BalanceMinutes == 90 && manualReport.OvertimeMinutes == 0, "Manual usage and previous-month credit carryover reconcile without appearing as this month's earned overtime");
        Settings fifo = manual.Copy(); fifo.OvertimeEntries.Clear(); fifo.LeaveEntries[0].Date = day; fifo.LeaveEntries[0].RegisteredAt = day.AddHours(19.5); fifo.LeaveEntries[0].Minutes = 90;
        fifo.OvertimeEntries.Add(new OvertimeEntry { Start = day.AddDays(-1).AddHours(18), Minutes = 60, HourlyRate = 50 }); fifo.OvertimeEntries.Add(new OvertimeEntry { Start = day.AddHours(18), Minutes = 90, HourlyRate = 80 });
        LedgerReport fifoReport = MonthlyLedger.Build(fifo, day, day.AddHours(19.5));
        check(fifoReport.Rows[16].Settlement.OvertimeValue == 0 && fifoReport.Rows[17].Settlement.OvertimeValue == 80 && fifoReport.Rows[17].TotalIncome == 480, "Ledger attributes remaining FIFO credits to the actual earning day and captured hourly rate");
        s.OvertimeEntries[0].Finished = true; s.OvertimeEntries[0].StoppedAt = day.AddHours(18).AddMinutes(47);
        check(MonthlyLedger.Build(s, day, cutoff).OvertimeMinutes == 30, "An early overtime finish only records completed half-hours"); s.OvertimeEntries[0].Finished = false;
        string description = MonthlyLedger.Describe(s, report, leave, false);
        check(description.Contains("09:00") && description.Contains("12:00") && description.Contains("计薪 0%") && description.Contains("其中后续加班补抵 1"), "Daily explanations include the registered interval, pay ratio and retroactive coverage");
        string csv = MonthlyLedger.Csv(report, false), hidden = MonthlyLedger.Csv(report, true);
        check(csv.Contains("\"正常工资估算\"") && csv.Contains("\"300.00\"") && csv.Contains("\"5100.00\""), "CSV includes labelled daily estimates and their report totals");
        check(hidden.Contains("已隐藏") && !hidden.Contains("300.00") && !hidden.Contains("5100.00") && !MonthlyLedger.Describe(s, report, overtime, true).Contains("50.00"), "Privacy masks row amounts, totals and original overtime rates in CSV and explanations");
        string oldStatus = report.Rows[0].Status; report.Rows[0].Status = "说明，含\"引号\"";
        check(MonthlyLedger.Csv(report, false).Contains("\"说明，含\"\"引号\"\"\""), "CSV quotes embedded punctuation safely"); report.Rows[0].Status = oldStatus;
        string exportDirectory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ledger-verification");
        string first = MonthlyLedger.Export(report, exportDirectory, true), second = MonthlyLedger.Export(report, exportDirectory, true);
        byte[] bytes = File.ReadAllBytes(first);
        check(first != second && bytes.Length > 3 && bytes[0] == 239 && bytes[1] == 187 && bytes[2] == 191 && File.ReadAllText(first).Contains("已隐藏"), "Exports use separate files and a Chinese-compatible UTF-8 BOM while preserving privacy");
        using (MonthlyLedgerForm form = new MonthlyLedgerForm(s))
        {
            form.Opacity = 0; form.Show(); form.Hide(); form.Show(); Application.DoEvents();
            DataGridView grid = (DataGridView)form.Controls.Find("LedgerRows", true)[0];
            check(grid.ReadOnly && grid.Rows.Count == 30, "The actual ledger window displays a read-only calendar table");
            grid.CurrentCell = grid.Rows[16].Cells[0];
            check(form.Controls.Find("LedgerDetails", true)[0].Text.Contains("计薪 0%"), "Selecting a ledger row displays its original leave record");
            using (Bitmap image = new Bitmap(form.Width, form.Height)) { form.DrawToBitmap(image, new Rectangle(0, 0, form.Width, form.Height)); image.Save(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ledger-preview.png")); }
            form.ApplyPrivacy(true);
            check((string)grid.Rows[16].Cells[7].Value == "已隐藏" && !form.Controls.Find("LedgerSummary", true)[0].Text.Contains(".00"), "Privacy immediately masks an already-open ledger table and summary");
            DateTimePicker picker = (DateTimePicker)form.Controls.Find("LedgerMonth", true)[0]; picker.Value = new DateTime(2026, 10, 1);
            check(grid.Rows.Count == 31 && form.Controls.Find("LedgerSummary", true)[0].Text.Contains("2026年10月"), "The month picker refreshes daily rows and summary");
            ((Button)form.Controls.Find("LedgerExport", true)[0]).PerformClick();
            check(form.Controls.Find("LedgerStatus", true)[0].Text.Contains("金额已隐藏"), "The real export action follows the current privacy setting");
            form.Close();
        }
        s.OvertimeEntries[0].StoppedAt = DateTime.MinValue;
        string after;
        using (StringWriter writer = new StringWriter()) { new XmlSerializer(typeof(Settings)).Serialize(writer, s); after = writer.ToString(); }
        check(after == original, "Building, viewing and exporting a ledger never modifies its source configuration");
        Settings ui = s.Copy(); ui.PetEnabled = true; ui.PetWalk = false; ui.PetPose = 0; ui.HoverEnabled = false; ui.AlwaysOnTop = false;
        using (Widget owner = new Widget(ui))
        {
            owner.Show(); Application.DoEvents(); ToolStripMenuItem menu = null;
            foreach (ToolStripItem item in owner.ContextMenuStrip.Items) if (item.Text == "月度明细账") menu = (ToolStripMenuItem)item;
            check(menu != null, "The shared desktop-pet and tray menu exposes the ledger"); menu.PerformClick();
            MonthlyLedgerForm window = null; DesktopPet pet = null;
            foreach (Form form in Application.OpenForms) { if (form is MonthlyLedgerForm) window = (MonthlyLedgerForm)form; if (form is DesktopPet) pet = (DesktopPet)form; }
            // 集成验证窗口不遮挡用户桌面，避免真实点击把测试窗口提前关闭。
            if (window != null) window.Opacity = 0; Application.DoEvents();
            check(window != null && window.Visible && pet != null && pet.Enabled && !pet.Paused, "Opening a ledger keeps native desktop-pet interaction enabled");
            menu.PerformClick(); Application.DoEvents(); int count = 0; foreach (Form form in Application.OpenForms) if (form is MonthlyLedgerForm) count++;
            check(count == 1, "Opening the menu again reuses the existing ledger window (count=" + count + ")");
            owner.TogglePrivacy();
            check((string)((DataGridView)window.Controls.Find("LedgerRows", true)[0]).Rows[0].Cells[7].Value == "已隐藏", "The original privacy-eye action also masks the modeless ledger");
            window.Close(); owner.Hide();
        }
    }
}
