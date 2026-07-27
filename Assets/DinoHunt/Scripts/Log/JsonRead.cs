using System;
using System.Globalization;

namespace DinoHunt.Logging
{
    /// <summary>
    /// Minimal reader for the flat JSON lines this project writes. The event log is the spine
    /// (GDD §13.2) and everything downstream — commentary, statistics, replay — reads it back,
    /// so the parse lives in one place rather than being re-implemented per consumer.
    ///
    /// Deliberately not a general JSON parser: JsonLine emits flat objects with no nesting or
    /// escapes, so field lookup is a substring scan.
    /// </summary>
    public static class JsonRead
    {
        /// <summary>Extract a field value (string or number) from an event line, or null if absent.</summary>
        public static string Field(string line, string key)
        {
            int i = line.IndexOf("\"" + key + "\":", StringComparison.Ordinal);
            if (i < 0) return null;
            i += key.Length + 3;
            if (i >= line.Length) return null;

            if (line[i] == '"')
            {
                int end = line.IndexOf('"', i + 1);
                return end < 0 ? null : line.Substring(i + 1, end - i - 1);
            }

            int j = i;
            while (j < line.Length && line[j] != ',' && line[j] != '}') j++;
            return line.Substring(i, j - i);
        }

        /// <summary>The "type" field — the event discriminator.</summary>
        public static string Type(string line) => Field(line, "type");

        public static float Number(string line, string key, float fallback = 0f)
        {
            string raw = Field(line, key);
            return raw != null && float.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out float v) ? v : fallback;
        }
    }
}
