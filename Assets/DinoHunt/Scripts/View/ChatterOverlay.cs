using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace DinoHunt.View
{
    /// <summary>
    /// Draws the narrator's output in three panels: blue team radio (top-left), red team radio
    /// (top-right), and match commentary (bottom-center). IMGUI, pure presentation.
    ///
    /// Panel size is fixed regardless of content (queue depth is capped in MatchNarrator, so it
    /// never needs to grow). Each time a new line arrives the block eases in from a slight
    /// downward offset so it reads as chatter sliding in, not text popping in place.
    /// </summary>
    public sealed class ChatterOverlay : MonoBehaviour
    {
        private const float SlideInSeconds = 0.25f;
        private const float SlideDistance = 22f;

        private MatchNarrator _narrator;
        private GUIStyle _blueStyle, _redStyle, _commentaryStyle, _headerStyle;

        private string _blueLastSeen, _redLastSeen, _commentaryLastSeen;
        private float _blueAnimStart = -999f, _redAnimStart = -999f, _commentaryAnimStart = -999f;

        public void Init(MatchNarrator narrator) => _narrator = narrator;

        private void EnsureStyles()
        {
            if (_blueStyle != null) return;

            _blueStyle = Panel(new Color(0.70f, 0.84f, 1f), TextAnchor.UpperLeft);
            _redStyle = Panel(new Color(1f, 0.74f, 0.66f), TextAnchor.UpperRight);
            _commentaryStyle = Panel(new Color(0.95f, 0.92f, 0.78f), TextAnchor.LowerCenter);
            _headerStyle = new GUIStyle(_blueStyle) { fontStyle = FontStyle.Bold };
        }

        private static GUIStyle Panel(Color color, TextAnchor anchor) => new GUIStyle
        {
            fontSize = 13,
            wordWrap = true,
            alignment = anchor,
            normal = { textColor = color },
            padding = new RectOffset(7, 7, 5, 5)
        };

        private void OnGUI()
        {
            if (_narrator == null) return;
            EnsureStyles();
            GuiScale.Apply();
            float sw = GuiScale.Width, sh = GuiScale.Height;

            const float w = 270f, h = 140f, m = 12f;

            DrawPanel(new Rect(m, m, w, h), "BLUE RADIO", _narrator.BlueRadio, _blueStyle,
                new Color(0.15f, 0.22f, 0.35f, 0.55f), ref _blueLastSeen, ref _blueAnimStart);
            DrawPanel(new Rect(sw - w - m, m, w, h), "RED RADIO", _narrator.RedRadio, _redStyle,
                new Color(0.32f, 0.16f, 0.16f, 0.55f), ref _redLastSeen, ref _redAnimStart);

            float cw = 460f, ch = 95f;
            DrawPanel(new Rect((sw - cw) * 0.5f, sh - ch - m, cw, ch), "COMMENTARY", _narrator.Commentary,
                _commentaryStyle, new Color(0.12f, 0.12f, 0.10f, 0.6f), ref _commentaryLastSeen, ref _commentaryAnimStart);
        }

        private void DrawPanel(Rect rect, string title, List<string> lines, GUIStyle style, Color bg,
                                ref string lastSeen, ref float animStart)
        {
            var prev = GUI.color;
            GUI.color = bg;
            GUI.DrawTexture(rect, Texture2D.whiteTexture);
            GUI.color = prev;

            var titleRect = new Rect(rect.x, rect.y, rect.width, 20f);
            _headerStyle.alignment = style.alignment == TextAnchor.UpperRight ? TextAnchor.UpperRight : TextAnchor.UpperLeft;
            _headerStyle.normal.textColor = style.normal.textColor;
            GUI.Label(titleRect, title, _headerStyle);

            // Newest line arriving restarts the slide-in: the whole block eases up from below
            // into its resting position, like a message dropping into a chat log.
            string newest = lines.Count > 0 ? lines[lines.Count - 1] : null;
            if (newest != lastSeen)
            {
                lastSeen = newest;
                animStart = Time.time;
            }

            float t = Mathf.Clamp01((Time.time - animStart) / SlideInSeconds);
            float eased = 1f - (1f - t) * (1f - t); // ease-out
            float offsetY = Mathf.Lerp(-SlideDistance, 0f, eased);

            // Newest line on top, older lines further down — "sliding down" reads naturally here.
            var body = new StringBuilder();
            for (int i = lines.Count - 1; i >= 0; i--) body.Append(lines[i]).Append('\n');

            var bodyRect = new Rect(rect.x, rect.y + 20f, rect.width, rect.height - 23f);
            GUI.BeginGroup(bodyRect);
            var innerRect = new Rect(0f, offsetY, bodyRect.width, bodyRect.height + SlideDistance);
            GUI.Label(innerRect, body.ToString(), style);
            GUI.EndGroup();
        }
    }
}
