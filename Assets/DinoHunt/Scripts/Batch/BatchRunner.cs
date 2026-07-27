using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using DinoHunt.Arena;
using DinoHunt.Core;

namespace DinoHunt.Batch
{
    /// <summary>
    /// Runs many matches across a seed range and aggregates the results.
    ///
    /// This is the data pipeline the research questions depend on (GDD §13.1): without it there
    /// are only anecdotes. It also doubles as the regression harness — a batch that suddenly
    /// reports zero eggs delivered or a 100% draw rate is a behavioural bug report.
    /// </summary>
    public static class BatchRunner
    {
        /// <summary>Run <paramref name="matches"/> matches with consecutive seeds starting at <paramref name="firstSeed"/>.</summary>
        public static List<MatchResult> Run(MatchConfig template, ArenaLayout layout, int matches, ulong firstSeed = 1,
                                            Action<int, MatchResult> onMatchDone = null, int maxTicks = MatchRunner.DefaultMaxTicks)
        {
            var results = new List<MatchResult>(matches);
            for (int i = 0; i < matches; i++)
            {
                MatchConfig config = Clone(template);
                config.seed = firstSeed + (ulong)i;

                MatchResult r = MatchRunner.Run(config, layout, maxTicks);
                results.Add(r);
                onMatchDone?.Invoke(i, r);
            }
            return results;
        }

        /// <summary>Per-match copy so varying one field never mutates the caller's config.</summary>
        public static MatchConfig Clone(MatchConfig c)
        {
            MatchConfig copy = c.Clone();
            copy.logStateSnapshots = false; // batch never writes Tier-2 snapshots
            return copy;
        }

        // ---- reporting ----

        /// <summary>Human-readable summary of a batch. This is what gets eyeballed after a run.</summary>
        public static string Summarize(IReadOnlyList<MatchResult> results)
        {
            if (results.Count == 0) return "no matches run";

            int blueWins = 0, redWins = 0, raptorWins = 0, draws = 0;
            var reasons = new Dictionary<string, int>();
            float totalDuration = 0f;
            int totalBlueEggs = 0, totalRedEggs = 0, totalRaptors = 0, totalDeaths = 0;
            int deathsByRaptor = 0, deathsByEnemy = 0;
            float deliveryRateSum = 0f; int deliveryRateAgents = 0;

            foreach (var r in results)
            {
                if (r.Winner == "blue") blueWins++;
                else if (r.Winner == "red") redWins++;
                else if (r.Winner == "raptors") raptorWins++;
                else draws++;

                string reason = r.EndReason ?? "unknown";
                reasons[reason] = reasons.TryGetValue(reason, out int n) ? n + 1 : 1;

                totalDuration += r.DurationSeconds;
                totalBlueEggs += r.BlueEggs;
                totalRedEggs += r.RedEggs;
                totalRaptors += r.RaptorsKilled;
                totalDeaths += r.TotalDeaths;

                foreach (var a in r.Agents)
                {
                    deathsByRaptor += a.DeathByRaptor;
                    deathsByEnemy += a.DeathByEnemy;
                    if (a.EggsPickedUp + a.EggsStolen > 0) { deliveryRateSum += a.DeliveryRate; deliveryRateAgents++; }
                }
            }

            int n2 = results.Count;
            var sb = new StringBuilder();
            sb.AppendLine($"=== {n2} matches ===");
            sb.AppendLine($"blue {blueWins} ({Pct(blueWins, n2)})   red {redWins} ({Pct(redWins, n2)})   " +
                          $"raptors {raptorWins} ({Pct(raptorWins, n2)})   draw {draws} ({Pct(draws, n2)})");
            sb.AppendLine($"mean duration   {totalDuration / n2:F1}s");
            sb.AppendLine($"eggs delivered  {(totalBlueEggs + totalRedEggs) / (float)n2:F2} per match  (blue {totalBlueEggs}, red {totalRedEggs})");
            sb.AppendLine($"raptors killed  {totalRaptors / (float)n2:F2} per match");
            sb.AppendLine($"deaths          {totalDeaths / (float)n2:F2} per match  (by enemy {deathsByEnemy}, by raptor {deathsByRaptor})");
            if (deliveryRateAgents > 0)
                sb.AppendLine($"delivery rate   {deliveryRateSum / deliveryRateAgents:P0} (mean over agents who took an egg)");
            if (deathsByEnemy + deathsByRaptor > 0)
                sb.AppendLine($"cause-of-death  {deathsByRaptor / (float)(deathsByEnemy + deathsByRaptor):P0} raptor");

            sb.Append("end reasons     ");
            foreach (var kv in reasons) sb.Append($"{kv.Key} {kv.Value}   ");
            sb.AppendLine();
            return sb.ToString();
        }

        private static string Pct(int part, int total) => (part / (float)total).ToString("P0", CultureInfo.InvariantCulture);

        /// <summary>Per-agent rows, for clustering / analysis downstream. One line per agent per match.</summary>
        public static string ToCsv(IReadOnlyList<MatchResult> results)
        {
            var inv = CultureInfo.InvariantCulture;
            var sb = new StringBuilder();
            sb.AppendLine("seed,winner,end_reason,duration,agent,team,survived,eggs_picked,eggs_stolen,eggs_delivered,eggs_dropped," +
                          "kills,death_enemy,death_raptor,raptors_downed,shots,damage,callouts,delivery_rate,cod_ratio,kd");

            foreach (var r in results)
            {
                foreach (var a in r.Agents)
                {
                    sb.Append(r.Seed.ToString(inv)).Append(',')
                      .Append(r.Winner ?? "draw").Append(',')
                      .Append(r.EndReason).Append(',')
                      .Append(r.DurationSeconds.ToString("0.##", inv)).Append(',')
                      .Append(a.Id).Append(',')
                      .Append(a.Team).Append(',')
                      .Append(a.Survived ? 1 : 0).Append(',')
                      .Append(a.EggsPickedUp).Append(',')
                      .Append(a.EggsStolen).Append(',')
                      .Append(a.EggsDelivered).Append(',')
                      .Append(a.EggsDropped).Append(',')
                      .Append(a.Kills).Append(',')
                      .Append(a.DeathByEnemy).Append(',')
                      .Append(a.DeathByRaptor).Append(',')
                      .Append(a.RaptorsDowned).Append(',')
                      .Append(a.ShotsFired).Append(',')
                      .Append(a.DamageDealt.ToString("0.#", inv)).Append(',')
                      .Append(a.Callouts).Append(',')
                      .Append(a.DeliveryRate.ToString("0.###", inv)).Append(',')
                      .Append(a.CauseOfDeathRatio.ToString("0.###", inv)).Append(',')
                      .Append(a.KD.ToString("0.###", inv))
                      .AppendLine();
                }
            }
            return sb.ToString();
        }
    }
}
