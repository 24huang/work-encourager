using System;
using System.Globalization;
using System.Windows.Forms;

public static class OvertimeTests
{
    public static void Run(Action<bool, string> check)
    {
        Settings settings = new Settings { MonthlySalary = 8800, ManualDays = true, Days = 22, ManualMonth = "2026-11", DailySummaryEnabled = true, PetEnabled = false };
        DateTime start = new DateTime(2026, 11, 2, 18, 0, 0);
        settings.OvertimeEntries.Add(new OvertimeEntry { Start = start, Minutes = 120, HourlyRate = 50 });
        check(OvertimeClock.At(settings, start.AddMinutes(29).AddSeconds(59)).TodayMinutes == 0, "Overtime under a full half-hour receives no credit");
        OvertimeSnapshot first = OvertimeClock.At(settings, start.AddMinutes(30));
        check(first.TodayMinutes == 30 && first.TodayValue == 25 && first.BalanceMinutes == 30, "One completed half-hour creates time-off credit and its normal-rate equivalent");
        check(OvertimeClock.At(settings, start.AddMinutes(89)).TodayMinutes == 60, "Partial trailing half-hour remains uncredited");
        check(OvertimeClock.At(settings, start.AddHours(2)).BalanceValue == 100 && !OvertimeClock.At(settings, start.AddHours(2)).Active, "Overtime stops accruing at its planned end");
        Settings early = settings.Copy(); early.OvertimeEntries[0].Finished = true; early.OvertimeEntries[0].StoppedAt = start.AddMinutes(47);
        check(OvertimeClock.At(early, start.AddHours(2)).TodayMinutes == 30 && OvertimeClock.At(early, start.AddHours(2)).BalanceValue == 25 && !OvertimeClock.At(early, start.AddHours(1)).Active, "Ending early keeps only completed half-hours and stops reminders");
        check(WorkClock.SummaryDue(early, start.AddMinutes(47)), "Ending early advances the final summary to the actual finish");
        DateTime nextDay = start.Date.AddDays(1).AddHours(12);
        settings.LeaveEntries.Add(new LeaveEntry { Date = nextDay.Date, Minutes = 60 });
        OvertimeSnapshot used = OvertimeClock.At(settings, nextDay);
        check(used.BalanceMinutes == 60 && used.BalanceValue == 50 && used.TodayMinutes == 0, "Using one hour from two hours at fifty per hour leaves one hour and fifty");
        settings.OvertimeEntries.Insert(0, new OvertimeEntry { Start = start.AddDays(1), Minutes = 120, HourlyRate = 80 });
        OvertimeSnapshot fifo = OvertimeClock.At(settings, start.AddDays(2));
        check(fifo.BalanceMinutes == 180 && fifo.BalanceValue == 210, "FIFO consumes the earliest credit even when records have different hourly rates and insertion order");
        settings.LeaveEntries.Add(new LeaveEntry { Date = start.Date.AddDays(2), Minutes = 90 });
        check(OvertimeClock.At(settings, start.AddDays(2)).BalanceMinutes == 90 && OvertimeClock.At(settings, start.AddDays(2)).BalanceValue == 120, "A leave entry can cross credit batches at their recorded rates");
        settings.Validate(); Settings restored = settings.Copy(); restored.OvertimeEntries[0].Minutes = 30; restored.LeaveEntries[0].Minutes = 30;
        check(settings.OvertimeEntries[0].Minutes == 120 && settings.LeaveEntries[0].Minutes == 60, "Editing a draft never mutates original overtime or leave records");
        Storage.Save(settings); Settings loaded = Storage.Load();
        check(OvertimeClock.At(loaded, start.AddDays(2)).BalanceValue == 120 && loaded.OvertimeEntries.Count == 2, "Overtime, leave, rates and remaining value survive restart");
        loaded.MonthlySalary = 9900;
        check(OvertimeClock.At(loaded, start.AddDays(2)).BalanceValue == 120, "Changing normal salary does not reprice historical overtime credit");
        bool rejected = false; Settings invalid = settings.Copy(); invalid.LeaveEntries.Add(new LeaveEntry { Date = start.Date.AddDays(2), Minutes = 120 });
        try { OvertimeClock.Validate(invalid, start.AddDays(2)); } catch { rejected = true; }
        check(rejected, "Leave cannot overdraw the available overtime balance");
        invalid = settings.Copy(); invalid.LeaveEntries.Clear(); invalid.LeaveEntries.Add(new LeaveEntry { Date = start.Date.AddDays(-1), Minutes = 30 }); rejected = false;
        try { OvertimeClock.Validate(invalid, start.AddDays(2)); } catch { rejected = true; }
        check(rejected, "Leave cannot spend credit before the overtime date");
        invalid = settings.Copy(); invalid.OvertimeEntries.Add(new OvertimeEntry { Start = start.AddMinutes(30), Minutes = 30, HourlyRate = 50 }); rejected = false;
        try { OvertimeClock.Validate(invalid, start.AddDays(2)); } catch { rejected = true; }
        check(rejected, "Overlapping overtime periods are rejected");
        invalid = new Settings(); invalid.OvertimeEntries.Add(new OvertimeEntry { Start = start.Date.AddHours(23), Minutes = 60, HourlyRate = 50 }); rejected = false;
        try { OvertimeClock.Validate(invalid, start.AddDays(2)); } catch { rejected = true; }
        check(rejected, "This version rejects overtime crossing midnight");
        Settings schedule = new Settings { MonthlySalary = 8800, ManualDays = true, Days = 22, ManualMonth = "2026-11", RemindersEnabled = true };
        schedule.OvertimeEntries.Add(new OvertimeEntry { Start = start, Minutes = 120, HourlyRate = 50 });
        schedule.OvertimeEntries.Add(new OvertimeEntry { Start = start.AddHours(3), Minutes = 60, HourlyRate = 50 });
        check(!WorkClock.SummaryDue(schedule, start) && !WorkClock.SummaryDue(schedule, start.AddHours(2)) && WorkClock.SummaryDue(schedule, start.AddHours(4)), "Daily summary waits until today's last overtime segment finishes");
        schedule.LastSummaryDate = start.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture); schedule.LastSummaryEnd = WorkClock.SummaryEnd(schedule, start).ToString("s", CultureInfo.InvariantCulture);
        check(!WorkClock.SummaryDue(schedule, start.AddHours(4).AddMinutes(1)), "Overtime summary deduplication survives its saved end marker");
        schedule.OvertimeEntries[1].Minutes = 30;
        check(!WorkClock.SummaryDue(schedule, start.AddHours(4)), "Shortening a summarized plan does not send another summary");
        schedule.OvertimeEntries[1].Minutes = 90;
        check(!WorkClock.SummaryDue(schedule, start.AddHours(4)) && WorkClock.SummaryDue(schedule, start.AddHours(4.5)), "Extending already-summarized overtime schedules one updated summary at the new end");
        Snapshot normal = WorkClock.At(schedule, start.AddHours(4.5));
        check(normal.Earned == 400 && normal.MonthEarned == 400, "Overtime and leave never merge into normal daily or monthly earnings");
        ReminderClock reminders = new ReminderClock(start.AddMinutes(-30));
        check(reminders.Poll(schedule, start) != null && reminders.Poll(schedule, start.AddMinutes(30)) != null, "Health reminders continue across regular clock-out into overtime");
        check(reminders.Poll(schedule, start.AddHours(2.5)) == null, "Health reminders pause during a gap between overtime segments");
        reminders.Poll(schedule, start.AddHours(3));
        check(reminders.Poll(schedule, start.AddHours(3.5)) != null, "Health reminders resume inside a later overtime segment");
        check(reminders.Poll(schedule, start.AddHours(4.5)) == null, "Health reminders stop when overtime ends");
        schedule.PrivacyMode = true; schedule.HoverVisible = "overtime";
        check(!Widget.BuildHoverMessage(schedule, start.AddHours(4)).Contains("¥") && Widget.BuildHoverMessage(schedule, start.AddHours(4)).Contains("隐藏"), "Overtime hover and balance values follow privacy mode");
        check(!Widget.SummaryMessage(schedule, start.AddHours(4)).Contains("¥"), "Overtime summary hides every amount in privacy mode");
        Settings ui = new Settings { MonthlySalary = 8800, ManualDays = true, Days = 22, PrivacyMode = true, DailySummaryEnabled = false, PetEnabled = false, WorkMode = 0, SingleRest = ((int)DateTime.Today.DayOfWeek + 1) % 7 };
        ui.OvertimeEntries.Add(new OvertimeEntry { Start = DateTime.Today.AddDays(-1).AddHours(18), Minutes = 120, HourlyRate = 50 });
        Storage.Save(ui);
        using (OvertimeForm form = new OvertimeForm(ui))
        {
            form.Opacity = 0; form.Show(); Application.DoEvents();
            ((NumericUpDown)form.Controls["OvertimeDuration"]).Value = 90;
            ((Button)form.Controls["AddOvertime"]).PerformClick();
            check(((ListBox)form.Controls["OvertimeEntries"]).Items.Count == 2, "The real form adds a today-only overtime segment from a preset duration");
            ((Button)form.Controls["UseLeave"]).PerformClick();
            check(((ListBox)form.Controls["LeaveEntries"]).Items.Count == 1 && !form.Controls["OvertimeBalance"].Text.Contains("¥"), "The real form records leave and masks balance values");
            ((Button)form.AcceptButton).PerformClick();
            check(Storage.Load().OvertimeEntries.Count == 2 && Storage.Load().LeaveEntries.Count == 1 && Storage.Load().OvertimePreset == 90, "Overtime form persists planned periods, used leave and the duration preset");
        }
        using (OvertimeForm canceled = new OvertimeForm(ui))
        {
            canceled.Opacity = 0; canceled.Show(); Application.DoEvents(); ((Button)canceled.Controls["AddOvertime"]).PerformClick(); canceled.DialogResult = DialogResult.Cancel; canceled.Close();
            check(ui.OvertimeEntries.Count == 1, "Canceling overtime edits leaves original data untouched");
        }
    }
}
