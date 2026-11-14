using System;
using System.Windows.Forms;

public static class AbsenceTests
{
    public static void Run(Action<bool, string> check)
    {
        DateTime day = new DateTime(2026, 11, 3);
        Settings basic = new Settings { MonthlySalary = 8800, ManualDays = true, Days = 22, ManualMonth = "2026-11", PetEnabled = false, DailySummaryEnabled = false };
        Settings unpaid = basic.Copy();
        unpaid.Absences.Add(new AbsenceEntry { From = day.AddHours(9), Until = day.AddHours(11), Kind = 0, PayPercent = 0, UseTimeOff = false });
        unpaid.Validate();
        Snapshot value = WorkClock.At(unpaid, day.AddHours(18));
        check(value.Earned == 300 && value.Absence.TodayLoss == 100 && value.MonthEarned == 700, "Two unpaid hours deduct normal daily and monthly income once");
        check(WorkClock.At(unpaid, day.AddHours(9.5)).Earned == 0 && WorkClock.At(unpaid, day.AddHours(9.5)).PerSecond == 0, "Unpaid leave pauses income accrual only while active");
        check(value.Progress == 1 && value.Remaining == 0, "Leave preserves the original scheduled-day progress and countdown");
        check(WorkClock.At(unpaid, day.AddHours(8)).Absence.TodayLoss == 0 && WorkClock.At(unpaid, day.AddHours(8)).Earned == 0, "Future leave does not deduct income before it occurs");
        check(WorkClock.At(unpaid, new DateTime(2026, 11, 30, 19, 0, 0)).MonthEarned == 8300, "Leave loss remains visible with a manual monthly divisor");
        Settings capped = unpaid.Copy(); capped.Days = 20;
        check(WorkClock.At(capped, new DateTime(2026, 11, 30, 19, 0, 0)).MonthEarned == 8690, "Monthly leave deduction is applied after the salary cap");
        Settings paid = unpaid.Copy(); paid.Absences[0].Kind = 2; paid.Absences[0].PayPercent = 100;
        check(WorkClock.At(paid, day.AddHours(18)).Earned == 400 && WorkClock.At(paid, day.AddHours(10)).Status == "年假中", "Paid annual leave keeps income and reports the active leave type");
        Settings sick = unpaid.Copy(); sick.Absences[0].Kind = 1; sick.Absences[0].PayPercent = 50; sick.SickLeavePercent = 50;
        check(WorkClock.At(sick, day.AddHours(18)).Earned == 350 && WorkClock.At(sick, day.AddHours(10)).PerSecond == WorkClock.At(basic, day.AddHours(10)).PerSecond / 2, "Custom sick-leave percentage applies to elapsed leave and the live rate");
        Settings lunch = basic.Copy(); lunch.Absences.Add(new AbsenceEntry { From = day.AddHours(11), Until = day.AddHours(14), PayPercent = 0, UseTimeOff = false });
        check(AbsenceClock.At(lunch, day.AddHours(18)).TodaySeconds == 7200 && AbsenceClock.At(lunch, day.AddHours(18)).TodayLoss == 100 && !AbsenceClock.At(lunch, day.AddHours(12.5)).Active, "Lunch is excluded from leave duration, money and active status");
        Settings multi = basic.Copy(); multi.Absences.Add(new AbsenceEntry { From = new DateTime(2026, 11, 6, 9, 0, 0), Until = new DateTime(2026, 11, 9, 18, 0, 0), PayPercent = 0, UseTimeOff = false });
        multi.Validate();
        check(AbsenceClock.At(multi, new DateTime(2026, 11, 9, 18, 0, 0)).MonthLoss == 800 && AbsenceClock.At(multi, new DateTime(2026, 11, 7, 12, 0, 0)).TodaySeconds == 0, "Multi-day leave excludes weekend rest days");
        Settings crossing = basic.Copy(); crossing.Absences.Add(new AbsenceEntry { From = new DateTime(2026, 10, 30, 9, 0, 0), Until = new DateTime(2026, 11, 2, 18, 0, 0), PayPercent = 0, UseTimeOff = false });
        check(AbsenceClock.At(crossing, day).MonthLoss == 400, "Cross-month leave only deducts this month's effective working time");
        Settings bank = unpaid.Copy(); bank.Absences[0].UseTimeOff = true;
        bank.OvertimeEntries.Add(new OvertimeEntry { Start = day.AddDays(-1).AddHours(18), Minutes = 60, HourlyRate = 50 });
        check(OvertimeClock.At(bank, day.AddHours(8)).BalanceMinutes == 60 && AbsenceClock.At(bank, day.AddHours(8)).UsedToday == 0, "A scheduled future absence does not reserve time-off credit");
        check(OvertimeClock.At(bank, day.AddHours(9)).BalanceMinutes == 30 && AbsenceClock.At(bank, day.AddHours(9)).UsedToday == 30, "A leave block uses one half-hour of available credit at its start, one to one");
        value = WorkClock.At(bank, day.AddHours(18));
        check(value.Absence.UsedToday == 60 && value.Absence.TodayLoss == 50 && value.Earned == 350 && value.Overtime.BalanceMinutes == 0, "Insufficient time-off covers available whole blocks and applies leave pay to the remainder");
        bank.OvertimeEntries.Add(new OvertimeEntry { Start = day.AddHours(18), Minutes = 60, HourlyRate = 80 });
        value = WorkClock.At(bank, day.AddHours(20));
        check(value.Absence.TodayLoss == 50 && value.Overtime.BalanceMinutes == 60 && value.Overtime.BalanceValue == 80, "Later overtime cannot retroactively pay earlier leave");
        check(AbsenceClock.At(bank, day.AddHours(10)).ActivePayPercent == 0, "A depleted bank falls back to the record's pay percentage");
        Settings halfPaid = bank.Copy(); halfPaid.Absences[0].PayPercent = 50;
        check(WorkClock.At(halfPaid, day.AddHours(20)).Earned == 375, "The uncovered portion uses a custom percentage instead of assuming unpaid leave");
        Settings fifo = unpaid.Copy(); fifo.Absences[0].UseTimeOff = true;
        fifo.OvertimeEntries.Add(new OvertimeEntry { Start = day.AddDays(-1).AddHours(20), Minutes = 120, HourlyRate = 80 });
        fifo.OvertimeEntries.Add(new OvertimeEntry { Start = day.AddDays(-1).AddHours(18), Minutes = 60, HourlyRate = 50 });
        value = WorkClock.At(fifo, day.AddHours(18));
        check(value.Earned == 400 && value.Overtime.BalanceMinutes == 60 && value.Overtime.BalanceValue == 80, "Automatic leave uses the oldest credit and preserves later lots' recorded rates");
        Settings manual = fifo.Copy(); manual.LeaveEntries.Add(new LeaveEntry { Date = day, RegisteredAt = day.AddHours(8), Minutes = 30 });
        check(OvertimeClock.At(manual, day.AddHours(18)).BalanceValue == 40 && AbsenceClock.At(manual, day.AddHours(18)).UsedToday == 120, "Manual usage and automatic leave share a single chronological FIFO bank");
        check(OvertimeClock.At(manual, day.AddHours(7)).BalanceMinutes == 180, "Today's manually used time-off is not spent before its registration time");
        Settings legacy = fifo.Copy(); legacy.LeaveEntries.Add(new LeaveEntry { Date = day, Minutes = 30 });
        check(OvertimeClock.At(legacy, day.AddHours(18)).BalanceValue == 40, "Legacy date-only manual time-off entries remain compatible with automatic usage");
        Settings fractional = basic.Copy(); fractional.Start = "09:10"; fractional.Absences.Add(new AbsenceEntry { From = day.AddHours(9).AddMinutes(10), Until = day.AddHours(18), PayPercent = 0, UseTimeOff = true });
        fractional.OvertimeEntries.Add(new OvertimeEntry { Start = day.AddDays(-1).AddHours(18), Minutes = 480 / 2, HourlyRate = 50 });
        fractional.Validate();
        check(AbsenceClock.At(fractional, day.AddHours(18)).UsedToday == 240 && AbsenceClock.At(fractional, day.AddHours(18)).TodaySeconds == 28200, "Full-day leave follows custom shift minutes and never spends fractional time-off");
        Settings invalid = unpaid.Copy(); invalid.Absences[0].Until = invalid.Absences[0].From.AddMinutes(45); bool rejected = false;
        try { invalid.Validate(); } catch { rejected = true; }
        check(rejected, "A forty-five-minute registration is rejected with the half-hour rule");
        invalid = unpaid.Copy(); invalid.Absences.Add(unpaid.Absences[0].Copy()); rejected = false;
        try { invalid.Validate(); } catch { rejected = true; }
        check(rejected, "Overlapping leave cannot double-deduct income or time-off");
        invalid = unpaid.Copy(); invalid.Absences[0].From = day.AddHours(12); invalid.Absences[0].Until = day.AddHours(13); rejected = false;
        try { invalid.Validate(); } catch { rejected = true; }
        check(rejected, "A leave record entirely in lunch time is rejected");
        invalid = unpaid.Copy(); invalid.Absences[0].PayPercent = 101; rejected = false;
        try { invalid.Validate(); } catch { rejected = true; }
        check(rejected, "Leave pay percentages outside zero to one hundred are rejected");
        Settings clone = unpaid.Copy(); clone.Absences[0].PayPercent = 100;
        check(unpaid.Absences[0].PayPercent == 0 && basic.SickLeavePercent == -1, "Leave drafts are independent and sick pay has no invented default");
        Storage.Save(bank); Settings loaded = Storage.Load();
        check(loaded.Absences.Count == 1 && WorkClock.At(loaded, day.AddHours(20)).Earned == 350 && loaded.Absences[0].UseTimeOff, "Leave periods and automatic consumption reproduce after configuration reload");
        Settings seven = basic.Copy(); seven.DetailOrder = "overtime,month,income,rate,progress,countdown,encouragement"; seven.DetailVisible = "month,progress";
        // Save directly to emulate the preceding release, whose seven-component schema predates this validator.
        using (System.IO.FileStream stream = System.IO.File.Create(Storage.PathName)) new System.Xml.Serialization.XmlSerializer(typeof(Settings)).Serialize(stream, seven);
        loaded = Storage.Load();
        check(loaded.DetailOrder == seven.DetailOrder + ",absence" && loaded.DetailVisible == seven.DetailVisible && loaded.Absences.Count == 0, "Upgrading the seven-component release preserves hidden fields and appends the leave component");
        paid.RemindersEnabled = true; ReminderClock reminders = new ReminderClock(day.AddHours(8.5));
        check(reminders.Poll(paid, day.AddHours(10)) == null && reminders.Poll(paid, day.AddHours(11)) == null, "Health reminders stay silent throughout paid leave without accumulating its time");
        check(reminders.Poll(paid, day.AddHours(11.5)) != null, "Health reminders resume after thirty minutes of actual work following leave");
        Settings partial = unpaid.Copy(); partial.Absences[0].From = day.AddHours(9).AddMinutes(20); partial.Absences[0].Until = day.AddHours(9).AddMinutes(50); partial.RemindersEnabled = true;
        reminders = new ReminderClock(day.AddHours(9));
        check(reminders.Poll(partial, day.AddHours(9).AddMinutes(25)) == null && reminders.Poll(partial, day.AddHours(10)) != null, "Reminder timing retains twenty pre-leave minutes and adds ten after leave");
        bank.PrivacyMode = true; bank.HoverVisible = "absence";
        check(!Widget.BuildHoverMessage(bank, day.AddHours(18)).Contains("¥") && Widget.BuildHoverMessage(bank, day.AddHours(18)).Contains("隐藏"), "Leave hover deductions follow privacy mode");
        check(Widget.SummaryMessage(bank, day.AddHours(20)).Contains("请假") && !Widget.SummaryMessage(bank, day.AddHours(20)).Contains("¥"), "Daily summary includes leave while masking every monetary amount");
        // Exercise the real dialog using a future workday, without touching the user's runtime configuration.
        DateTime next = DateTime.Today.AddDays(1); while (!WorkClock.IsWorkingDay(basic, next)) next = next.AddDays(1);
        Storage.Save(basic);
        using (AbsenceForm form = new AbsenceForm(basic))
        {
            form.Opacity = 0; form.Show(); Application.DoEvents();
            ((ComboBox)form.Controls["AbsenceKind"]).SelectedIndex = 1;
            check(form.Controls["AbsencePercent"].Text == "", "The real leave dialog requires an explicit first sick-pay percentage");
            form.Controls["AbsencePercent"].Text = "60";
            ((DateTimePicker)form.Controls["AbsenceFrom"]).Value = next.AddHours(9);
            ((DateTimePicker)form.Controls["AbsenceUntil"]).Value = next.AddHours(11);
            ((Button)form.Controls["AddAbsence"]).PerformClick();
            check(((ListBox)form.Controls["AbsenceEntries"]).Items.Count == 1 && basic.Absences.Count == 0, "Adding a valid future leave modifies only the dialog draft");
            form.ApplyPrivacy(true);
            check(!form.Controls["AbsenceInfo"].Text.Contains("¥"), "An already-open leave dialog immediately masks its deduction amount");
            ((Button)form.AcceptButton).PerformClick(); loaded = Storage.Load();
            check(loaded.Absences.Count == 1 && loaded.SickLeavePercent == 60 && loaded.Absences[0].PayPercent == 60, "The real dialog persists per-record pay and the selected type's default");
        }
        using (AbsenceForm canceled = new AbsenceForm(loaded))
        {
            canceled.Opacity = 0; canceled.Show(); Application.DoEvents();
            ((ListBox)canceled.Controls["AbsenceEntries"]).SelectedIndex = 0;
            ((Button)canceled.Controls["RemoveAbsence"]).PerformClick(); canceled.DialogResult = DialogResult.Cancel; canceled.Close();
            check(loaded.Absences.Count == 1 && Storage.Load().Absences.Count == 1, "Canceling leave deletions preserves original and persisted records");
        }
        Settings latest = loaded.Copy(); latest.PrivacyMode = true; latest.LastSummaryDate = "2026-11-03";
        using (AbsenceForm merged = new AbsenceForm(loaded, delegate { return latest; }))
        {
            merged.Opacity = 0; merged.Show(); Application.DoEvents(); ((Button)merged.AcceptButton).PerformClick();
            check(Storage.Load().PrivacyMode && Storage.Load().LastSummaryDate == "2026-11-03", "Saving leave preserves privacy and summary changes made while the dialog was open");
        }
    }
}
