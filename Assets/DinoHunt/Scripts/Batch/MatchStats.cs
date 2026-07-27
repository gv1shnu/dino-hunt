using System.Collections.Generic;
using DinoHunt.Logging;

namespace DinoHunt.Batch
{
    /// <summary>Per-agent statistics for one match. Every field is derived from the event log, never stored during the sim.</summary>
    public sealed class AgentStats
    {
        public string Id;
        public string Team;

        public int EggsPickedUp;      // fresh grabs from the nest
        public int EggsStolen;        // taken from an enemy carrier's drop
        public int EggsDelivered;     // banked (fresh or stolen)
        public int EggsDropped;       // lost on death to an enemy

        public int Kills;
        public int DeathByEnemy;
        public int DeathByRaptor;
        public int RaptorsDowned;

        public int ShotsFired;
        public float DamageDealt;
        public int Callouts;

        public bool Survived;

        public int Deaths => DeathByEnemy + DeathByRaptor;

        /// <summary>Eggs delivered ÷ eggs taken. GDD §10.3 calls this the single best measure of an agent.</summary>
        public float DeliveryRate
        {
            get
            {
                int taken = EggsPickedUp + EggsStolen;
                return taken == 0 ? 0f : EggsDelivered / (float)taken;
            }
        }

        /// <summary>Raptor deaths ÷ total deaths. A readable greed dial — above ~0.5 means living too close to the nest.</summary>
        public float CauseOfDeathRatio => Deaths == 0 ? 0f : DeathByRaptor / (float)Deaths;

        /// <summary>Kills per death, counting a survivor as having died zero times.</summary>
        public float KD => Deaths == 0 ? Kills : Kills / (float)Deaths;
    }

    /// <summary>Outcome + derived statistics for a single match.</summary>
    public sealed class MatchResult
    {
        public ulong Seed;
        public string Winner;          // "blue", "red", or null for a draw
        public string EndReason;       // team_wipe / all_eggs_delivered / timeout / mutual_elimination / tick_cap
        public float DurationSeconds;
        public int Ticks;

        public int BlueEggs;
        public int RedEggs;
        public int RaptorsKilled;
        public int TotalDeaths;
        public int TotalShots;

        public readonly List<AgentStats> Agents = new List<AgentStats>();

        public AgentStats Agent(string id)
        {
            for (int i = 0; i < Agents.Count; i++)
                if (Agents[i].Id == id) return Agents[i];
            var s = new AgentStats { Id = id, Team = id != null && id.StartsWith("red") ? "red" : "blue", Survived = true };
            Agents.Add(s);
            return s;
        }
    }

    /// <summary>
    /// Turns a raw event stream into a MatchResult. This is the "statistics are never stored,
    /// they are derived from the log" rule (GDD §13.2) made concrete — and it is also the
    /// measurement apparatus for research question Q1, so it lives in production code rather
    /// than in an analysis script.
    /// </summary>
    public static class MatchStats
    {
        public static MatchResult Derive(IReadOnlyList<string> lines, ulong seed, int ticks)
        {
            var r = new MatchResult { Seed = seed, Ticks = ticks, EndReason = "tick_cap" };

            for (int i = 0; i < lines.Count; i++)
            {
                string line = lines[i];
                string type = JsonRead.Type(line);
                if (type == null) continue;

                switch (type)
                {
                    case "spawn":
                        r.Agent(JsonRead.Field(line, "actor")).Team = JsonRead.Field(line, "team");
                        break;

                    case "shot_fired":
                        r.Agent(JsonRead.Field(line, "actor")).ShotsFired++;
                        r.TotalShots++;
                        break;

                    case "damage":
                    {
                        string by = JsonRead.Field(line, "by");
                        if (by != null && by.StartsWith("raptor")) break; // raptor claws aren't an agent's doing
                        r.Agent(by).DamageDealt += JsonRead.Number(line, "amount");
                        break;
                    }

                    case "raptor_damage":
                        r.Agent(JsonRead.Field(line, "by")).DamageDealt += JsonRead.Number(line, "amount");
                        break;

                    case "death":
                    {
                        var victim = r.Agent(JsonRead.Field(line, "actor"));
                        victim.Survived = false;
                        string cause = JsonRead.Field(line, "cause");
                        if (cause == "raptor") victim.DeathByRaptor++;
                        else
                        {
                            victim.DeathByEnemy++;
                            string killer = JsonRead.Field(line, "killer");
                            if (killer != null) r.Agent(killer).Kills++;
                        }
                        r.TotalDeaths++;
                        break;
                    }

                    case "raptor_death":
                    {
                        string killer = JsonRead.Field(line, "killer");
                        if (killer != null) r.Agent(killer).RaptorsDowned++;
                        r.RaptorsKilled++;
                        break;
                    }

                    case "egg_pickup":
                        r.Agent(JsonRead.Field(line, "actor")).EggsPickedUp++;
                        break;

                    case "egg_stolen":
                        r.Agent(JsonRead.Field(line, "actor")).EggsStolen++;
                        break;

                    case "egg_drop":
                        r.Agent(JsonRead.Field(line, "actor")).EggsDropped++;
                        break;

                    case "egg_delivered":
                    {
                        var a = r.Agent(JsonRead.Field(line, "actor"));
                        a.EggsDelivered++;
                        if (JsonRead.Field(line, "team") == "red") r.RedEggs++; else r.BlueEggs++;
                        break;
                    }

                    case "radio_callout":
                        r.Agent(JsonRead.Field(line, "actor")).Callouts++;
                        break;

                    case "match_end":
                        r.EndReason = JsonRead.Field(line, "reason");
                        r.Winner = JsonRead.Field(line, "winner");
                        r.DurationSeconds = JsonRead.Number(line, "t");
                        break;
                }
            }

            return r;
        }
    }
}
