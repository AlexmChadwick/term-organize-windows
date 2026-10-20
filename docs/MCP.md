# Term Organize MCP server

Unofficial local server for the bundled course catalogs and the degree plans stored on this computer. It is not affiliated with, endorsed by, or sponsored by Northeastern University.

The server speaks MCP over stdin and stdout, protocol `2025-06-18`. It uses the same catalog decoder, conflict check, schedule generator, and `UserData.json` model as the desktop app. It does not open a network connection, does not log in, and does not return student name or student id fields.

Fall 2026 (`202710`) and Spring 2027 (`202730`) are the only catalogs it reads. Instructor emails are returned only when the domain is exactly `northeastern.edu` or `neu.edu`. Other addresses are removed. Section times use the word "to" rather than a dash.

## Run

From the repository, with the .NET 9 SDK:

```bash
dotnet run --project src/TermOrganize.Mcp/TermOrganize.Mcp.csproj
```

A published build is `TermOrganize.Mcp.dll` (run with `dotnet exec`) or `TermOrganize.Mcp.exe` on Windows.

```bash
dotnet exec src/TermOrganize.Mcp/bin/Debug/net9.0/TermOrganize.Mcp.dll --data-file /tmp/UserData.json
```

Flags:

| Flag | Environment variable | Effect |
| --- | --- | --- |
| `--data-file <path>` | `TERM_ORGANIZE_DATA_FILE` | Read and write this file instead of the resolved app path. The flag wins. |
| `--read-only` | `TERM_ORGANIZE_MCP_READONLY=1` (also `true` or `TRUE`) | Hide every write tool. `list_plans` and `get_plan` stay available. |
| `--help` |  | Print usage on stderr and exit. |

Logging goes to stderr so it does not break the JSON-RPC stream.

## Where plans are stored

The app saves every plan, schedule, and preference through `UserDataStore`. The file name is `UserData.json`.

Portable and unpackaged builds call `Environment.SpecialFolder.LocalApplicationData`, which is:

`%LOCALAPPDATA%\TermOrganize\UserData.json`

The MSIX package uses that same call. Windows virtualizes `LocalApplicationData` for a packaged process into the package folder. It does not use `ApplicationData` LocalState unless some other API writes there. For this package identity (`TermOrganize`, publisher `CN=Term Organize`, publisher id `wmazyzrpc95f0`) the virtualized file is:

`%LOCALAPPDATA%\Packages\TermOrganize_wmazyzrpc95f0\LocalCache\Local\TermOrganize\UserData.json`

The MCP process is not inside the package, so it does not get that redirection automatically. On startup it looks for an existing file in this order and keeps the one with the newest UTC write time:

1. `%LOCALAPPDATA%\TermOrganize\UserData.json`
2. `%LOCALAPPDATA%\Packages\TermOrganize_*\LocalCache\Local\TermOrganize\UserData.json`
3. `%LOCALAPPDATA%\Packages\TermOrganize_*\LocalState\TermOrganize\UserData.json`
4. `%LOCALAPPDATA%\Packages\TermOrganize_*\LocalState\UserData.json`

`TermOrganize_wmazyzrpc95f0` is always included. Any other `TermOrganize_*` folder is included too, so a different test certificate still matches.

If none of those files exist yet and the package folder is present, the next write goes to the LocalCache path above, which is where the installed MSIX app will read it. Otherwise the next write goes to the portable path. `--data-file` or `TERM_ORGANIZE_DATA_FILE` skips this search.

Writes reload the file, compare `expected_revision`, then save with a temp file and rename. The previous file is copied to `UserData.json.bak` (one rolling backup). If the desktop app saved in between, the write fails and names the current revision.

The desktop app also watches this file. Before it saves, it hashes the bytes and compares that revision, along with the UTC write time, to the file it last loaded. Plans only one side edited are merged. A plan both sides edited keeps the file already on disk, and the app's copy is saved beside it as `<name> (edited in app)`, so an MCP write is not replaced. A change while the window is open is reloaded through a debounced file watcher.

Degree-plan courses may include `selectedCrn`. The app's loader keeps that field. The server also mirrors the plan into a non-primary schedule (`IsPrimary` false) so the Schedules page can show the chosen section. That schedule's id is derived from the degree plan id and the term code. Other schedules are left alone.

## Claude Desktop

```json
{
  "mcpServers": {
    "term-organize": {
      "command": "dotnet",
      "args": ["exec", "C:\\path\\to\\TermOrganize.Mcp.dll"]
    }
  }
}
```

Read only:

```json
{
  "mcpServers": {
    "term-organize": {
      "command": "dotnet",
      "args": ["exec", "C:\\path\\to\\TermOrganize.Mcp.dll", "--read-only"],
      "env": { "TERM_ORGANIZE_DATA_FILE": "C:\\path\\to\\UserData.json" }
    }
  }
}
```

## Cursor

`.cursor/mcp.json`:

```json
{
  "mcpServers": {
    "term-organize": {
      "command": "dotnet",
      "args": ["exec", "/absolute/path/to/TermOrganize.Mcp.dll", "--data-file", "/absolute/path/to/UserData.json"]
    }
  }
}
```

## Tools

Every tool returns structured JSON. Names and keys are the same on Windows, Mac, and Linux. Write tools that change a plan return the same object as `get_plan`, plus extra keys where noted. `revision` is the first 16 hex characters of the SHA-256 of the degree plan JSON. Pass it back as `expected_revision`.

