# Term Organize for Windows

> **Unofficial.** Term Organize is not affiliated with, endorsed by, or sponsored by Northeastern University or Sandbox.

A Windows desktop app for searching Northeastern University courses, building a class schedule, planning a degree, and watching seats. This repository is the C# and WinUI port of the [Mac and iPhone app](https://github.com/AlexmChadwick/Northeastern-Term-Organize). Course information is copyright Northeastern University and is not covered by the license below.

The window has one sidebar: Search, Subjects, Saved, Professors, Free Rooms, Schedules, Degree Plans, and Watchlist. The term picker is in the title bar. The unofficial-status line stays along the bottom. Closing the window leaves the app in the tray. Exit on the tray menu quits. The tray flyout is Up Next. Light and dark themes use Fluent styling, and a Mica backdrop when Windows provides one.

## What it does

**Search.** The box takes a course, a professor, or a 5-digit CRN. A filter above the results is All, Undergrad, or Grad, and the choice is saved with your settings. Undergrad is a course number below 5000. Grad is 5000 and up. All leaves every number. The same row also has open seats, honors, no prerequisites, no corequisites, subject, NUpath, level, meeting days, and campus.

**CPS restriction.** When every loaded section of a course is limited to College of Professional Studies students, the course is marked "Restricted to CPS (College of Professional Studies) students". A mix of CPS-only and open sections says "Some sections are CPS only". The note shows on search results and on degree-plan and schedule rows.

**Seats.** A section with anyone on the waitlist is not open: a freed seat goes to the waitlist first, so the open count is zero while the waitlist count is above zero. The seat line is "N of M seats". When a waitlist exists it adds the size, "Waitlist 7/10", or "Waitlist 7" when Banner sends a count and no capacity. Spoken text says "Full", or "N of M seats open", and names the waitlist. A watchlist row says "Full" or "N of M open", and names open waitlist spots when the section is not actually open.

**Schedules.** Generate a week from the courses on a plan, with filters for full sections, honors, online, minimum seats, campus, and a free day. The page can copy CRNs, print, export an image, write an `.ics` file to the desktop, or add the meetings to a Windows calendar named Term Organize. If Calendar refuses the events, the app writes the `.ics` file instead. A variable-credit course has a credit box. The value has to be a whole number inside Banner's range. A blank choice uses the catalog default when that default is inside the range, otherwise the low end.

**Degree plans.** Add terms and courses, import a degree-audit PDF, and audit the plan against the requirements for a catalog year, program, and concentration. The requirements pane covers the requirement tree, substitutions, petitions, and waivers. Ctrl+E on this page writes `degree-plan.pdf` to the desktop. Variable-credit rows use the same credit box as schedules. If the imported PDF has a student name or ID, the PDF export includes them.

**Watchlist.** Each section has "Watch seats". The watchlist can notify this PC when a seat opens, and, if you turn it on, when a waitlist spot opens. A scheduled check can keep running after you quit. No account is involved.

**Lists.** Search, professor, and watchlist results are single-selection tables. Arrow keys stay in the list and move the selection. A click shows the row. A double-click opens it.

**MCP server.** `src/TermOrganize.Mcp` is a local stdio server (protocol `2025-06-18`) for the bundled catalogs and the degree plans in `UserData.json`. It does not open a network connection. `search_courses` takes the same All / Undergrad / Grad split (`all`, `undergrad`, `grad`). Tool names, JSON shapes, and editor snippets are in [docs/MCP.md](docs/MCP.md).

## Instructor emails and privacy

Instructor emails are kept only when the domain is exactly `northeastern.edu` or `neu.edu`. The check is case-insensitive, and the value has to be one whole ASCII address. Anything else is dropped, not trimmed into a different address.

That drops subdomains (`husky.neu.edu`, `coe.neu.edu`, `ece.northeastern.edu`), lookalikes (`northeastern.edu.example.org`, `neu.edu.evil.com`, a fullwidth character, a trailing dot), and obfuscated or malformed text (`mailto:`, a space inside the address, a zero-width character, a string that is not an address). The same filter runs when a live Banner response is read, when a catalog file is loaded or saved, and when the MCP server returns an instructor.

The app does not collect student personal data. There is no account, no analytics, and no tracking. The only network calls are to Northeastern's public Banner class search at `nubanner.neu.edu` and, for degree requirements, the public catalog at `catalog.northeastern.edu`. Student-looking mail such as `@husky.neu.edu` is dropped with every other non-matching domain. A degree-audit PDF you choose to import can contain a student name and ID. Those two fields stay in the local data file on this PC. They are left out of folder sync, the MCP server does not return them, and a degree-plan PDF includes them only when the plan already has them.

## Your data

Plans, the watchlist, saved courses, and settings are `%LOCALAPPDATA%\TermOrganize\UserData.json`. Settings can export and import that file. An MSIX install virtualizes `LocalApplicationData`; the path the MCP server uses is described in [docs/MCP.md](docs/MCP.md).

Bundled catalogs are Fall 2026 (`term-202710`, gzip and the Mac LZFSE `.catalog`) and Spring 2027 (`term-202730.json.gz`). The loader reads gzip before `.catalog`. A fresh launch opens the newest bundled term. Live sections come from `nubanner.neu.edu`.

## Keyboard

Ctrl+1 through Ctrl+8 switch the sidebar, in the order listed above. Ctrl+N new schedule, Ctrl+Shift+N new degree plan, Ctrl+O import a degree-audit PDF, Ctrl+E export, Ctrl+P print, Ctrl+R reload the term, Ctrl+, settings, Alt+Ctrl+0 show or hide the sidebar.

## License

Term Organize is free software under the GNU Affero General Public License v3.0 only (`AGPL-3.0-only`). You can redistribute it and change it. There is no warranty. The full text is [LICENSE](LICENSE).

Portions of the Banner client, requisite parsing, and schedule generation are derived from [SearchNEU](https://github.com/sandboxnu/searchneu), Copyright (C) Sandbox at Northeastern University and the SearchNEU contributors, also under the AGPL-3.0. Copyright, the upstream revision, and third-party notices are in [NOTICE.md](NOTICE.md).

How each Swift area was carried over is in [PARITY.md](PARITY.md). The logic is `src/TermOrganize.Core` (.NET 9). The WinUI 3 shell is `src/TermOrganize.App`. The MCP server is `src/TermOrganize.Mcp`.
