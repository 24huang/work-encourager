using System;
public static class TodayIncomeTests
{
    public static void Run(Action<bool, string> check)
    {
        DateTime day = new DateTime(2026, 11, 18);
        Settings s = new Settings { MonthlySalary = 8800, ManualDays = true, Days = 22, ManualMonth = "2026-11" };
        s.OvertimeEntries.Add(new OvertimeEntry { Start = day.AddHours(18), Minutes = 90, HourlyRate = 50 });
        check(WorkClock.At(s, day.AddHours(18).AddMinutes(29)).Earned == 400, "Today does not include unfinished overtime half-hours");
        Snapshot first = WorkClock.At(s, day.AddHours(18.5));
        check(first.NormalEarned == 400 && first.Earned == 425 && first.Overtime.TodayAvailableValue == 25, "The first completed overtime half-hour is included in today's total");
        Snapshot full = WorkClock.At(s, day.AddHours(19.5));
        check(full.Earned == 475 && full.MonthEarned == 5200, "Today includes unused overtime and the existing regular-pay monthly total stays unchanged");
        Settings todayLeave = s.Copy(); todayLeave.Absences.Add(new AbsenceEntry { From = day.AddHours(9), Until = day.AddHours(10), PayPercent = 0, UseTimeOff = true });
        Snapshot used = WorkClock.At(todayLeave, day.AddHours(19.5));
        check(used.NormalEarned == 400 && used.Overtime.TodayAvailableValue == 25 && used.Earned == 425, "One hour of time-off restores normal pay and leaves only the unused half-hour in today's overtime income");
        Settings previousLeave = s.Copy(); previousLeave.Absences.Add(new AbsenceEntry { From = day.AddDays(-1).AddHours(9), Until = day.AddDays(-1).AddHours(10), PayPercent = 0, UseTimeOff = true });
        Snapshot retro = WorkClock.At(previousLeave, day.AddHours(19.5));
        check(retro.Earned == 425 && retro.Absence.MonthLoss == 0 && retro.Overtime.TodayAvailableValue == 25, "Covering yesterday's leave cannot also count its consumed overtime as today's extra income");
        Settings history = new Settings { MonthlySalary = 8800, ManualDays = true, Days = 22, ManualMonth = "2026-11" };
        history.OvertimeEntries.Add(new OvertimeEntry { Start = day.AddDays(-1).AddHours(18), Minutes = 60, HourlyRate = 50 });
        check(WorkClock.At(history, day.AddHours(19.5)).Earned == 400 && OvertimeClock.At(history, day.AddHours(19.5)).BalanceValue == 50, "Unused historical overtime remains available but is not counted again as today's income");
        Settings fifo = s.Copy(); fifo.OvertimeEntries[0].HourlyRate = 80;
        fifo.OvertimeEntries.Add(history.OvertimeEntries[0].Copy()); fifo.LeaveEntries.Add(new LeaveEntry { Date = day, Minutes = 90 });
        check(WorkClock.At(fifo, day.AddHours(19.5)).Earned == 480 && OvertimeClock.At(fifo, day.AddHours(19.5)).TodayAvailableValue == 80, "FIFO manual usage removes old credits before today's credits and preserves each recorded hourly rate");
        Settings stopped = s.Copy(); stopped.OvertimeEntries[0].Finished = true; stopped.OvertimeEntries[0].StoppedAt = day.AddHours(18).AddMinutes(47);
        check(WorkClock.At(stopped, day.AddHours(20)).Earned == 425, "Ending overtime early includes only completed half-hours in today's income");
        Storage.Save(previousLeave); Snapshot reloaded = WorkClock.At(Storage.Load(), day.AddHours(19.5));
        check(reloaded.Earned == 425 && reloaded.Overtime.TodayAvailableValue == 25, "Reloading reproduces combined income without duplicating already-used overtime");
        check(Widget.SummaryMessage(s, day.AddHours(19.5)).Contains("475.00") && Widget.SummaryMessage(s, day.AddHours(19.5)).Contains("已计入"), "Clock-out summary reports combined income and labels overtime as already included");
        s.PrivacyMode = true; s.HoverVisible = "income";
        check(!Widget.SummaryMessage(s, day.AddHours(19.5)).Contains("¥") && !Widget.BuildHoverMessage(s, day.AddHours(19.5)).Contains("475.00"), "Combined income and its overtime detail remain masked in privacy mode");
    }
}
