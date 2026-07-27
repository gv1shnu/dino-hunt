using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace DinoHunt.Batch
{
    public enum HealthVerdict { Pass, Warn, Fail }

    public sealed class HealthCheck
    {
        public string Name;
        public HealthVerdict Verdict;
        public float Value;
        public string Detail;
    }

    /// <summary>
    /// Answers the question "is this build better or worse than the last one?"
    ///
    /// Batch statistics on their own do not answer it — 4.4 eggs per match means nothing without
    /// knowing what it was yesterday, or what good looks like. This turns a batch into a verdict
    /// against explicit criteria, so a change that quietly breaks the game is caught the same day.
    ///
    /// Every threshold below is a design claim, not a magic number, and each one exists because the
    /// failure it detects has actually happened at least once:
    ///
    ///   - eggs delivered near zero        → the objective loop is broken (this shipped once)
    ///   - deaths approaching agent count  → nobody disengages; fights are mutual destruction
    ///   - draws dominating                → matches do not resolve
    ///   - delivery rate near zero         → agents take eggs but never get home
    ///   - one team winning overwhelmingly → an asymmetry has crept into a symmetric game
    /// </summary>
    public static class MatchHealth
    {
        public static List<HealthCheck> Evaluate(IReadOnlyList<MatchResult> results, int agentsPerMatch)
        {
            var checks = new List<HealthCheck>();
            if (results == null || results.Count == 0) return checks;

            int n = results.Count;
            float eggs = 0f, deaths = 0f, duration = 0f, rateSum = 0f;
            int rateAgents = 0, decisive = 0, blue = 0, red = 0, eggsPossible = 0;

            foreach (var r in results)
            {
                eggs += r.BlueEggs + r.RedEggs;
                deaths += r.TotalDeaths;
                duration += r.DurationSeconds;
                if (r.Winner != null) decisive++;
                if (r.Winner == "blue") blue++;
                if (r.Winner == "red") red++;
                eggsPossible += 5;
                foreach (var a in r.Agents)
                    if (a.EggsPickedUp + a.EggsStolen > 0) { rateSum += a.DeliveryRate; rateAgents++; }
            }

            float eggsPer = eggs / n;
            float deathsPer = deaths / n;
            float decisiveFrac = decisive / (float)n;
            float rate = rateAgents > 0 ? rateSum / rateAgents : 0f;
            float durationPer = duration / n;

            // The objective must actually function. This is the check that would have caught the
            // perception bug that made every match a scoreless timeout.
            checks.Add(Check("objective works", eggsPer,
                eggsPer >= 2.5f ? HealthVerdict.Pass : eggsPer >= 1f ? HealthVerdict.Warn : HealthVerdict.Fail,
                $"{eggsPer:F2} eggs delivered per match (want ≥ 2.5 of 5)"));

            // Agents must be able to lose a fight without dying in it.
            float deathFrac = deathsPer / Mathf.Max(1, agentsPerMatch);
            checks.Add(Check("agents survive", deathFrac,
                deathFrac <= 0.6f ? HealthVerdict.Pass : deathFrac <= 0.85f ? HealthVerdict.Warn : HealthVerdict.Fail,
                $"{deathsPer:F1} of {agentsPerMatch} agents die per match ({deathFrac:P0}; want ≤ 60%)"));

            checks.Add(Check("matches resolve", decisiveFrac,
                decisiveFrac >= 0.7f ? HealthVerdict.Pass : decisiveFrac >= 0.4f ? HealthVerdict.Warn : HealthVerdict.Fail,
                $"{decisiveFrac:P0} decisive (want ≥ 70%)"));

            checks.Add(Check("carriers get home", rate,
                rate >= 0.4f ? HealthVerdict.Pass : rate >= 0.2f ? HealthVerdict.Warn : HealthVerdict.Fail,
                $"{rate:P0} delivery rate (want ≥ 40%)"));

            // Symmetric game, symmetric cast: a large persistent skew means something is unfair.
            int decided = blue + red;
            float skew = decided > 0 ? Mathf.Abs(blue - red) / (float)decided : 0f;
            checks.Add(Check("teams balanced", skew,
                skew <= 0.35f ? HealthVerdict.Pass : skew <= 0.6f ? HealthVerdict.Warn : HealthVerdict.Fail,
                $"blue {blue} / red {red} — skew {skew:P0} (want ≤ 35%)"));

            checks.Add(Check("watchable length", durationPer,
                durationPer >= 45f && durationPer <= 400f ? HealthVerdict.Pass : HealthVerdict.Warn,
                $"{durationPer:F0}s mean (want 45–400s)"));

            return checks;
        }

        private static HealthCheck Check(string name, float value, HealthVerdict v, string detail) =>
            new HealthCheck { Name = name, Value = value, Verdict = v, Detail = detail };

        /// <summary>Printable report. Any FAIL means the build is worse than it should be, whatever the other numbers say.</summary>
        public static string Report(IReadOnlyList<MatchResult> results, int agentsPerMatch)
        {
            var checks = Evaluate(results, agentsPerMatch);
            var sb = new StringBuilder();
            int fails = 0, warns = 0;

            sb.AppendLine("--- health ---");
            foreach (var c in checks)
            {
                if (c.Verdict == HealthVerdict.Fail) fails++;
                else if (c.Verdict == HealthVerdict.Warn) warns++;
                string tag = c.Verdict == HealthVerdict.Pass ? "PASS" : c.Verdict == HealthVerdict.Warn ? "WARN" : "FAIL";
                sb.AppendLine($"  [{tag}] {c.Name,-18} {c.Detail}");
            }
            sb.AppendLine(fails > 0 ? $"  => {fails} FAILED — this build is broken, not merely different."
                        : warns > 0 ? $"  => {warns} warning(s) — worth a look."
                        : "  => healthy.");
            return sb.ToString();
        }

        /// <summary>
        /// Compare a batch against a previously recorded one. This is the actual answer to "is it
        /// better than last time" — a single run cannot tell you, only a delta can.
        /// </summary>
        public static string Compare(IReadOnlyList<MatchResult> baseline, IReadOnlyList<MatchResult> current, int agentsPerMatch)
        {
            var a = Evaluate(baseline, agentsPerMatch);
            var b = Evaluate(current, agentsPerMatch);
            var sb = new StringBuilder();

            sb.AppendLine("--- vs baseline ---");
            for (int i = 0; i < a.Count && i < b.Count; i++)
            {
                float delta = b[i].Value - a[i].Value;
                // "agents survive" and "teams balanced" are costs: lower is better.
                bool lowerIsBetter = b[i].Name == "agents survive" || b[i].Name == "teams balanced";
                string arrow = Mathf.Abs(delta) < 0.001f ? "  ="
                             : (delta > 0) == !lowerIsBetter ? " ▲ better" : " ▼ worse";
                sb.AppendLine($"  {b[i].Name,-18} {a[i].Value,8:F3} → {b[i].Value,8:F3}{arrow}");
            }
            return sb.ToString();
        }
    }
}
