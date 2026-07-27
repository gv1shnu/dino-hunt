using System;
using System.IO;
using System.Text;

namespace DinoHunt.Logging
{
    /// <summary>
    /// Appends event lines to a .jsonl file, one JSON object per line. AutoFlush is on so a
    /// crash mid-match still leaves a readable log (dev volumes are tiny). Runtime-only.
    /// </summary>
    public sealed class JsonlFileSink : IEventSink, IDisposable
    {
        private StreamWriter _writer;

        public string Path { get; }

        public JsonlFileSink(string path)
        {
            Path = path;
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path));
            _writer = new StreamWriter(path, append: false, Encoding.UTF8) { AutoFlush = true };
        }

        public void Write(string jsonLine)
        {
            _writer?.WriteLine(jsonLine);
        }

        public void Dispose()
        {
            _writer?.Dispose();
            _writer = null;
        }
    }
}
