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

    public static void Show(Grid host, AppSession session, CourseKey key, bool inWindow, bool allowLiveRefresh = true)
    {
        var index = session.Index;
        var course = index?.Course(key);
        if (index is null || course is null)
        {
            Chrome.Fill(host, key.ToString(), "That course is not in this term\u2019s catalog.");
            return;
        }
        session.Store.RecordView(key, index.Term.Code);
        var stats = index.Stats(key);
        var header = new StackPanel { Spacing = 8 };
        header.Children.Add(Chrome.Heading($"{course.Code}  {course.Title}"));
        header.Children.Add(Chrome.Body($"{course.CreditsLabel} \u00b7 {stats.AvailabilityLabel} \u00b7 {stats.SeatsRemaining} seats left"));
        if (CpsNotes.ForCourse(index.Sections(key)) is string cps)
            header.Children.Add(Chrome.Caption(cps));
        if (!string.IsNullOrWhiteSpace(course.Description))
            header.Children.Add(Chrome.Body(course.Description));
        if (RequisiteTreeView.Build(course.Prereqs, session, "Prerequisites") is UIElement prereq)
            header.Children.Add(prereq);
        if (RequisiteTreeView.Build(course.Coreqs, session, "Corequisites") is UIElement coreq)
            header.Children.Add(coreq);
        var standing = CourseDegreePlanStandingLookup.For(session.Store, key);
        if (standing is not null) header.Children.Add(Chrome.Body(standing.Text));

        var actions = new WrapLayout { Spacing = 8 };
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
            session.Store.AddCourse(key, plan.Id, credits: course.IsVariableCredit ? course.StartingCredits : null);
        };
        var refresh = new Button { Content = "Refresh seats" };
        refresh.Click += async (_, _) => await RefreshAsync(host, session, course, index.Term.Code, inWindow);
        actions.Children.Add(save);
        actions.Children.Add(add);
        actions.Children.Add(refresh);
        if (!inWindow)
        {
            var open = new Button { Content = "Open in a window" };
            open.Click += (_, _) => CourseWindow.Open(session, key);
            actions.Children.Add(open);
        }
        header.Children.Add(actions);
        host.Children.Clear();
        host.RowDefinitions.Clear();
        host.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        host.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        var headerScroll = new ScrollViewer
        {
            Content = header,
            MaxHeight = 360,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollMode = ScrollMode.Disabled,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
        };
        host.Children.Add(headerScroll);
        var sections = SectionsList(session, index.Sections(key), course.Title, index.Term.Code);
        Grid.SetRow(sections, 1);
        host.Children.Add(sections);
        if (!allowLiveRefresh) return;
        var needsDetails = string.IsNullOrWhiteSpace(course.Description)
            || index.Sections(key).Any(section => section.Restrictions is null);
        if (session.Store.Preferences.AutoRefreshSeats)
            _ = RefreshAsync(host, session, course, index.Term.Code, inWindow);
        else if (needsDetails)
            _ = DetailsAsync(host, session, course, index.Term.Code, index.Sections(key).FirstOrDefault()?.Crn, inWindow);
    }

    private static ListView SectionsList(AppSession session, IReadOnlyList<CourseSection> sections, string title, string term)
    {
        var list = new ListView
        {
            SelectionMode = ListViewSelectionMode.Single,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
        };
        foreach (var section in sections)
        {
            list.Items.Add(new ListViewItem
            {
                Content = SectionCard(session, section, title, term),
                Tag = section.Crn,
                HorizontalContentAlignment = HorizontalAlignment.Stretch,
                Padding = new Thickness(0, 4, 0, 4),
            });
        }
        list.SelectionChanged += (_, _) =>
        {
            if (list.SelectedItem is ListViewItem item)
                list.ScrollIntoView(item);
        };
        return list;
    }

    private static async Task RefreshAsync(Grid host, AppSession session, Course course, string term, bool inWindow)
    {
        var token = term + "|" + course.Key;
        if (!Busy.Add(token)) return;
        try
        {
            var live = await BannerClient.Shared.FetchSectionsAsync(term, [course.Key]);
            if (live.TryGetValue(course.Key, out var banner))
            {
                try
                {
                    await BannerClient.Shared.AttachFacultyAsync(term, banner, 12);
                }
                catch (BannerException)
                {
                    // Seat counts still apply when the faculty endpoint is down.
                }
                session.RefreshCourse(course.Key, banner);
                var current = session.Course(course.Key) ?? course;
                try
                {
                    await FillDetailsAsync(session, current, term, banner.FirstOrDefault()?.CourseReferenceNumber);
                }
                catch (BannerException)
                {
                    // Sections are already refreshed. The description can load on the next open.
                }
                Show(host, session, course.Key, inWindow, allowLiveRefresh: false);
            }
        }
        catch (Exception ex)
        {
            if (host.Children.FirstOrDefault() is ScrollViewer { Content: StackPanel header })
                header.Children.Insert(0, Chrome.Body("Banner didn\u2019t answer: " + ex.Message));
        }
        finally
        {
            Busy.Remove(token);
        }
    }

    private static async Task DetailsAsync(Grid host, AppSession session, Course course, string term, string? crn, bool inWindow)
    {
        var token = "details|" + term + "|" + course.Key;
        if (!Busy.Add(token)) return;
        try
        {
            await FillDetailsAsync(session, course, term, crn);
            Show(host, session, course.Key, inWindow, allowLiveRefresh: false);
        }
        catch (BannerException ex)
        {
            if (host.Children.FirstOrDefault() is ScrollViewer { Content: StackPanel header })
                header.Children.Insert(0, Chrome.Body("Banner didn\u2019t return the description: " + ex.Message));
        }
        finally
        {
            Busy.Remove(token);
        }
    }

    private static async Task FillDetailsAsync(AppSession session, Course course, string term, string? crn)
    {
        if (session.Index is null) return;
        if (string.IsNullOrWhiteSpace(course.Description) && crn is not null)
        {
            var detailed = await BannerClient.Shared.FetchCourseDetailsAsync(
                term,
                crn,
                course,
                new BannerParsers.SubjectLookup(session.Index.Catalog.Subjects));
            CatalogBuilder.ApplyCourseDetails(course, detailed);
        }
        await FillRestrictionsAsync(session, course.Key, term);
    }

    /// <summary>One restrictions request at a time, with a short pause, so a course with many sections does not hammer Banner.</summary>
    private static async Task FillRestrictionsAsync(AppSession session, CourseKey key, string term)
    {
        var sections = session.Index?.Sections(key).Where(section => section.Restrictions is null).ToList();
        if (sections is null || sections.Count == 0) return;
        for (var i = 0; i < sections.Count; i++)
        {
            if (i > 0) await Task.Delay(150);
            try
            {
                var html = await BannerClient.Shared.FetchRestrictionsHtmlAsync(term, sections[i].Crn);
                sections[i].Restrictions = SectionRestrictions.Parse(html);
            }
            catch (Exception)
            {
                // Leave the section unknown. The caption stays hidden.
            }
        }
    }

    private static Border SectionCard(AppSession session, CourseSection section, string title, string term)
    {
        var panel = new StackPanel { Spacing = 4, HorizontalAlignment = HorizontalAlignment.Stretch };
        panel.Children.Add(new TextBlock
        {
            Text = $"{section.ClassType} {section.SectionNumber} \u00b7 CRN {section.Crn}",
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            TextWrapping = TextWrapping.Wrap,
        });
        panel.Children.Add(Chrome.Body($"{section.ScheduleSummary} \u00b7 {section.InstructorLabel} \u00b7 {section.Campus}"));
        panel.Children.Add(Chrome.Body($"{section.SeatsLabel}" + (section.HasWaitlist ? $" \u00b7 waitlist {section.WaitlistRemaining}" : "")));
        if (section.CpsOnly == true)
            panel.Children.Add(Chrome.Caption(CpsNotes.Restricted));
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

    public static SortableTable.Row Make(SearchResult result, bool saved, DateTimeOffset? savedAt, string? note = null)
    {
        var course = result.Course;
        var stats = result.Stats;
        var code = stats.HasHonors ? course.Code + " HN" : course.Code;
        var title = note is null ? course.Title : course.Title + "\n" + note;
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
                title,
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
        var host = new Grid { Padding = new Thickness(24) };
        Content = host;
        try { AppWindow.Resize(new Windows.Graphics.SizeInt32(760, 840)); } catch (Exception) { }
        CourseDetail.Show(host, session, key, inWindow: true);
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
        var root = new Grid { Padding = new Thickness(24), RowSpacing = 12 };
        Chrome.ClipToBounds(root);
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
