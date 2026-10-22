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

Writes reload the file, compare `expected_revision`, then save with a temp file and rename. The previous file is copied to `UserData.json.bak` (one rolling backup). If the plan changed, the write fails with `revision_conflict` and names the current integer revision.

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

SHARED CONTRACT

- Transport: stdio, newline-delimited JSON-RPC 2.0, one message per line, per the MCP stdio spec (protocol 2025-06-18). No Content-Length framing.
- Term objects: {"term":"202730","name":"Spring 2027"}. The key is always "term", never "code".
- Revisions: integer, starting at 1 and incremented on every change to that plan.
- Errors: a tool result with isError:true and structuredContent {"error":{"code":C,"message":M}}, where C is one of: invalid_argument, not_found, revision_conflict, confirm_required, read_only, storage_error, unknown_tool. The message is never empty. A write tool called with --read-only on returns read_only, not unknown_tool. Write tools stay hidden from tools/list.
- Instructors in get_course: [{"name":..., "email":... (optional)}]. The email is present only when its domain is EXACTLY northeastern.edu or neu.edu, compared case-insensitively. Subdomains are not allowed (husky.neu.edu, ccs.neu.edu, mail.neu.edu are all dropped).
- create_plan {name, terms?: ["202710","202730"]}, a list of term strings.
- reorganize_plan accepts either {plan_id} or {plan:{terms:[{term, courses:[{subject, number}]}]}}. It returns {moves:[{subject, number, from_term, to_term, reason}], unresolved:[{subject, number, reason}], plan:{...}}.
- Reorganize rule, the same everywhere: a course moves only into a term the plan already has. The target term's bundled catalog must offer the course, prerequisite order within the plan must be kept, and it never adds a semester. A course is offered in a term when the bundled catalog for that term has at least one section of it.
- Shared fixture: tests/fixtures/mcp-reorganize-fixture.json. It is a plan with CS 4805, CS 2535, CS 2700, CS 4130 and CS 2701 in Fall 2026, plus an empty Spring 2027. Each server must have a test that checks its output against the expected moves. Work out the expected result from the bundled 202730 catalog and the prereqs, and write it into the fixture. Include in your report the per-course reasoning: offered in 202730 yes/no, and prereq status.
- Storage failures: if the plan file exists but can't be read (permission denied, sandbox, malformed JSON, unknown newer version), every plan tool returns storage_error with the path and reason. NEVER treat a read failure as an empty plan list, and NEVER write over a file you couldn't read. A missing file is the only case that counts as empty.

## Tools

Every tool returns structured JSON. Names and keys are the same on Windows, Mac, and Linux. Write tools that change a plan return the same object as `get_plan`, plus extra keys where noted. `revision` is an integer starting at 1. Pass it back as `expected_revision`.

A tool error is `isError: true` and `structuredContent` of the form below. `message` is never empty.

```json
{ "error": { "code": "invalid_argument", "message": "name is required." } }
```

`code` is one of `invalid_argument`, `not_found`, `revision_conflict`, `confirm_required`, `read_only`, `storage_error`, `unknown_tool`.

### list_terms

No arguments.

```json
{ "terms": [ { "term": "202710", "name": "Fall 2026" } ] }
```

### search_courses

`term` (code or label such as `Fall 2026`), optional `query`, `subject`, `number`, `attribute`, `no_prerequisites`, `no_corequisites`, `limit` (default 25, max 100), `level` (`all`, `undergrad`, or `grad`).

`level` combines with the other filters. Undergrad is a course number below 5000. Grad is 5000 and up. Omitted or `all` leaves every number in the results. Any other value is `invalid_argument`.

`no_prerequisites` and `no_corequisites` are booleans. When true, a course is kept only when its details were loaded and that requisite was omitted. Omitted on a loaded course means none. Null before the details load means unknown, and those courses are left out. A placement test still counts as a prerequisite.

```json
{
  "term": "202730",
  "courses": [
    { "subject": "CS", "number": "3000", "title": "Algorithms", "credits": 4, "credit_low": 4, "credit_high": null, "cps_only": null, "attributes": ["NCFQ"] }
  ],
  "truncated": false
}
```

