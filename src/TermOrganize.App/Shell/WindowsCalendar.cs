// Copyright (C) 2026 Term Organize contributors.
// SPDX-License-Identifier: AGPL-3.0-only

using TermOrganize.Core;
using Windows.ApplicationModel.Appointments;

namespace TermOrganize;

/// <summary>
/// Writes a schedule into an app calendar named Term Organize.
/// If the Appointments API refuses, the caller still has the .ics export.
/// </summary>
internal static class WindowsCalendar
{
    public sealed class Result
    {
        public int Added { get; init; }
        public required string Message { get; init; }
        public bool UsedIcsFallback { get; init; }
    }

    public static async Task<Result> AddAsync(Term term, IReadOnlyList<CourseSection> sections, Func<CourseKey, string?>? titleOf)
    {
        var drafts = CalendarEventDrafts.FromSections(term, sections, titleOf);
        if (drafts.Count == 0)
            return new Result { Message = "Those sections have no weekly meeting to put on the calendar." };
        try
        {
            var store = await AppointmentManager.RequestStoreAsync(AppointmentStoreAccessType.AppCalendarsReadWrite);
            AppointmentCalendar? calendar = null;
            foreach (var existing in await store.FindAppointmentCalendarsAsync())
            {
                if (existing.DisplayName == "Term Organize") calendar = existing;
            }
            calendar ??= await store.CreateAppointmentCalendarAsync("Term Organize");
            foreach (var draft in drafts)
            {
                var appointment = new Appointment
                {
                    Subject = draft.Subject,
                    Location = draft.Location,
                    Details = draft.Details,
                    StartTime = draft.Start,
                    Duration = draft.Duration,
                };
                if (draft.Until is DateTimeOffset until && !draft.Days.IsEmpty)
                {
                    appointment.Recurrence = new AppointmentRecurrence
                    {
                        Unit = AppointmentRecurrenceUnit.Weekly,
                        Interval = 1,
                        DaysOfWeek = Days(draft.Days),
                        Until = until,
                    };
                }
                await calendar.SaveAppointmentAsync(appointment);
            }
            var noun = drafts.Count == 1 ? "event" : "events";
            return new Result { Added = drafts.Count, Message = $"Added {drafts.Count} {noun} to the Term Organize calendar." };
        }
        catch (Exception ex)
        {
            return new Result
            {
                UsedIcsFallback = true,
                Message = "Windows Calendar didn\u2019t take the events (" + ex.Message + "). Export .ics and open that file in Calendar.",
            };
        }
    }

    private static AppointmentDaysOfWeek Days(DaySet days)
    {
        AppointmentDaysOfWeek mask = 0;
        if (days.Contains(0)) mask |= AppointmentDaysOfWeek.Sunday;
        if (days.Contains(1)) mask |= AppointmentDaysOfWeek.Monday;
        if (days.Contains(2)) mask |= AppointmentDaysOfWeek.Tuesday;
        if (days.Contains(3)) mask |= AppointmentDaysOfWeek.Wednesday;
        if (days.Contains(4)) mask |= AppointmentDaysOfWeek.Thursday;
        if (days.Contains(5)) mask |= AppointmentDaysOfWeek.Friday;
        if (days.Contains(6)) mask |= AppointmentDaysOfWeek.Saturday;
        return mask;
    }
}
