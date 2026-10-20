// Copyright (C) 2026 Term Organize contributors.
// SPDX-License-Identifier: AGPL-3.0-only

using TermOrganize.Core;

namespace TermOrganize.Core.Tests;

public class BundledCatalogTests
{
    [Fact]
    public void StartupOpensTheNewestBundledTerm()
    {
        Assert.Equal(["202710", "202730"], BundledCatalog.TermCodes());
        Assert.Equal("202730", BundledCatalog.DefaultTerm);
        Assert.Equal("202730", BundledCatalog.Load().Term.Code);
        Assert.Equal("202730", BundledCatalog.StartupTerm(null));
        Assert.Equal("202730", BundledCatalog.StartupTerm("199999"));
        Assert.Equal("202710", BundledCatalog.StartupTerm("202710"));
        Assert.Equal("202650", BundledCatalog.StartupTerm("202650", _ => true));
        Assert.Equal("202730", BundledCatalog.SelectedCode(loadedCode: null, savedCode: null));
        Assert.Equal("202730", BundledCatalog.SelectedCode("202730", "202710"));
        Assert.Equal("202710", BundledCatalog.SelectedCode(loadedCode: null, savedCode: "202710"));
    }

    [Fact]
    public void Spring2027GzipLoads()
    {
        var catalog = BundledCatalog.Load("202730");
        Assert.Equal("Spring 2027 Semester (View Only)", catalog.Term.BannerDescription);
        Assert.Equal(3_076, catalog.Courses.Count);
        Assert.Equal(8_326, catalog.Sections.Count);
        Assert.All(catalog.Courses, course => Assert.True(course.DetailsLoaded));
        Assert.Contains(catalog.Sections, section => section.Instructors?.Any(instructor => instructor.Email is null) == true);
        Assert.Contains(catalog.Sections, section => section.Instructors?.Any(instructor => instructor.Email is not null) == true);
    }

    [Fact]
    public void ReferenceLibraryLzfseMatchesTheGzipSnapshot()
    {
        var path = BundledCatalog.Resolve("term-202730.catalog");
        Assert.NotNull(path);
        var fromLzfse = CatalogArchive.Read(path);
        var fromGzip = BundledCatalog.Load("202730");
        Assert.Equal(fromGzip.Term.Code, fromLzfse.Term.Code);
        Assert.Equal(fromGzip.Term.BannerDescription, fromLzfse.Term.BannerDescription);
        Assert.Equal(fromGzip.Courses.Select(course => course.Code), fromLzfse.Courses.Select(course => course.Code));
        Assert.Equal(fromGzip.Sections.Select(section => section.Crn), fromLzfse.Sections.Select(section => section.Crn));
    }

    [Fact]
    public void Fall2026StillLoadsBesideSpring()
    {
        var fall = BundledCatalog.Load("202710");
        Assert.Equal("202710", fall.Term.Code);
        Assert.NotEqual(BundledCatalog.Load("202730").Courses.Count, fall.Courses.Count);
    }

    [Fact]
    public void PickerListsBothBundledTermsWhenBannerIsOffline()
    {
        var terms = BundledCatalog.PickerTerms(loaded: null, live: []);
        Assert.Equal(["202730", "202710"], terms.Select(term => term.Code));
        Assert.Equal("Spring 2027", terms[0].Name);
        Assert.Contains("Fall 2026", terms[1].Name);
    }

    [Fact]
    public void PickerKeepsALiveTermAndBothBundledSnapshots()
    {
        var loaded = BundledCatalog.Load("202730").Term;
        var live = new[] { new Term("202750", "Summer 2027 Semester"), new Term("202730", "Spring 2027 Semester") };
        var terms = BundledCatalog.PickerTerms(loaded, live);
        Assert.Equal("202730", terms[0].Code);
        Assert.Contains(terms, term => term.Code == "202710");
        Assert.Contains(terms, term => term.Code == "202750");
    }
}