`credits` is a number when min and max match, otherwise a string such as `1 to 8`. `credit_low` and `credit_high` are the range. `credit_high` is null when the course is fixed. `cps_only` is true when every known section is CPS-only, false when every section is known and one is not, and null when restrictions have not been loaded.

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
  "credit_low": 4,
  "credit_high": null,
  "cps_only": null,
  "attributes": [],
  "sections": [
    {
      "crn": "12345",
      "section": "01",
      "days": "MW",
      "times": "MW 9:50 AM to 11:30 AM",
      "location": "SN 108",
      "instructors": [{ "name": "Ada Lovelace", "email": "lovelace@northeastern.edu" }],
      "seats_remaining": 3,
      "seat_capacity": 40,
      "cps_only": null
    }
  ]
}
```

### check_schedule

`term`, `crns` (array of strings).

```json
{
  "term": { "term": "202710", "name": "Fall 2026" },
  "clashes": [ { "first": "10001", "second": "10002", "reason": "CS 3000 overlaps CS 3500" } ],
  "unknown_crns": ["99999"]
}
```

### suggest_schedule

`term`, `courses` (array of `{ "subject", "number", "crn"? }`), optional `constraints`:

`crn` locks that listing to one section. suggest_schedule will not substitute another section of the same course. The same subject and number may appear more than once only when every listing has a `crn` and the CRNs differ. A repeated course without those CRNs, or the same CRN twice, is `invalid_argument`.

* `no_classes_before` and `no_classes_after`: 24-hour `HH:MM` such as `08:00`, 12-hour `8:00 AM` or `8am`, or a JSON number of minutes from midnight. Anything else is `invalid_argument`.
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

Does not save. Pass either `plan_id` or `plan`, not both. `plan` is `{ "terms": [ { "term": "202710", "courses": [ { "subject": "CS", "number": "3000", "crn": "12345" } ] } ] }`. `term` is a Banner code such as `202710` or `202730`. Optional `crn` is kept on the course. A move does not replace it with another section of the same number. The same subject and number may be repeated in `plan` only when every listing has a different `crn`.

A course moves only into a term the plan already has. The target term's bundled catalog must offer the course, prerequisite order within the plan must be kept, and it never adds a semester. A chosen CRN moves with the course and is not replaced or cleared. A course is offered in a term when that term's bundled catalog has at least one section of it. Prerequisites that are not on the plan do not block the move.

```json
{
  "moves": [ { "subject": "CS", "number": "3000", "from_term": "202710", "to_term": "202730", "reason": "CS 3000 is not offered in Fall 2026. Spring 2027 offers it, and the move keeps prerequisite order without adding a semester." } ],
  "unresolved": [ { "subject": "CS", "number": "1990", "reason": "CS 1990 is not offered in Fall 2026, and no other term already on the plan offers it." } ],
  "plan": { "terms": [ { "term": "202730", "name": "Spring 2027", "courses": [ { "subject": "CS", "number": "3000", "title": "Algorithms" } ] } ] }
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
      "revision": 1
    }
  ]
}
```

### get_plan

`plan_id`. Same fields as one list entry, with full courses and `validation`. A chosen section adds `crn` and `section`.

```json
{
  "validation": {
    "clashes": [ { "first": "10001", "second": "10002", "reason": "overlap" } ],
    "not_offered": [ { "subject": "CS", "number": "3000", "term": "Fall 2026" } ],
    "duplicates": [
      {
        "subject": "CS",
        "number": "4990",
        "message": "CS 4990 appears more than once. Each copy still counts."
      }
    ]
  }
}
```

### create_plan

`name`, optional `terms` (a list of term strings such as `202710` and `202730`). Returns `get_plan`. `revision` starts at 1.

### rename_plan

`plan_id`, `name`, `expected_revision`. Returns `get_plan`.

### add_course

`plan_id`, `term`, `subject`, `number`, optional `crn`, `expected_revision`, optional `credits`.

The course must exist in a bundled catalog. A CRN must be a section of that course in that term. The same subject and number may be added again, including without a CRN and including the same CRN. When the copies are not separate topics, `validation.duplicates` names the course and says each copy still counts. Different CRNs are not a warning, and a title containing "topic" is not a warning when neither row has a CRN. Each course object includes `id`, the row a substitution names, plus `credits` (the chosen value), `credit_low`, `credit_high` (null when the course is fixed), and `cps_only`. A known course may be placed in a term that does not offer it. `get_plan` then reports `not_offered`. The linked schedule keeps one row per degree-plan row. Every credited copy counts in the audit.

Omitted `credits` uses Banner's default when that default sits inside the range, otherwise the low end. A variable course rejects a value that is not a whole number inside the range. A fixed course rejects a value other than its own credit count. Each copy keeps its own chosen value.

### remove_course

`plan_id`, `term`, `subject`, `number`, `expected_revision`. Destructive.

### move_course

`plan_id`, `subject`, `number`, `from_term`, `to_term`, `expected_revision`.

A chosen CRN stays on the course. `move_course` does not clear it when the destination catalog uses a different section. Two listings may land in one term. When the copies are not separate topics, `validation.duplicates` names the course and says each copy still counts. `move_course` moves the first row of that subject and number.

### set_credits

`plan_id`, `term`, `subject`, `number`, `credits`, `expected_revision`, optional `crn`.

Sets the chosen credits on one row. Hours totals use that value. When the term lists the course more than once, `crn` names the row and is required. A missing course is `not_found`. A fixed-credit course, or a value outside the range, is `invalid_argument`. The linked schedule row keeps the same value. Rows are not merged.

### set_section

`plan_id`, `term`, `subject`, `number`, `crn`, `expected_revision`. The course must already be in the term. This is the tool that changes a chosen section. One listing is updated to `crn`. When several listings share the subject and number, `set_section` fills the single listing that has no CRN, or does nothing when `crn` is already selected. A new CRN with several chosen sections is `invalid_argument` and does not retarget one of them. The plan is left unchanged, revision included, when that CRN is already selected.

### apply_reorganize

`plan_id`, `expected_revision`. Destructive. Runs `reorganize_plan` and saves when there is at least one move. `from_term` and `to_term` are Banner codes such as `202710` and `202730`. A result with no moves leaves the file and the revision unchanged. Returns `get_plan` plus `moves` and `unresolved`.

### delete_plan

`plan_id`, `expected_revision`, `confirm`. Destructive. Refuses unless `confirm` is true.

```json
{ "deleted": true, "plan_id": "00000000-0000-0000-0000-000000000000" }
```

## Annotations

Read tools set `readOnlyHint`: `list_terms`, `search_courses`, `get_course`, `check_schedule`, `suggest_schedule`, `reorganize_plan`, `list_plans`, `get_plan`.

`delete_plan`, `remove_course`, and `apply_reorganize` set `destructiveHint`.

Every tool sets `openWorldHint` false. Nothing here calls the network.
