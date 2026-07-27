namespace DinoHunt.Logging
{
    /// <summary>
    /// Destination for event-log lines. The Simulation emits semantic events as JSON lines to
    /// a sink; it never does file I/O itself. Runtime uses JsonlFileSink, tests use
    /// InMemoryEventSink, and NullEventSink discards (used when no logging is wired up).
    /// </summary>
    public interface IEventSink
    {
        void Write(string jsonLine);
    }

    /// <summary>Discards everything. Default when no sink is supplied.</summary>
    public sealed class NullEventSink : IEventSink
    {
        public void Write(string jsonLine) { }
    }

    /// <summary>Fans one event stream out to several sinks (e.g. the file log + a live narrator).</summary>
    public sealed class MultiSink : IEventSink
    {
        private readonly IEventSink[] _sinks;
        public MultiSink(params IEventSink[] sinks) => _sinks = sinks;

        public void Write(string jsonLine)
        {
            for (int i = 0; i < _sinks.Length; i++)
                _sinks[i]?.Write(jsonLine);
        }
    }
}
