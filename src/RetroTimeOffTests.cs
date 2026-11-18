using System;
public static class RetroTimeOffTests
{
    public static void Run(Action<bool, string> check)
    {
        DateTime day = new DateTime(2026, 11, 3);
        Settings s = new Settings { MonthlySalary = 8800, ManualDays = true, Days = 22, ManualMonth = "2026-11" };
        s.Absences.Add(new AbsenceEntry { From = day.AddHours(9), Until = day.AddHours(12), PayPercent = 0, UseTimeOff = true });
        s.OvertimeEntries.Add(new OvertimeEntry { Start = day.AddHours(18), Minutes = 120, HourlyRate = 50 });
        check(WorkClock.At(s, day.AddHours(18).AddMinutes(29)).Absence.TodayLoss == 150, "Planned overtime and incomplete half-hours cannot retroactively cover leave");
        Snapshot first = WorkClock.At(s, day.AddHours(18.5));
        check(first.Absence.TodayLoss == 125 && first.Absence.MonthRetroMinutes == 30 && first.Overtime.BalanceMinutes == 0, "Each completed half-hour restores exactly half an hour of regular pay");
        Snapshot done = WorkClock.At(s, day.AddHours(20));
        check(done.Absence.TodayLoss == 50 && done.Absence.UsedToday == 120 && done.Overtime.BalanceValue == 0 && done.Earned == 350, "Two overtime hours cover two of three unpaid leave hours without double-paying overtime");
        check(done.MonthEarned - WorkClock.At(s, day.AddHours(18)).MonthEarned == 100, "Retroactive coverage restores monthly regular income as well as daily income");
        Settings excess = s.Copy(); excess.OvertimeEntries[0].Minutes = 240;
        check(OvertimeClock.At(excess, day.AddHours(22)).BalanceMinutes == 60 && OvertimeClock.At(excess, day.AddHours(22)).BalanceValue == 50, "Excess completed overtime remains available with its original money equivalent");
        Settings partial = s.Copy(); partial.Absences[0].PayPercent = 50;
        check(WorkClock.At(partial, day.AddHours(20)).Absence.TodayLoss == 25, "Partial-pay leave restores only the previously unpaid salary fraction");
        Settings excluded = s.Copy(); excluded.Absences[0].UseTimeOff = false;
        check(WorkClock.At(excluded, day.AddHours(20)).Absence.TodayLoss == 150 && OvertimeClock.At(excluded, day.AddHours(20)).BalanceMinutes == 120, "Opted-out leave does not consume later overtime");
        excluded = s.Copy(); excluded.Absences[0].PayPercent = 100;
        check(OvertimeClock.At(excluded, day.AddHours(20)).BalanceMinutes == 120, "Fully paid leave never consumes later overtime for retroactive coverage");
        Settings boundary = s.Copy(); boundary.Absences[0].From = new DateTime(2026, 10, 30, 9, 0, 0); boundary.Absences[0].Until = boundary.Absences[0].From.AddHours(3);
        check(OvertimeClock.At(boundary, day.AddHours(20)).BalanceMinutes == 120 && AbsenceClock.At(boundary, day.AddHours(20)).MonthRetroMinutes == 0, "Overtime cannot retroactively cover a different calendar month's leave");
        Settings earlier = s.Copy(); earlier.Absences.Add(new AbsenceEntry { From = day.AddDays(-1).AddHours(9), Until = day.AddDays(-1).AddHours(10), PayPercent = 0, UseTimeOff = true });
        check(WorkClock.At(earlier, day.AddHours(20)).Absence.TodayLoss == 100 && WorkClock.At(earlier, day.AddHours(20)).Absence.MonthRetroMinutes == 120, "The oldest eligible uncovered leave receives remaining credit first");
        Settings manual = s.Copy(); manual.LeaveEntries.Add(new LeaveEntry { Date = day, Minutes = 60, RegisteredAt = day.AddHours(20) });
        manual.Validate();
        check(WorkClock.At(manual, day.AddHours(20)).Absence.TodayLoss == 100 && OvertimeClock.At(manual, day.AddHours(20)).BalanceMinutes == 0, "Previously recorded manual time-off remains valid and cannot be spent twice");
        check(WorkClock.At(s, day.AddHours(20)).Absence.MonthRetroMinutes == 120 && s.Absences[0].PayPercent == 0, "Recalculation is idempotent and never rewrites the registered leave pay ratio");
    }
}
