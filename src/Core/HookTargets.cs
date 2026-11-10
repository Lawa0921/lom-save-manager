using System;
using System.Collections.Generic;

namespace LoMSaveManager
{
    /// <summary>One game member the mod needs, and whether the running game has it (with the expected signature).</summary>
    public struct HookTarget
    {
        public HookTarget(string name, bool found)
        {
            Name = name;
            Found = found;
        }

        /// <summary>Readable member name for the log, for example <c>SaveSystem.SaveGameData(String)</c>.</summary>
        public string Name { get; }

        public bool Found { get; }
    }

    /// <summary>The all-or-nothing check: patch only when every target is present.</summary>
    public static class HookTargets
    {
        /// <summary>Names of every target that was not found, in the given order. Empty means safe to patch.</summary>
        public static IReadOnlyList<string> Validate(IEnumerable<HookTarget> targets)
        {
            if (targets == null) throw new ArgumentNullException(nameof(targets));
            var missing = new List<string>();
            foreach (var target in targets)
                if (!target.Found) missing.Add(target.Name);
            return missing;
        }

        /// <summary>The single log line for a game that does not match: <c>LoM Save Manager disabled: missing A, B</c>.</summary>
        public static string DisabledMessage(IReadOnlyList<string> missing)
        {
            if (missing == null) throw new ArgumentNullException(nameof(missing));
            if (missing.Count == 0) throw new ArgumentException("Nothing is missing.", nameof(missing));
            return "LoM Save Manager disabled: missing " + string.Join(", ", missing);
        }
    }
}
