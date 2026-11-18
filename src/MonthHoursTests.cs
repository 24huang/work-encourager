using System;
using System.IO;
using System.Xml.Serialization;
public static class MonthHoursTests
{
    public static void Run(Action<bool, string> check)
    {
        DateTime day = new DateTime(2026, 11, 18);
        Settings s = new Settings { MonthlySalary = 8800, ManualDays = true, Days = 22, ManualMonth = "2026-11" };
        s.Absences.Add(new AbsenceEntry { From = day.AddDays(-1).AddHours(9), Until = day.AddDays(-1).AddHours(12), PayPercent = 0, UseTimeOff = true });
        s.OvertimeEntries.Add(new OvertimeEntry { Start = day.AddHours(18), Minutes = 60, HourlyRate = 50 });
        Snapshot value = WorkClock.At(s, day.AddHours(19));
        check(value.Absence.MonthElapsedSeconds == 10800 && value.Overtime.MonthMinutes == 60 && value.Overtime.MonthUsedMinutes == 60, "Monthly totals show yesterday's three-hour leave and today's one-hour overtime and time-off");
        check(value.Absence.MonthUncoveredSeconds == 7200 && value.Absence.MonthLoss == 100 && value.Overtime.BalanceMinutes == 0, "One-to-one monthly settlement leaves two hours and their normal pay deduction");
        check(value.Absence.TodaySeconds == 0 && value.Absence.MonthElapsedSeconds > 0, "A day without leave still shows the earlier leave in monthly totals");
        value = WorkClock.At(s, day.AddHours(18).AddMinutes(29));
        check(value.Overtime.MonthMinutes == 0 && value.Overtime.MonthUsedMinutes == 0, "Monthly overtime and time-off exclude an unfinished half-hour");
        Settings combined = s.Copy(); combined.OvertimeEntries.Add(new OvertimeEntry { Start = day.AddDays(-2).AddHours(18), Minutes = 60, HourlyRate = 50 });
        value = WorkClock.At(combined, day.AddHours(19));
        check(value.Overtime.MonthUsedMinutes == 120 && value.Absence.MonthRetroMinutes == 60 && value.Absence.MonthUncoveredSeconds == 3600, "On-time use and retroactive use are combined once, without double-counting retroactive time-off");
        Settings manual = new Settings { MonthlySalary = 8800 };
        manual.OvertimeEntries.Add(new OvertimeEntry { Start = day.AddDays(-2).AddHours(18), Minutes = 120, HourlyRate = 50 });
        manual.LeaveEntries.Add(new LeaveEntry { Date = day.Date, Minutes = 30 });
        check(OvertimeClock.At(manual, day.AddHours(19)).MonthUsedMinutes == 30, "Monthly manual time-off is counted even when there are no ordinary leave records");
        manual.Absences.Add(new AbsenceEntry { From = day.AddDays(-1).AddHours(11), Until = day.AddDays(-1).AddHours(14), PayPercent = 100, UseTimeOff = false });
        value = WorkClock.At(manual, day.AddHours(19));
        check(value.Absence.MonthElapsedSeconds == 7200 && value.Overtime.MonthUsedMinutes == 30 && value.Absence.MonthUncoveredSeconds == 0, "Monthly leave excludes lunch, includes paid leave, and does not mark paid leave as uncovered");
        manual.Absences.Add(new AbsenceEntry { From = day.AddDays(1).AddHours(9), Until = day.AddDays(1).AddHours(12), PayPercent = 0 });
        check(WorkClock.At(manual, day.AddHours(19)).Absence.MonthElapsedSeconds == 7200, "Future scheduled leave is excluded from elapsed monthly leave");
        manual.Absences.Add(new AbsenceEntry { From = new DateTime(2026, 10, 30, 9, 0, 0), Until = new DateTime(2026, 10, 30, 12, 0, 0), PayPercent = 0 });
        manual.OvertimeEntries.Add(new OvertimeEntry { Start = new DateTime(2025, 11, 18, 18, 0, 0), Minutes = 60, HourlyRate = 50 });
        value = WorkClock.At(manual, day.AddHours(19));
        check(value.Absence.MonthElapsedSeconds == 7200 && value.Overtime.MonthMinutes == 120, "Monthly totals exclude other months and the same month in another year");
        s.HoverVisible = "hours"; s.PrivacyMode = true;
        string message = Widget.BuildHoverMessage(s, day.AddHours(19));
        check(message.Contains("本月请假：3") && message.Contains("本月已用调休：1") && !message.Contains("¥"), "Monthly-hours hover reports hours without exposing salary amounts");
        Settings old = new Settings(); old.DetailOrder = "overtime,absence,income,rate,progress,countdown,encouragement,month"; old.DetailVisible = "overtime,absence";
        using (FileStream stream = File.Create(Storage.PathName)) new XmlSerializer(typeof(Settings)).Serialize(stream, old);
        Settings upgraded = Storage.Load();
        check(upgraded.DetailOrder == "hours," + old.DetailOrder && upgraded.DetailVisible == "hours,overtime,absence", "Upgrade puts the new monthly-hours card first while retaining existing cards' relative order");
        Storage.Save(upgraded); check(Storage.Load().DetailVisible == upgraded.DetailVisible, "Monthly-hours migration is idempotent after saving");
    }
}
