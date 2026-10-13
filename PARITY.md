# Parity with the Mac app

This is the Windows port of [Term Organize](https://github.com/AlexmChadwick/Northeastern-Term-Organize) (SwiftUI). Logic is in `src/TermOrganize.Core`. The shell is `src/TermOrganize.App` (WinUI 3, Mica, light and dark). Status is against the Swift sources this port was written from.

The core library was built and tested on Linux (.NET 9). The WinUI project was not compiled or run on Windows in that environment. GitHub Actions (`.github/workflows/windows.yml`) is the Windows build, test, MSIX, and unpackaged zip.

| Swift area | Windows | Status |
| --- | --- | --- |
| `Core/Models` (term, course, section, requisite, equivalences, user data) | `Models/` | Ported. JSON matches the Mac files: camelCase, DaySet as a bit mask, Swift keyed enums, lossy user-data decoding. |
| `Core/Banner` | `Banner/` | Ported. Live term list smoke-tested against `nubanner.neu.edu` (Fall 2026, code 202710). |
| `Services/SearchEngine.swift`, filters, shorthand | `Search/` | Ported and tested, including typo tolerance, aliases (orgo, linalg, fundies), and CRN search. |
| `Core/Storage/CatalogArchive.swift`, LZFSE | `Catalog/` | Read of JSON, gzip, and LZFSE `.catalog` is ported. The bundled Fall 2026 `.catalog` matches the gzip snapshot (tested on Linux). Writes are gzip. A managed decoder handles the bundled archive; if a block is rejected and the `lzfse` tool is installed, that tool is the fallback. |
| Course page: prereqs, coreqs, sections, seats, waitlist | `Course/`, `Shell/SearchPage.cs` | Logic ported. The course pane shows the requisite outline and refreshes live sections for the selected course. |
| `ScheduleGenerator`, scoring, filters, conflicts, half terms | `Schedule/Generator.cs`, `Scoring.cs`, `SlotMask.cs` | Ported. Slot masks are a prefilter; real conflicts use date windows. Tests include a brute-force check. |
| Constraint generator, preferences survey, ranking with reasons, no-results | `PreferenceRules.cs`, `Blockers.cs`, `NoResults.cs`, `NaturalLanguage.cs` | Ported. The survey text is parsed by `PreferenceParser` (deterministic). It does not call a model. |
| Apple Intelligence (`ScheduleAssistant`) | `Schedule/Assistant.cs`, `PreferenceParser` | Stand-in. Same job, on device, no network, no model. `Availability` reports that a model is not used. |
| Plans, week calendar, export .ics, export image, copy CRNs | `IcsWriter.cs`, `Exporter.cs`, `SchedulesPage.cs` | Ported. ICS uses America/New_York. The image is a PNG of the week grid. Adding events to the Windows Calendar app is not implemented. |
| `DegreePlanFormat`, PDF import, View Plan export, autofill | `Degree/Format.cs`, `Pdf.cs`, `Text.cs`, `Autofill.cs` | Ported. Export is a hand-rolled PDF in the same text layout (A4, Helvetica metrics, the same line positions). PdfPig reads text back in tests. |
| `DegreeAudit`, substitutions, petitions, waivers, hour trim, rebalance | `Degree/Audit.cs`, `AuditEngine.cs`, `Petitions.cs` | Ported. The Swift fixture tests, including 160 random plans checked against a separate exhaustive search, pass. |
| NUpath, universitywide minimums, major hours, co-op | `Degree/NUpath.cs` | Ported and tested, including the two-areas-per-course limit, renumbered courses, and unknown attributes. |
| Attribute lookup across recent Banner terms | `Course/CourseAttributeRepository.cs` | Ported and tested with a fake Banner. The degree page does not yet run this lookup and paint the requirement tree. |
| Equivalences, renumbered courses, older-term checks | `Models/Equivalences.cs`, `Course/`, `Degree/PlanEquivalences.cs`, `Reordering.cs` | Ported. A renumbered course uses the replacement's NUpath attributes. Offering history feeds reorder suggestions. |
| Degree plan editing, checks, totals | `Degree/Editing.cs`, `Analysis.cs`, `DegreePage.cs` | Logic ported. The page lists terms, credits, duplicate courses, PDF export, print, and schedule autofill. It does not yet include the Mac program/year picker or the full requirement outline. |
| `WatchlistComparison`, seat alerts | `Watchlist/`, `PlatformServices.cs` | Comparison logic ported. While the process is running, a timer (default 15 minutes) refreshes watched sections and raises a Windows toast. The tray menu can check now. Closing the window hides to the tray. There is no separate background task after Exit. |
| Free rooms, professors | `Rooms/`, `Professors/`, `CatalogPages.cs` | Ported. |
| Up Next widget | Tray flyout in `MainWindow.xaml` | Stand-in. Windows Widgets Board is not used. The flyout names the next meeting from the primary schedule. |
| `termorganize://`, `neuclasssearch://`, .pdf Open With | `Package.appxmanifest`, `PlatformServices.cs` | Ported. Packaged builds declare the protocols and the PDF file type. Unpackaged builds register them under `HKCU` as Open With, not as the default PDF app. |
| Siri / App Intents | Jump list in `PlatformServices.cs` | Stand-in. Search, Schedules, Degree plans, Watchlist, and Free rooms. |
| Mac commands and sidebar | `MainWindow.xaml` | Ported as a menu bar and keyboard shortcuts: Ctrl+1–8, Ctrl+N, Ctrl+Shift+N, Ctrl+O, Ctrl+E, Ctrl+P, Ctrl+R, Ctrl+,, Alt+Ctrl+0. Term picker is in the title bar. The unofficial notice stays visible. |
| Settings, legal text, export/import | `Settings/`, `SettingsPages.cs` | Ported. AGPL notices, SearchNEU credit, and the Northeastern disclaimer are in the app and in `NOTICE.md`. |
| iCloud / CloudKit | `Sync/CloudSync.cs`, local JSON | Stand-in. No CloudKit. `UserDataStore` is the file of record. Export/import and a folder sync helper cover the same data. Device-only fields stay off the sync payload. |

## Tests

Swift tests under `NEUClassSearchTests` were ported to xUnit in `tests/TermOrganize.Core.Tests`: models, search, Banner parsers, the catalog, schedules (generator, preferences, blockers, brute force), degree-plan text and PDF, the degree audit and NUpath fixtures, random audit matching, attribute lookup, watchlist, rooms, professors, cloud-sync merge, and the preference parser. On Linux, `dotnet test` of that project passed (291 tests, 0 failed), including the live Banner term-list smoke test.

## Not the same

- The WinUI shell is a working desktop app, not a control-for-control copy of the SwiftUI panes. Search is a resizable two-pane view. Schedules and degree plans are scrolling pages with the actions above, not the Mac inspector.
- Widgets Board, EventKit, and a signed-in iCloud account are not implemented.
- Catalog files this port saves are gzip. It still opens the Mac `.catalog` format.
- Seat checks run in-process. They stop when the tray command Exit quits the app.
