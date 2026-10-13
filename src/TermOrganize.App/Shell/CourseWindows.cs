// Copyright (C) 2026 Term Organize contributors.
// SPDX-License-Identifier: AGPL-3.0-only

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using TermOrganize.Core;
using Windows.System;

namespace TermOrganize;

/// <summary>The course page the Mac opens in its own window, also used as the search preview.</summary>
internal static class CourseDetail
{
    private static readonly HashSet<string> Busy = new(StringComparer.Ordinal);

    public static void Show(StackPanel detail, AppSession session, CourseKey key, bool inWindow, bool allowLiveRefresh = true)
    {
        var index = session.Index;
        var course = index?.Course(key);
        if (index is null || course is null)
        {
            Chrome.Fill(detail, key.ToString(), "That course is not in this term\u2019s catalog.");
            return;
        }
        session.Store.RecordView(key, index.Term.Code);
        var stats = index.Stats(key);
        detail.Children.Clear();
        detail.Children.Add(Chrome.Heading($"{course.Code}  {course.Title}"));
        detail.Children.Add(Chrome.Body($"{course.CreditsLabel} \u00b7 {stats.AvailabilityLabel} \u00b7 {stats.SeatsRemaining} seats left"));
        if (!string.IsNullOrWhiteSpace(course.Description))
            detail.Children.Add(Chrome.Body(course.Description));
        if (RequisiteTreeView.Build(course.Prereqs, session, "Prerequisites") is UIElement prereq)
            detail.Children.Add(prereq);
        if (RequisiteTreeView.Build(course.Coreqs, session, "Corequisites") is UIElement coreq)
            detail.Children.Add(coreq);
        var standing = CourseDegreePlanStandingLookup.For(session.Store, key);
        if (standing is not null) detail.Children.Add(Chrome.Body(standing.Text));

        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        var save = new Button { Content = session.Store.IsSaved(key, index.Term.Code) ? "Saved" : "Save" };
        save.Click += (_, _) =>
        {
            session.Store.ToggleSaved(key, index.Term.Code);
            save.Content = session.Store.IsSaved(key, index.Term.Code) ? "Saved" : "Save";
        };
        var add = new Button { Content = "Add to schedule" };
        add.Click += (_, _) =>
        {
            var plan = session.Store.PrimaryPlan(index.Term.Code) ?? session.Store.CreatePlan("Schedule", index.Term.Code);
            session.Store.AddCourse(key, plan.Id);
        };
        var refresh = new Button { Content = "Refresh seats" };
        refresh.Click += async (_, _) => await RefreshAsync(detail, session, course, index.Term.Code, inWindow);
        actions.Children.Add(save);
        actions.Children.Add(add);
        actions.Children.Add(refresh);
        if (!inWindow)
        {
            var open = new Button { Content = "Open in a window" };
            open.Click += (_, _) => CourseWindow.Open(session, key);
            actions.Children.Add(open);
        }
        detail.Children.Add(actions);
        foreach (var section in index.Sections(key))
            detail.Children.Add(SectionCard(session, section, course.Title, index.Term.Code));
        if (allowLiveRefresh && session.Store.Preferences.AutoRefreshSeats)
            _ = RefreshAsync(detail, session, course, index.Term.Code, inWindow);
    }

    private static async Task RefreshAsync(StackPanel detail, AppSession session, Course course, string term, bool inWindow)
    {
        var token = term + "|" + course.Key;
        if (!Busy.Add(token)) return;
        try
        {
            var live = await BannerClient.Shared.FetchSectionsAsync(term, [course.Key]);
            if (live.TryGetValue(course.Key, out var banner))
            {
                session.RefreshCourse(course.Key, banner);
                Show(detail, session, course.Key, inWindow, allowLiveRefresh: false);
            }
        }
        catch (Exception ex)
        {
            detail.Children.Insert(0, Chrome.Body("Banner didn\u2019t answer: " + ex.Message));
        }
        finally
        {
            Busy.Remove(token);
        }
    }

    private static Border SectionCard(AppSession session, CourseSection section, string title, string term)
    {
        var panel = new StackPanel { Spacing = 4 };
        panel.Children.Add(new TextBlock { Text = $"{section.ClassType} {section.SectionNumber} \u00b7 CRN {section.Crn}", FontWeight = Microsoft.UI.Text.FontWeights.SemiBold });
        panel.Children.Add(Chrome.Body($"{section.ScheduleSummary} \u00b7 {section.InstructorLabel} \u00b7 {section.Campus}"));
        panel.Children.Add(Chrome.Body($"{section.SeatsLabel}" + (section.HasWaitlist ? $" \u00b7 waitlist {section.WaitlistRemaining}" : "")));
        var watch = new Button { Content = session.Store.IsWatching(section.Crn, term) ? "Watching" : "Watch seats", HorizontalAlignment = HorizontalAlignment.Left };
        watch.Click += (_, _) =>
        {
            session.Store.ToggleWatch(section, title, term);
            watch.Content = session.Store.IsWatching(section.Crn, term) ? "Watching" : "Watch seats";
        };
        panel.Children.Add(watch);
        return Chrome.Card(panel);
    }
}

internal static class CourseTable
{
    public static SortableTable.Column[] Columns { get; } =
    [
        new() { Title = "Course", Width = 108 },
        new() { Title = "Title", Width = 240 },
        new() { Title = "Credits", Width = 72, Numeric = true },
        new() { Title = "Sections", Width = 156 },
        new() { Title = "Seats", Width = 72, Numeric = true },
        new() { Title = "NUpath", Width = 110 },
        new() { Title = "Instructors", Width = 160 },
        new() { Title = "Campus", Width = 110 },
        new() { Title = "Days", Width = 80 },
        new() { Title = "Saved", Width = 88 },
    ];

