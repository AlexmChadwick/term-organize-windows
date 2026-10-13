// Copyright (C) 2026 Term Organize contributors.
// SPDX-License-Identifier: AGPL-3.0-only

namespace TermOrganize.Core.Tests;

public class CourseAttributeRepositoryTests
{
    private sealed class FakeSections : IAttributeFetcher
    {
        private readonly Dictionary<string, Dictionary<CourseKey, HashSet<string>>> _byTerm;
        public List<(string Term, List<CourseKey> Courses)> Calls { get; } = [];
        public HashSet<string> Failing { get; set; } = new(StringComparer.Ordinal);

        public FakeSections(Dictionary<string, Dictionary<string, string[]>> byTerm)
        {
            _byTerm = byTerm.ToDictionary(
                pair => pair.Key,
                pair => pair.Value.ToDictionary(item => CourseKey.Parse(item.Key)!.Value, item => item.Value.ToHashSet(StringComparer.Ordinal)),
                StringComparer.Ordinal);
        }

        public Task<IReadOnlyList<Term>> TermsAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<Term>>(
                new[] { "202710", "202655", "202650", "202630", "202610", "202560", "202550", "202540", "202530", "202510" }
                    .Select(code => new Term(code, "")).ToList());

        public Task<IReadOnlyDictionary<CourseKey, IReadOnlySet<string>>> SectionAttributesAsync(
            string term, IReadOnlyList<CourseKey> courses, CancellationToken cancellationToken = default)
        {
            Calls.Add((term, courses.ToList()));
            if (Failing.Contains(term)) throw new HttpRequestException("503");
            var found = _byTerm.GetValueOrDefault(term) ?? [];
            IReadOnlyDictionary<CourseKey, IReadOnlySet<string>> result = found
                .Where(pair => courses.Contains(pair.Key))
                .ToDictionary(pair => pair.Key, pair => (IReadOnlySet<string>)pair.Value);
            return Task.FromResult(result);
        }
    }

    [Fact]
    public async Task SearchesRecentTermsUntilACourseHasSections()
    {
        var banner = new FakeSections(new Dictionary<string, Dictionary<string, string[]>>
        {
            ["202630"] = new() { ["CS 3386"] = ["NCFQ", "NCEI"] },
            ["202610"] = new() { ["HIST 1130"] = ["NCDD", "NCSI"], ["CS 3386"] = ["NCFQ"] },
        });
        var directory = Directory.CreateTempSubdirectory("attributes-").FullName;
        try
        {
            var repository = new CourseAttributeRepository(banner, directory);
            var keys = new[] { Key("ENGW 1111"), Key("CS 3386"), Key("HIST 1130"), Key("ZZZZ 9999") };
            var results = await repository.LookupAsync(keys, [Loaded()]);
            Assert.Equal(CourseAttributeRepository.Lookup.FoundCodes(["NCW1"], "202710"), results[Key("ENGW 1111")]);
            Assert.Equal(CourseAttributeRepository.Lookup.FoundCodes(["NCFQ", "NCEI"], "202630"), results[Key("CS 3386")]);
            Assert.Equal(CourseAttributeRepository.Lookup.FoundCodes(["NCDD", "NCSI"], "202610"), results[Key("HIST 1130")]);
            Assert.Equal(CourseAttributeRepository.Lookup.None, results[Key("ZZZZ 9999")]);
            Assert.DoesNotContain(banner.Calls, call => call.Term is "202710" or "202655");
            Assert.Equal(new[] { Key("HIST 1130"), Key("ZZZZ 9999") }, banner.Calls.First(call => call.Term == "202610").Courses);
            Assert.Equal(new[] { Key("ZZZZ 9999") }, banner.Calls.First(call => call.Term == "202560").Courses);

            var again = new CourseAttributeRepository(banner, directory);
            var calls = banner.Calls.Count;
            Assert.Equal(results, await again.LookupAsync(keys, [Loaded()]));
            Assert.Equal(calls, banner.Calls.Count);
        }
        finally
        {
            try { Directory.Delete(directory, recursive: true); } catch (IOException) { }
        }
    }

    [Fact]
    public async Task FailuresAreNotCached()
    {
        var banner = new FakeSections(new Dictionary<string, Dictionary<string, string[]>>
        {
            ["202610"] = new() { ["HIST 1130"] = ["NCDD"] },
        })
        { Failing = new HashSet<string>(StringComparer.Ordinal) { "202650" } };
        var directory = Directory.CreateTempSubdirectory("attributes-").FullName;
        try
        {
            var repository = new CourseAttributeRepository(banner, directory);
            var key = Key("HIST 1130");
            Assert.Equal(new Dictionary<CourseKey, CourseAttributeRepository.Lookup> { [key] = CourseAttributeRepository.Lookup.Error }, await repository.LookupAsync([key]));
            banner.Failing.Clear();
            Assert.Equal(
                new Dictionary<CourseKey, CourseAttributeRepository.Lookup> { [key] = CourseAttributeRepository.Lookup.FoundCodes(["NCDD"], "202610") },
                await repository.LookupAsync([key]));
        }
        finally
        {
            try { Directory.Delete(directory, recursive: true); } catch (IOException) { }
        }
    }

    private static CourseKey Key(string code) => CourseKey.Parse(code)!.Value;

    private static CatalogIndex Loaded()
    {
        var key = Key("ENGW 1111");
        var course = new Course
        {
            Subject = key.Subject,
            Number = key.Number,
            Title = "First-Year Writing",
            MinCredits = 4,
            MaxCredits = 4,
            Attributes = ["NCW1"],
            DetailsLoaded = true,
        };
        var section = new CourseSection
        {
            Crn = "10001",
            Subject = key.Subject,
            Number = key.Number,
            Title = "First-Year Writing",
            SectionNumber = "01",
            PartOfTerm = "1",
            SeatCapacity = 20,
            SeatsRemaining = 2,
            Enrollment = 18,
            ClassType = "Lecture",
            Campus = "Boston",
            InstructionMethod = "Traditional",
            Attributes = ["NCW1"],
        };
        return CatalogIndex.Build(new TermCatalog
        {
            Term = new Term("202710", "Fall 2026 Semester"),
            Courses = [course],
            Sections = [section],
        });
    }
}
