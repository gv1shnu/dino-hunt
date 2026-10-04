using System.Collections.Generic;
using DinoHunt.Batch;
using UnityEngine;

namespace DinoHunt.View
{
    /// <summary>
    /// End-of-match scoreboard: who won, and the per-agent numbers behind it.
    ///
    /// The columns are chosen to indict as much as to praise. Points measure egg work, kills measure
    /// combat, and they are reported side by side and never summed — so an agent with an excellent
    /// K/D and zero deliveries reads as having had a *bad* match, which in a normal shooter would top
    /// the board (GDD §10.2).
    ///
    /// Deaths are split by cause because that split is a personality fingerprint: a high raptor-death
    /// ratio means an agent living too close to the nest.
    /// </summary>
    public sealed class ScoreboardOverlay : MonoBehaviour
    {
        private MatchNarrator _narrator;
        private GUIStyle _title, _header, _row, _sub;

        public void Init(MatchNarrator narrator) => _narrator = narrator;

        private void EnsureStyles()
        {
            if (_title != null) return;

            _title = new GUIStyle
            {
                fontSize = 26,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter,
                normal = { textColor = Color.white }
            };
            _sub = new GUIStyle
            {
                fontSize = 13,
                alignment = TextAnchor.MiddleCenter,
                normal = { textColor = new Color(0.75f, 0.75f, 0.72f) }
            };
            _header = new GUIStyle
            {
                fontSize = 12,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleLeft,
                normal = { textColor = new Color(0.70f, 0.70f, 0.68f) }
            };
            _row = new GUIStyle
            {
                fontSize = 13,
                alignment = TextAnchor.MiddleLeft,
                normal = { textColor = Color.white }
            };
        }

        private void OnGUI()
        {
            if (_narrator == null || !_narrator.MatchOver) return;
            MatchResult r = _narrator.FinalResult;
            if (r == null) return;

            EnsureStyles();
            GuiScale.Apply();

            const float w = 720f, h = 420f;
            var panel = new Rect((GuiScale.Width - w) * 0.5f, (GuiScale.Height - h) * 0.5f, w, h);

            var prev = GUI.color;
            GUI.color = new Color(0.06f, 0.07f, 0.09f, 0.93f);
            GUI.DrawTexture(panel, Texture2D.whiteTexture);
            GUI.color = prev;

            // ---- winner ----
            _title.normal.textColor = WinnerColor(_narrator.Winner);
            GUI.Label(new Rect(panel.x, panel.y + 16f, w, 34f), WinnerText(_narrator.Winner), _title);
            GUI.Label(new Rect(panel.x, panel.y + 50f, w, 20f),
                $"{_narrator.EndReason}    ·    eggs  Blue {r.BlueEggs} – {r.RedEggs} Red    ·    " +
                $"{r.DurationSeconds:F0}s    ·    raptors killed {r.RaptorsKilled}", _sub);

            // ---- table ----
            float y = panel.y + 88f;
            float x = panel.x + 24f;
            DrawRow(x, y, "AGENT", "TEAM", "DELIV", "STOLE", "KILLS", "D:ENEMY", "D:RAPTOR", "RAPT", "DEL%", _header);
            y += 20f;

            GUI.color = new Color(1f, 1f, 1f, 0.15f);
            GUI.DrawTexture(new Rect(x, y, w - 48f, 1f), Texture2D.whiteTexture);
            GUI.color = prev;
            y += 6f;

            var ordered = new List<AgentStats>(r.Agents);
            // Eggs delivered first, then kills — the ordering states the priority.
            ordered.Sort((a, b) =>
            {
                int d = b.EggsDelivered.CompareTo(a.EggsDelivered);
                return d != 0 ? d : b.Kills.CompareTo(a.Kills);
            });

            foreach (var a in ordered)
            {
                _row.normal.textColor = a.Team == "red"
                    ? new Color(1f, 0.60f, 0.55f)
                    : new Color(0.62f, 0.80f, 1f);
                if (!a.Survived) _row.normal.textColor *= 0.62f; // the dead read dimmer

                DrawRow(x, y, a.Id + (a.Survived ? "" : " †"), a.Team,
                        a.EggsDelivered.ToString(), a.EggsStolen.ToString(), a.Kills.ToString(),
                        a.DeathByEnemy.ToString(), a.DeathByRaptor.ToString(),
                        a.RaptorsDowned.ToString(), $"{a.DeliveryRate:P0}", _row);
                y += 19f;
            }

            GUI.Label(new Rect(panel.x, panel.y + h - 28f, w, 18f),
                "delivery rate = eggs delivered ÷ eggs taken    ·    points and kills are never summed", _sub);
        }

        private static void DrawRow(float x, float y, string a, string b, string c, string d,
                                    string e, string f, string g, string hh, string i, GUIStyle s)
        {
            GUI.Label(new Rect(x, y, 130f, 18f), a, s);
            GUI.Label(new Rect(x + 130f, y, 55f, 18f), b, s);
            GUI.Label(new Rect(x + 190f, y, 55f, 18f), c, s);
            GUI.Label(new Rect(x + 250f, y, 55f, 18f), d, s);
            GUI.Label(new Rect(x + 310f, y, 55f, 18f), e, s);
            GUI.Label(new Rect(x + 375f, y, 70f, 18f), f, s);
            GUI.Label(new Rect(x + 455f, y, 70f, 18f), g, s);
            GUI.Label(new Rect(x + 535f, y, 55f, 18f), hh, s);
            GUI.Label(new Rect(x + 595f, y, 60f, 18f), i, s);
        }

        private static string WinnerText(string winner)
        {
            switch (winner)
            {
                case "blue": return "BLUE WINS";
                case "red": return "RED WINS";
                case "raptors": return "THE RAPTORS WIN";
                default: return "DRAW";
            }
        }

        private static Color WinnerColor(string winner)
        {
            switch (winner)
            {
                case "blue": return new Color(0.50f, 0.75f, 1f);
                case "red": return new Color(1f, 0.52f, 0.46f);
                case "raptors": return new Color(0.72f, 0.86f, 0.42f);
                default: return new Color(0.85f, 0.85f, 0.82f);
            }
        }
    }
}
