// Copyright (C) 2026 Term Organize contributors.
// SPDX-License-Identifier: AGPL-3.0-only

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using TermOrganize.Core;
using Windows.ApplicationModel.DataTransfer;

namespace TermOrganize;

public sealed class WatchPage : UserControl
{
    private readonly AppSession _session;
    private readonly MainWindow _window;
    private readonly SortableTable _table = new();
    private readonly StackPanel _detail = new() { Spacing = 8 };
    private readonly WeekCalendarView _week = new();
    private WatchItem? _selected;

    public WatchPage(AppSession session, MainWindow window)
    {
        _session = session;
        _window = window;
        var bar = new WrapLayout { Spacing = 8 };
        bar.Children.Add(Chrome.Heading("Watchlist"));
        var check = new Button { Content = "Check now", VerticalAlignment = VerticalAlignment.Center };
        check.Click += (_, _) =>
        {
            PlatformServices.CheckSeats(session, window);
            Show();
        };
        bar.Children.Add(check);
        var print = new Button { Content = "Print", VerticalAlignment = VerticalAlignment.Center };
        print.Click += (_, _) => Print(WinRT.Interop.WindowNative.GetWindowHandle(_window));
        bar.Children.Add(print);
        var left = new Grid { RowSpacing = 8 };
        left.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        left.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        left.Children.Add(bar);
        Grid.SetRow(_table, 1);
        left.Children.Add(_table);
        var right = new ScrollViewer { Content = _detail, Padding = new Thickness(16, 0, 0, 0) };
        Content = new PaneSplit(left, right, 520) { Padding = Chrome.PagePadding };
        _table.RowChosen += tag =>
        {
            if (tag is WatchItem item)
            {
                _selected = item;
                PaintDetail();
            }
        };
        _table.RowOpened += tag =>
        {
            if (tag is WatchItem item) CourseWindow.Open(session, item.CourseKey);
        };
        Show();
    }

    public void Print(nint owner)
    {
        var lines = new List<string> { "Watchlist" };
        foreach (var item in _session.Store.Watchlist)
        {
            var row = Row(item);
            lines.Add(string.Join(" \u00b7 ", row.Cells.Where(cell => cell.Length > 0)));
        }
        if (lines.Count == 1) lines.Add("Nothing is watched.");
        if (NativePrint.Show(owner, "Watchlist", lines)) _window.SetStatus("Sent the watchlist to the printer.");
        else _window.SetStatus("Print was canceled.");
    }

    private void Show()
    {
        var items = _session.Store.Watchlist;
        if (items.Count == 0)
        {
            _table.Show(Columns(), Array.Empty<SortableTable.Row>());
            PaintEmpty();
            return;
        }
        var rows = items.Select(Row).ToList();
        _table.Show(Columns(), rows);
        _selected = items.FirstOrDefault(item => item.Id == _selected?.Id) ?? items[0];
        PaintDetail();
    }

    private static SortableTable.Column[] Columns() =>
    [
        new() { Title = "Course", Width = 110 },
        new() { Title = "Section", Width = 120 },
        new() { Title = "CRN", Width = 80 },
        new() { Title = "Instructor", Width = 150 },
        new() { Title = "Times", Width = 160 },
        new() { Title = "Location", Width = 120 },
        new() { Title = "Seats", Width = 140, Numeric = true },
        new() { Title = "Checked", Width = 130 },
        new() { Title = "Last Opened", Width = 130 },
        new() { Title = "Alerts", Width = 120 },
        new() { Title = "Term", Width = 120 },
    ];

    private SortableTable.Row Row(WatchItem item)
    {
        var loaded = _session.Index is not null && _session.Index.Term.Code == item.TermCode;
        var section = loaded ? _session.Index!.Section(item.Crn) : null;
        var status = WatchlistStatusInfo.For(item, loaded && section is null);
        var term = _session.KnownTerms.FirstOrDefault(candidate => candidate.Code == item.TermCode)?.Name
            ?? (_session.Term?.Code == item.TermCode ? _session.Term.Name : item.TermCode);
        var rank = status switch
        {
            WatchlistStatus.Open => 3,
            WatchlistStatus.Waitlist => 2,
            WatchlistStatus.Full => 1,
            _ => 0,
        };
        return new SortableTable.Row
        {
            Tag = item,
            Cells =
            [
                item.CourseKey.ToString(),
                item.SectionLabel.Replace(" \u00b7 Section ", " ", StringComparison.Ordinal),
                item.Crn,
                section?.InstructorLabel ?? "\u2014",
                section?.ScheduleSummary ?? "\u2014",
                Places(section),
                status.Detail(item),
                item.LastCheckedAt?.ToLocalTime().ToString("g") ?? "\u2014",
                item.LastNotifiedAt?.ToLocalTime().ToString("g") ?? "\u2014",
                item.NotifyOnWaitlist ? "Seats and waitlist" : "Seats",
                term,
            ],
            Sort =
            [
                null, null, null, null, null, null,
                rank * 100_000 + Math.Max(0, item.LastSeatsRemaining),
                item.LastCheckedAt is DateTimeOffset checkedAt ? (double)checkedAt.ToUnixTimeSeconds() : null,
                item.LastNotifiedAt is DateTimeOffset opened ? (double)opened.ToUnixTimeSeconds() : 0,
                item.NotifyOnWaitlist ? 1 : 0,
                null,
            ],
        };
    }