    public static SortableTable.Row Make(SearchResult result, bool saved, DateTimeOffset? savedAt)
    {
        var course = result.Course;
        var stats = result.Stats;
        var code = stats.HasHonors ? course.Code + " HN" : course.Code;
        var paths = string.Join(" ", course.Nupaths.Select(path => path.Short()));
        var instructors = stats.InstructorNames.Count == 0 ? "\u2014" : string.Join(", ", stats.InstructorNames);
        var campus = stats.Campuses.Count == 0 ? "\u2014" : string.Join(", ", stats.Campuses.OrderBy(name => name, StringComparer.OrdinalIgnoreCase));
        var days = stats.MeetingDays.IsEmpty ? "\u2014" : stats.MeetingDays.CompactLabel;
        return new SortableTable.Row
        {
            Tag = course.Key,
            Cells =
            [
                code,
                course.Title,
                course.CreditsLabel,
                stats.AvailabilityLabel,
                stats.SeatsRemaining.ToString(),
                paths.Length == 0 ? "\u2014" : paths,
                instructors,
                campus,
                days,
                saved ? "Saved" : "",
            ],
            Sort =
            [
                null,
                null,
                course.MinCredits,
                stats.OpenSectionCount,
                stats.SeatsRemaining,
                null,
                null,
                null,
                null,
                savedAt is DateTimeOffset at ? (double)at.ToUnixTimeSeconds() : null,
            ],
        };
    }
}

internal sealed class CourseWindow : Window
{
    private static readonly List<Window> OpenWindows = [];

    private CourseWindow(AppSession session, CourseKey key)
    {
        Title = key.ToString();
        SystemBackdrop = new MicaBackdrop();
        var detail = new StackPanel { Spacing = 8 };
        Content = new ScrollViewer { Content = detail, Padding = new Thickness(20) };
        try { AppWindow.Resize(new Windows.Graphics.SizeInt32(760, 840)); } catch (Exception) { }
        CourseDetail.Show(detail, session, key, inWindow: true);
    }

    public static void Open(AppSession session, CourseKey key)
    {
        var window = new CourseWindow(session, key);
        OpenWindows.Add(window);
        window.Closed += (_, _) => OpenWindows.Remove(window);
        window.Activate();
    }
}

internal sealed class ProfessorWindow : Window
{
    private static readonly List<Window> OpenWindows = [];

    private ProfessorWindow(AppSession session, ProfessorDirectoryEntry entry)
    {
        Title = entry.DisplayName;
        SystemBackdrop = new MicaBackdrop();
        try { AppWindow.Resize(new Windows.Graphics.SizeInt32(1080, 760)); } catch (Exception) { }
        var root = new Grid { Padding = new Thickness(16), RowSpacing = 12 };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(300) });
        var header = new StackPanel { Spacing = 4 };
        header.Children.Add(Chrome.Heading(entry.DisplayName));
        header.Children.Add(Chrome.Body(entry.CourseSummary));
        var rate = new Button { Content = "Rate My Professors", HorizontalAlignment = HorizontalAlignment.Left };
        var name = entry.DisplayName;
        rate.Click += async (_, _) =>
        {
            var uri = new Uri("https://www.ratemyprofessors.com/search/professors?q=" + Uri.EscapeDataString(name));
            await Launcher.LaunchUriAsync(uri);
        };
        header.Children.Add(rate);
        root.Children.Add(header);

        var index = session.Index;
        var summary = index?.Instructor(entry.Id);
        var sections = summary is null || index is null
            ? new List<CourseSection>()
            : ProfessorPage.Sections(summary, index);
        var table = new SortableTable();
        var rows = sections.Select(section => new SortableTable.Row
        {
            Tag = section.CourseKey,
            Cells =
            [
                section.CourseKey.ToString(),
                section.SectionNumber,
                section.Title,
                section.ClassType,
                section.ScheduleSummary,
                section.Meetings.FirstOrDefault()?.LocationLabel ?? "\u2014",
                section.SeatsLabel,
            ],
            Sort = [null, null, null, null, section.WeeklyMeetings.Select(meeting => meeting.Start ?? 24 * 60).DefaultIfEmpty(24 * 60).Min(), null, section.SeatsRemaining],
        }).ToList();
        table.Show(
        [
            new() { Title = "Course", Width = 110 },
            new() { Title = "Section", Width = 80 },
            new() { Title = "Title", Width = 220 },
            new() { Title = "Type", Width = 100 },
            new() { Title = "Meets", Width = 180 },
            new() { Title = "Location", Width = 140 },
            new() { Title = "Seats", Width = 120, Numeric = true },
        ], rows);
        table.RowOpened += tag =>
        {
            if (tag is CourseKey key) CourseWindow.Open(session, key);
        };
        Grid.SetRow(table, 1);
        root.Children.Add(table);
        if (sections.Count == 0)
            header.Children.Add(Chrome.Body(index is null ? "The catalog is still loading." : "This term doesn\u2019t list sections for them."));

        var week = new WeekCalendarView();
        var colors = ProfessorPage.CourseColors(sections.Select(section => section.CourseKey).Distinct().ToList());
        week.Show(sections, key => colors.TryGetValue(key, out var color) ? color.Index : 0, 8, 18, session.Store.Preferences.ShowWeekends);
        Grid.SetRow(week, 2);
        root.Children.Add(week);
        Content = root;
    }

    public static void Open(AppSession session, ProfessorDirectoryEntry entry)
    {
        var window = new ProfessorWindow(session, entry);
        OpenWindows.Add(window);
        window.Closed += (_, _) => OpenWindows.Remove(window);
        window.Activate();
    }
}
