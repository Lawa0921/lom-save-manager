using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;

namespace LoMSaveManager
{
    /// <summary>
    /// Manual save-slot keys: "001".."999" and "1000".."9999" (the game's own Save_&lt;key&gt;.dat naming).
    /// Anything else ("auto", "universe", "act1", ...) is not a manual slot.
    /// </summary>
    public static class SlotKey
    {
        public const int MaxManualSlot = 9999;

        private static readonly Regex ManualPattern = new Regex(
            @"\A(?:00[1-9]|0[1-9][0-9]|[1-9][0-9]{2,3})\z",
            RegexOptions.CultureInvariant);

        public static readonly IComparer<string> NumericOrder = new NumericOrderComparer();

        public static bool IsManual(string key)
        {
            return key != null && ManualPattern.IsMatch(key);
        }

        /// <summary>Canonical key for slot n, same as the game's SetSlot(int): n.ToString().PadLeft(3, '0').</summary>
        public static string Format(int n)
        {
            if (n < 1 || n > MaxManualSlot)
                throw new ArgumentOutOfRangeException("n", n, "Manual slot must be 1.." + MaxManualSlot + ".");
            return n.ToString(CultureInfo.InvariantCulture).PadLeft(3, '0');
        }

        public static int Number(string key)
        {
            if (!IsManual(key))
                throw new ArgumentException("Not a manual slot key: " + (key ?? "<null>"), "key");
            return int.Parse(key, NumberStyles.None, CultureInfo.InvariantCulture);
        }

        /// <summary>Manual keys by number (so "1000" sorts after "999"), then any other key ordinally.</summary>
        private sealed class NumericOrderComparer : IComparer<string>
        {
            public int Compare(string x, string y)
            {
                var xManual = IsManual(x);
                var yManual = IsManual(y);
                if (xManual && yManual) return Number(x).CompareTo(Number(y));
                if (xManual) return -1;
                if (yManual) return 1;
                return string.CompareOrdinal(x, y);
            }
        }
    }
}