    private static string Places(CourseSection? section)
    {
        if (section is null) return "\u2014";
        var weekly = section.WeeklyMeetings.ToList();
        if (weekly.Count == 0) return section.IsOnline ? "Online" : "\u2014";
        return string.Join(" \u00b7 ", weekly.Select(meeting => meeting.IsOnline ? "Online" : meeting.LocationLabel).Distinct());
    }

    private void PaintEmpty()
    {
        _detail.Children.Clear();
        _detail.Children.Add(Chrome.Heading("Watch full sections"));
        _detail.Children.Add(Chrome.Body("Get a notification on this PC when a seat or a waitlist spot opens. No account needed."));
        _detail.Children.Add(Chrome.Body("1  Find a full section. Search for a course and open it."));
        _detail.Children.Add(Chrome.Body("2  Watch it. Each section has Watch seats. It is listed here from then on."));
        _detail.Children.Add(Chrome.Body("3  Get notified. Term Organize keeps checking on a schedule, including after you quit, and shows a toast when a seat opens."));
        var find = new Button { Content = "Find a course", HorizontalAlignment = HorizontalAlignment.Left };
        find.Click += (_, _) => _window.Select("search");
        _detail.Children.Add(find);
    }

    private void PaintDetail()
    {
        _detail.Children.Clear();
        if (_selected is not WatchItem item)
        {
            PaintEmpty();
            return;
        }
        var loaded = _session.Index is not null && _session.Index.Term.Code == item.TermCode;
        var section = loaded ? _session.Index!.Section(item.Crn) : null;
        var status = WatchlistStatusInfo.For(item, loaded && section is null);
        _detail.Children.Add(Chrome.Heading(item.CourseKey.ToString()));
        _detail.Children.Add(Chrome.Body(item.CourseTitle));
        _detail.Children.Add(Chrome.Body($"{item.SectionLabel} \u00b7 CRN {item.Crn}"));
        _detail.Children.Add(new TextBlock { Text = status.Detail(item), FontWeight = Microsoft.UI.Text.FontWeights.SemiBold });
        if (section is not null)
        {
            _detail.Children.Add(Chrome.Body(section.ScheduleSummary));
            _detail.Children.Add(Chrome.Body(section.InstructorLabel));
            _detail.Children.Add(Chrome.Body(Places(section)));
        }
        var alerts = new CheckBox { Content = "Also alert for a waitlist spot", IsChecked = item.NotifyOnWaitlist };
        alerts.Checked += (_, _) => SaveAlert(item, true);
        alerts.Unchecked += (_, _) => SaveAlert(item, false);
        _detail.Children.Add(alerts);
        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        var open = new Button { Content = "Open course" };
        open.Click += (_, _) => CourseWindow.Open(_session, item.CourseKey);
        var copy = new Button { Content = "Copy CRN" };
        copy.Click += (_, _) =>
        {
            var package = new DataPackage();
            package.SetText(item.Crn);
            Clipboard.SetContent(package);
            _window.SetStatus("Copied " + item.Crn);
        };
        var stop = new Button { Content = "Stop watching" };
        stop.Click += (_, _) =>
        {
            _session.Store.Unwatch(item.Crn, item.TermCode);
            _selected = null;
            Show();
        };
        actions.Children.Add(open);
        actions.Children.Add(copy);
        actions.Children.Add(stop);
        _detail.Children.Add(actions);
        var watched = new List<CourseSection>();
        if (_session.Index is not null)
        {
            foreach (var other in _session.Store.Watchlist.Where(candidate => candidate.TermCode == _session.Index.Term.Code))
            {
                var match = _session.Index.Section(other.Crn);
                if (match is not null) watched.Add(match);
            }
        }
        _week.Show(watched, key => CourseColor.ForCourse(key).Index, 8, 18, _session.Store.Preferences.ShowWeekends);
        _detail.Children.Add(_week);
    }

    private void SaveAlert(WatchItem item, bool waitlist)
    {
        item.NotifyOnWaitlist = waitlist;
        _session.Store.UpdateWatchItem(item);
        Show();
    }
}
