using System;
using System.Windows.Forms;
using System.Runtime.InteropServices;
public static class LeaveCorrectionTests
{
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr FindWindow(string className, string title);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr window, out uint process);
    [DllImport("user32.dll")] private static extern bool PostMessage(IntPtr window, uint message, IntPtr parameter, IntPtr data);
    public static void Run(Action<bool, string> check)
    {
        Settings s = new Settings { MonthlySalary = 8800, ManualDays = true, Days = 22, ManualMonth = "2026-03", WorkMode = 0, SingleRest = 0 };
        DateTime first = new DateTime(2026, 3, 2), later = new DateTime(2026, 3, 12);
        s.Absences.Add(new AbsenceEntry { From = first.AddHours(9), Until = first.AddHours(12), PayPercent = 0 });
        s.Absences.Add(new AbsenceEntry { From = later.AddHours(9), Until = later.AddHours(12), PayPercent = 0 });
        s.OvertimeEntries.Add(new OvertimeEntry { Start = new DateTime(2026, 3, 5, 18, 30, 0), Minutes = 60, HourlyRate = 50 });
        s.OvertimeEntries.Add(new OvertimeEntry { Start = new DateTime(2026, 3, 6, 18, 30, 0), Minutes = 60, HourlyRate = 50 });
        s.OvertimeEntries.Add(new OvertimeEntry { Start = new DateTime(2026, 3, 7, 18, 30, 0), Minutes = 120, HourlyRate = 50 });
        LedgerReport beforeLater = MonthlyLedger.Build(s, first, new DateTime(2026, 3, 7, 21, 0, 0));
        check(beforeLater.Rows[1].Settlement.AutoMinutes == 180 && beforeLater.BalanceMinutes == 60, "Four overtime hours settle the older three-hour leave before the later leave occurs");
        LedgerReport afterLater = MonthlyLedger.Build(s, first, later.AddHours(18));
        check(afterLater.Rows[1].Settlement.AutoMinutes == 180 && afterLater.Rows[1].NormalIncome == 400, "A later leave cannot take credit away from an already-covered earlier leave");
        check(afterLater.Rows[11].Settlement.AutoMinutes == 60 && afterLater.Rows[11].Settlement.Loss == 100 && afterLater.Rows[11].NormalIncome == 300, "Only the remaining one hour covers the later leave, leaving two hours unpaid");
        check(afterLater.AutoMinutes == 240 && afterLater.ManualMinutes == 0 && afterLater.BalanceMinutes == 0 && afterLater.LeaveSeconds == 21600, "Six hours of leave minus four overtime hours use exactly four hours without duplicate manual entries");
        check(WorkClock.At(s, later.AddHours(18)).Absence.MonthUncoveredSeconds == 7200 && WorkClock.At(s, later.AddHours(18)).Absence.MonthLoss == 100, "The month deducts exactly two normal hourly wages after automatic settlement");
        s.Absences.Reverse();
        check(MonthlyLedger.Build(s, first, later.AddHours(18)).Rows[1].Settlement.AutoMinutes == 180, "Record insertion order does not change chronological allocation");
        s.Absences.Reverse(); Settings reserved = s.Copy();
        reserved.LeaveEntries.Add(new LeaveEntry { Date = new DateTime(2026, 3, 6), Minutes = 90, RegisteredAt = new DateTime(2026, 3, 6, 19, 10, 0) });
        reserved.LeaveEntries.Add(new LeaveEntry { Date = new DateTime(2026, 3, 7), Minutes = 90, RegisteredAt = new DateTime(2026, 3, 7, 19, 55, 0) });
        reserved.Validate(); Snapshot keepManual = WorkClock.At(reserved, later.AddHours(18));
        check(!keepManual.Overtime.Deficit && keepManual.Overtime.MonthUsedMinutes == 240 && keepManual.Absence.MonthUncoveredSeconds == 18000, "Genuine previously used manual time-off is reserved and remains valid on upgrade");
        Settings rates = s.Copy(); rates.Absences[1].Until = later.AddHours(9.5); rates.OvertimeEntries[1].HourlyRate = 80; rates.OvertimeEntries[2].HourlyRate = 100;
        check(OvertimeClock.At(rates, later.AddHours(18)).BalanceMinutes == 30 && OvertimeClock.At(rates, later.AddHours(18)).BalanceValue == 50, "Chronological automatic settlement keeps the captured FIFO value of remaining credit");
        DateTime next = DateTime.Today.AddDays(1); while (!WorkClock.IsWorkingDay(new Settings(), next)) next = next.AddDays(1);
        DateTime second = next.AddDays(1); while (!WorkClock.IsWorkingDay(new Settings(), second)) second = second.AddDays(1);
        Settings editable = new Settings { MonthlySalary = 8800, PersonalLeavePercent = 100, DailySummaryEnabled = false, PetEnabled = false };
        editable.Absences.Add(new AbsenceEntry { From = next.AddHours(9), Until = next.AddHours(12), PayPercent = 100 });
        editable.Absences.Add(new AbsenceEntry { From = second.AddHours(9), Until = second.AddHours(12), PayPercent = 100 });
        Storage.Save(editable);
        using (AbsenceForm form = new AbsenceForm(editable))
        {
            form.Opacity = 0; form.Show(); Application.DoEvents(); ListBox list = (ListBox)form.Controls["AbsenceEntries"];
            list.SelectedIndex = 0;
            check(form.Controls["AbsencePercent"].Text == "100" && form.Controls["AddAbsence"].Text == "更新选中" && list.Items[0].ToString().Contains("全薪不扣款"), "Selecting an existing full-pay record exposes its saved pay ratio and edit mode clearly");
            form.Controls["AbsencePercent"].Text = "0"; list.SelectedIndex = 1; form.Controls["AbsencePercent"].Text = "0";
            ((Button)form.Controls["SaveAbsence"]).PerformClick();
            Settings saved = Storage.Load();
            check(saved.Absences.Count == 2 && saved.Absences[0].PayPercent == 0 && saved.Absences[1].PayPercent == 0 && saved.PersonalLeavePercent == 0, "Switching records and saving directly preserves both edits and remembers the unpaid personal-leave default");
            check(editable.Absences[0].PayPercent == 100 && editable.PersonalLeavePercent == 100, "Record editing never mutates the source settings before saving");
        }
        Settings empty = new Settings { MonthlySalary = 8800, DailySummaryEnabled = false, PetEnabled = false };
        using (AbsenceForm form = new AbsenceForm(empty))
        {
            form.Opacity = 0; form.Show(); Application.DoEvents();
            ((DateTimePicker)form.Controls["AbsenceFrom"]).Value = next.AddHours(9); ((DateTimePicker)form.Controls["AbsenceUntil"]).Value = next.AddHours(10);
            ((Button)form.Controls["SaveAbsence"]).PerformClick();
            check(Storage.Load().Absences.Count == 1 && Storage.Load().Absences[0].PayPercent == 0, "Direct save commits a newly entered unpaid record even without a separate add click");
        }
        Settings one = Storage.Load();
        using (AbsenceForm form = new AbsenceForm(one))
        {
            form.Opacity = 0; form.Show(); Application.DoEvents(); ((ListBox)form.Controls["AbsenceEntries"]).SelectedIndex = 0;
            ((Button)form.Controls["NewAbsence"]).PerformClick();
            ((DateTimePicker)form.Controls["AbsenceFrom"]).Value = second.AddHours(13); ((DateTimePicker)form.Controls["AbsenceUntil"]).Value = second.AddHours(14);
            ((Button)form.Controls["SaveAbsence"]).PerformClick();
            check(Storage.Load().Absences.Count == 2 && Storage.Load().Absences[0].From == next.AddHours(9), "New-entry action does not overwrite the selected existing record");
        }
        empty.PersonalLeavePercent = 100;
        using (AbsenceForm form = new AbsenceForm(empty))
        using (Timer answer = new Timer { Interval = 25 })
        {
            form.Opacity = 0; form.Show(); Application.DoEvents();
            ((DateTimePicker)form.Controls["AbsenceFrom"]).Value = next.AddHours(9); ((DateTimePicker)form.Controls["AbsenceUntil"]).Value = next.AddHours(10);
            int choice = 7;
            answer.Tick += delegate
            {
                IntPtr dialog = FindWindow("#32770", "确认全薪请假"); uint process;
                if (dialog != IntPtr.Zero && GetWindowThreadProcessId(dialog, out process) != 0 && process == System.Diagnostics.Process.GetCurrentProcess().Id) { PostMessage(dialog, 0x111, new IntPtr(choice), IntPtr.Zero); answer.Stop(); }
            };
            answer.Start(); ((Button)form.Controls["AddAbsence"]).PerformClick();
            check(((ListBox)form.Controls["AbsenceEntries"]).Items.Count == 0, "Declining full-pay confirmation cannot silently register an unintended paid personal leave");
            choice = 6; answer.Start(); ((Button)form.Controls["AddAbsence"]).PerformClick(); ((Button)form.Controls["SaveAbsence"]).PerformClick();
            check(Storage.Load().Absences.Count == 1 && Storage.Load().Absences[0].PayPercent == 100, "An explicitly confirmed full-pay personal leave remains supported");
        }
    }
}
