using System.Collections.Generic;

namespace DinoHunt.Logging
{
    /// <summary>Collects event lines in memory. For tests and inspection.</summary>
    public sealed class InMemoryEventSink : IEventSink
    {
        public readonly List<string> Lines = new List<string>();

        public void Write(string jsonLine) => Lines.Add(jsonLine);

        /// <summary>Count lines whose event type matches (matches the "type":"..." field).</summary>
        public int Count(string type)
        {
            string needle = "\"type\":\"" + type + "\"";
            int n = 0;
            foreach (var line in Lines)
                if (line.Contains(needle)) n++;
            return n;
        }
    }
}