### list_terms

No arguments.

```json
{ "terms": [ { "code": "202710", "name": "Fall 2026" } ] }
```

### search_courses

`term` (code or label such as `Fall 2026`), optional `query`, `subject`, `number`, `attribute`, `limit` (default 25, max 100).

```json
{
  "term": "202730",
  "courses": [
    { "subject": "CS", "number": "3000", "title": "Algorithms", "credits": 4, "attributes": ["NCFQ"] }
  ],
  "truncated": false
}
```

`credits` is a number when min and max match, otherwise a string such as `1 to 4`.

### get_course

`term`, `subject`, `number`.

```json
{
  "term": "202730",
  "subject": "CS",
  "number": "3000",
  "title": "Algorithms",
  "description": "",
  "credits": 4,
  "attributes": [],
  "sections": [
    {
      "crn": "12345",
      "section": "01",
      "days": "MW",
      "times": "MW 9:50 AM to 11:30 AM",
      "location": "SN 108",
      "instructors": ["Ada Lovelace"],
      "seats_remaining": 3,
      "seat_capacity": 40
    }
  ]
}
```

### check_schedule

`term`, `crns` (array of strings).

```json
{
  "term": "202710",
  "clashes": [ { "crn_a": "10001", "crn_b": "10002", "summary": "CS 3000 overlaps CS 3500" } ],
  "unknown_crns": ["99999"]
}
```

### suggest_schedule

`term`, `courses` (array of `{ "subject", "number" }`), optional `constraints`:

* `no_classes_before` and `no_classes_after`: `"09:30"`, `"9:30 AM"`, or minutes from midnight.
* `days_off`: array of `U M T W R F S`, full names, or a string such as `MWF`.

Up to 8 schedules, best first.

```json
{
  "term": "202730",
  "schedules": [
    {
      "score": 1.0,
      "sections": [ { "crn": "12345", "subject": "CS", "number": "3000", "days": "MW", "times": "MW 9:50 AM to 11:30 AM" } ]
    }
  ],
  "truncated": false
}
```

### reorganize_plan

Does not save. `plan` is `{ "terms": [ { "term": "Fall 2026", "courses": [ { "subject": "CS", "number": "3000" } ] } ] }`. `term` may be a Banner code.

The core library only suggests moves for one semester. This tool walks the plan and applies suggestions that do not add a semester, so graduation is not delayed. Courses that would need a new semester are listed in `unresolved`.

```json
{
  "terms": [ { "term": "202730", "name": "Spring 2027", "courses": [ { "subject": "CS", "number": "3000", "title": "Algorithms" } ] } ],
  "moves": [ { "subject": "CS", "number": "3000", "from_term": "Fall 2026", "to_term": "Spring 2027", "reason": "Offered every spring" } ],
  "unresolved": [],
  "note": "Whole-plan reorganize is implemented on top of DegreePlanReordering."
}
```

### list_plans

```json
{
  "plans": [
    {
      "id": "00000000-0000-0000-0000-000000000000",
      "name": "Primary",
      "terms": [ { "term": "202710", "name": "Fall 2026", "course_count": 2 } ],
      "updated_at": "2026-10-20T00:00:00.0000000+00:00",
      "revision": "0123456789abcdef"
    }
  ]
}
```

### get_plan

`plan_id`. Same fields as one list entry, with full courses and `validation`. A chosen section adds `crn` and `section`.

```json
{
  "validation": {
    "clashes": [ { "crn_a": "10001", "crn_b": "10002", "summary": "overlap" } ],
    "not_offered": [ { "subject": "CS", "number": "3000", "term": "Fall 2026" } ]
  }
}
```

### create_plan

`name`, optional `terms` (labels or codes). Returns `get_plan`.

### rename_plan

`plan_id`, `name`, `expected_revision`. Returns `get_plan`.

### add_course

`plan_id`, `term`, `subject`, `number`, optional `crn`, `expected_revision`.

The course must exist in a bundled catalog. A CRN must be a section of that course in that term. A course already on the plan is rejected; use `move_course`. A known course may be placed in a term that does not offer it. `get_plan` then reports `not_offered`.

### remove_course

`plan_id`, `term`, `subject`, `number`, `expected_revision`. Destructive.

### move_course

`plan_id`, `subject`, `number`, `from_term`, `to_term`, `expected_revision`.

If the selected CRN is not a section of the course in the destination term, the CRN is cleared.

### set_section

`plan_id`, `term`, `subject`, `number`, `crn`, `expected_revision`. The course must already be in the term.

### apply_reorganize

`plan_id`, `expected_revision`. Destructive. Runs `reorganize_plan` and saves. Returns `get_plan` plus `moves`, `unresolved`, and `note`.

### delete_plan

`plan_id`, `expected_revision`, `confirm`. Destructive. Refuses unless `confirm` is true.

```json
{ "deleted": true, "id": "00000000-0000-0000-0000-000000000000" }
```

## Annotations

Read tools set `readOnlyHint`: `list_terms`, `search_courses`, `get_course`, `check_schedule`, `suggest_schedule`, `reorganize_plan`, `list_plans`, `get_plan`.

`delete_plan`, `remove_course`, and `apply_reorganize` set `destructiveHint`.

Every tool sets `openWorldHint` false. Nothing here calls the network.
