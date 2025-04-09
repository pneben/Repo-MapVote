using System;
using System.Collections.Generic;
using System.Text;

namespace MapVote
{
    internal static class LevelColorDictionary
    {
        private static readonly Dictionary<string, string> _dictionary = new()
        {
            { MapVote.VOTE_RANDOM_LABEL, "#ff0000" },
            { "Level - Manor", "#A47551" },
            { "Level - Arctic", "#4FC3F7" },
            { "Level - Wizard", "#B066FF" },
            { "Level - Hospital", "#66D9D9" },
            { "Level - Stronghold", "#A56695" },
        };

        public static string GetColor(string key) => _dictionary.TryGetValue(key, out var value) ? value : "#ffffff";
    }
}
