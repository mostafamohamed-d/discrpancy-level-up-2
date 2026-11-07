using System;
using System.Collections.Generic;

namespace EtabsTextModel
{
    /// <summary>
    /// Hands back one shared string for every name that reads the same.
    ///
    /// The sections with a row per area repeat themselves enormously: a hundred and seventy
    /// thousand areas share thirty section names, fifty storeys and a handful of diaphragms.
    /// Keeping one string each rather than one per row is the difference between a few kilobytes
    /// and thirty megabytes held for as long as the caller holds the tables.
    ///
    /// It does not save the decoding - a string has to exist before it can be looked up - only
    /// the keeping. Which is why it is used on the fields that repeat and not on the labels,
    /// where every value is different and pooling would be pure cost.
    ///
    /// The cap is there so that being wrong about which fields repeat is merely useless rather
    /// than harmful: past it the pool stops growing and simply hands back what it was given.
    /// </summary>
    internal sealed class StringPool
    {
        private const int Most = 8192;

        private readonly Dictionary<string, string> shared =
            new Dictionary<string, string>(StringComparer.Ordinal);

        public string Share(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return text;
            }

            string already;
            if (shared.TryGetValue(text, out already))
            {
                return already;
            }

            if (shared.Count < Most)
            {
                shared[text] = text;
            }

            return text;
        }
    }
}
