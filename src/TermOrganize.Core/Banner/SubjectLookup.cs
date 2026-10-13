// Copyright (C) 2026 Term Organize contributors.
// SPDX-License-Identifier: AGPL-3.0-only

namespace TermOrganize.Core;

public static partial class BannerParsers
{
    /// <summary>
    /// Maps a Banner subject name or code to its code. Campus listings append a suffix
    /// such as "Computer Science - MMC"; those share the main subject's code.
    /// </summary>
    public sealed class SubjectLookup
    {
        private readonly Dictionary<string, string> _byName;

        public SubjectLookup(IEnumerable<Subject> subjects)
        {
            _byName = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var subject in subjects)
            {
                _byName[subject.Name.ToLowerInvariant()] = subject.Code;
                _byName[subject.Code.ToLowerInvariant()] = subject.Code;
            }
        }

        public string Code(string name)
        {
            var trimmed = name.Trim();
            if (_byName.TryGetValue(trimmed.ToLowerInvariant(), out var code)) return code;
            var match = System.Text.RegularExpressions.Regex.Match(trimmed, @"\s+-\s+[A-Z]{2,5}$");
            if (match.Success && _byName.TryGetValue(trimmed[..match.Index].ToLowerInvariant(), out code))
                return code;
            return trimmed;
        }
    }
}
