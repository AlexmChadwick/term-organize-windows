// Copyright (C) 2026 Term Organize contributors.
// SPDX-License-Identifier: AGPL-3.0-only

using System.Reflection;
using System.Text;

namespace TermOrganize.Core;

/// <summary>
/// Appropriate Legal Notices for the GNU AGPL v3: copyright, no warranty,
/// permission to convey, and a way to read the license.
/// </summary>
public static class LegalText
{
    public const string AppName = "Term Organize";
    public const string AppCopyright = "Copyright (C) 2026 Term Organize contributors.";
    public const string UpstreamCopyright = "Copyright (C) Sandbox at Northeastern University and the SearchNEU contributors";
    public const string LicenseName = "GNU Affero General Public License v3.0";
    public const string LicenseGrant = "Term Organize is free software: you can redistribute it and modify it under the terms of the GNU Affero General Public License, version 3, as published by the Free Software Foundation.";
    public const string NoWarranty = "This program is distributed in the hope that it will be useful, but WITHOUT ANY WARRANTY; without even the implied warranty of MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. See the GNU Affero General Public License for more details.";
    public const string LicenseNotice = "You should have received a copy of the GNU Affero General Public License along with this program. If not, see https://www.gnu.org/licenses/.";
    public const string SourceCodeUrl = "https://github.com/AlexmChadwick/Northeastern-Term-Organize";
    public const string SourceOffer = "Term Organize is free software licensed under the GNU Affero General Public License v3.0. The Mac and iPhone app this Windows version was ported from is at " + SourceCodeUrl + ". The complete corresponding source for this Windows version is the repository you built or received it from.";
    public const string UnofficialNotice = "Not affiliated with, endorsed by, or sponsored by Northeastern University or Sandbox.";
    public const string PlatformName = "Windows";
    public const string SearchNeuSummary = "This app is an independent Windows adaptation of SearchNEU, the course search built by Sandbox at Northeastern University (sandboxnu). Portions of this app — including the Banner scraping logic, requisite parsing and schedule generation — are derived from SearchNEU's source code.";
    public const string PrivacyNotice = "No accounts, no analytics, no tracking. Your schedules, degree plans and watchlist are stored on this device. Degree audit PDFs are read on the device, and the student name and ID they contain stay on this device unless you include them in an export. The preference sentence is parsed on this device. The app only contacts nubanner.neu.edu and catalog.northeastern.edu to fetch public course and degree-requirement information. iCloud sync is a Mac and iPhone feature; on Windows, export and import the data file, or point a folder sync at it.";
    public const string DataNotice = "Course information is © Northeastern University and is retrieved from its public class search. Northeastern University and its marks belong to their owners and are used only to say where the data comes from.";
    public const string SearchNeuRepositoryUrl = "https://github.com/sandboxnu/searchneu";
    public const string SearchNeuWebsiteUrl = "https://searchneu.com";
    public const string LicenseUrl = "https://www.gnu.org/licenses/agpl-3.0.html";

    public static string? LoadLicenseText()
    {
        var assembly = typeof(LegalText).Assembly;
        var name = assembly.GetManifestResourceNames().FirstOrDefault(n => n.EndsWith("LICENSE.txt", StringComparison.Ordinal));
        if (name is null) return null;
        using var stream = assembly.GetManifestResourceStream(name);
        if (stream is null) return null;
        using var reader = new StreamReader(stream, Encoding.UTF8);
        return Reflow(reader.ReadToEnd());
    }

    public static string Reflow(string raw)
    {
        var paragraphs = new List<string>();
        var lines = new List<string>();
        void Flush()
        {
            if (lines.Count == 0) return;
            var indent = lines[0].TakeWhile(ch => ch == ' ').Count();
            var trimmed = lines.Select(line => line.Trim()).ToList();
            if (indent >= 10) paragraphs.Add(string.Join('\n', trimmed));
            else if (indent >= 4) paragraphs.Add("    " + string.Join(' ', trimmed));
            else paragraphs.Add(string.Join(' ', trimmed));
            lines.Clear();
        }
        foreach (var line in raw.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n').Split('\n'))
        {
            if (string.IsNullOrWhiteSpace(line)) Flush();
            else lines.Add(line);
        }
        Flush();
        return string.Join("\n\n", paragraphs);
    }
}
