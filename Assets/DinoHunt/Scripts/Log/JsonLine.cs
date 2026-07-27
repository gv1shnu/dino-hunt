using System.Globalization;
using System.Text;
using UnityEngine;

namespace DinoHunt.Logging
{
    /// <summary>
    /// Minimal, allocation-light JSON object builder for one event line. Deterministic:
    /// InvariantCulture formatting and fixed key order. Avoids JsonUtility's limitations
    /// (no polymorphism, no control over field order) and produces exactly the compact
    /// one-line-per-event format the GDD describes. Reused per event (single-threaded).
    /// </summary>
    public sealed class JsonLine
    {
        private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
        private readonly StringBuilder _sb = new StringBuilder(160);
        private bool _first;

        public JsonLine Begin()
        {
            _sb.Clear();
            _sb.Append('{');
            _first = true;
            return this;
        }

        private void Key(string k)
        {
            if (!_first) _sb.Append(',');
            _first = false;
            _sb.Append('"').Append(k).Append("\":");
        }

        public JsonLine Str(string key, string value)
        {
            Key(key);
            AppendEscaped(value);
            return this;
        }

        public JsonLine Num(string key, double value)
        {
            Key(key);
            _sb.Append(value.ToString("0.###", Inv));
            return this;
        }

        public JsonLine Int(string key, long value)
        {
            Key(key);
            _sb.Append(value.ToString(Inv));
            return this;
        }

        public JsonLine Bool(string key, bool value)
        {
            Key(key);
            _sb.Append(value ? "true" : "false");
            return this;
        }

        public JsonLine Pos(string key, Vector3 p)
        {
            Key(key);
            _sb.Append('[')
               .Append(p.x.ToString("0.##", Inv)).Append(',')
               .Append(p.y.ToString("0.##", Inv)).Append(',')
               .Append(p.z.ToString("0.##", Inv)).Append(']');
            return this;
        }

        public string End()
        {
            _sb.Append('}');
            return _sb.ToString();
        }

        private void AppendEscaped(string v)
        {
            _sb.Append('"');
            if (v != null)
            {
                foreach (char c in v)
                {
                    if (c == '"' || c == '\\') _sb.Append('\\').Append(c);
                    else if (c == '\n') _sb.Append("\\n");
                    else _sb.Append(c);
                }
            }
            _sb.Append('"');
        }
    }
}
