using System.Collections.Generic;
using DinoHunt.Batch;
using DinoHunt.Logging;

namespace DinoHunt.View
{
    /// <summary>
    /// Turns the live event stream into readable text: per-team radio chatter and match commentary.
    /// A rule-based, event-driven reader of the event log (the M7 radio + M12 commentary systems in
    /// lightweight form). Pure presentation — it only reads events. Full perception-gated radio and
    /// LLM commentary arrive at their milestones.
    /// </summary>
    public sealed class MatchNarrator : IEventSink
    {
        private const int MaxLines = 4;

        public readonly List<string> BlueRadio = new List<string>();
        public readonly List<string> RedRadio = new List<string>();
        public readonly List<string> Commentary = new List<string>();

        public int BlueEggs { get; private set; }
        public int RedEggs { get; private set; }

        /// <summary>Winner once the match ends: "blue", "red", "raptors", or null for a draw.</summary>
        public string Winner { get; private set; }
        public string EndReason { get; private set; }
        public bool MatchOver { get; private set; }

        /// <summary>Full derived scoreboard, available once the match ends. Built from the log, never accumulated live.</summary>
        public MatchResult FinalResult { get; private set; }

        // The commentator needs the whole match, not just the last event, so every line is kept and
        // the scoreboard is derived from all of them at the end — the same derivation the batch
        // runner uses, so the on-screen numbers and the research numbers can never disagree.
        private readonly List<string> _allLines = new List<string>();

        // The raptor re-aggros constantly (agents keep coming to the nest); throttle its chatter so
        // it doesn't bury egg/death events. The event log still records every screech.
        private const int RaptorChatterCooldown = 90;
        private int _writeCount;
        private int _lastRaptorChatterAt = -1000;

        public void Write(string line)
        {
            string type = Field(line, "type");
            if (type == null) return;
            _allLines.Add(line);
            _writeCount++;

            if ((type == "raptor_aggro" || type == "raptor_screech")
                && _writeCount - _lastRaptorChatterAt < RaptorChatterCooldown)
                return;

            switch (type)
            {
                case "match_start":
                    Say(Commentary, "The teams move out. Eggs in the nest, raptor on the prowl.");
                    break;

                case "egg_pickup":
                {
                    string actor = Field(line, "actor");
                    Radio(actor, actor + ": got an egg — moving out.");
                    break;
                }
                case "egg_stolen":
                {
                    string actor = Field(line, "actor");
                    string from = Field(line, "from");
                    Radio(actor, actor + ": stole it off " + from + "!");
                    Say(Commentary, actor + " rips an egg away from " + from + "!");
                    break;
                }
                case "egg_drop":
                {
                    string actor = Field(line, "actor");
                    Radio(actor, actor + " dropped the egg!");
                    break;
                }
                case "egg_delivered":
                {
                    string actor = Field(line, "actor");
                    string team = Field(line, "team");
                    if (team == "blue") BlueEggs++; else RedEggs++;
                    Radio(actor, actor + ": egg's home!");
                    Say(Commentary, Cap(team) + " banks an egg.  Blue " + BlueEggs + " – " + RedEggs + " Red");
                    break;
                }
                case "death":
                {
                    string actor = Field(line, "actor");
                    string cause = Field(line, "cause");
                    string killer = Field(line, "killer");
                    Radio(actor, actor + " is down!");
                    if (cause == "raptor") Say(Commentary, "The raptor drags down " + actor + "!");
                    else Say(Commentary, killer + " drops " + actor + ".");
                    break;
                }
                case "radio_callout":
                {
                    string actor = Field(line, "actor");
                    string kind = Field(line, "kind");
                    // Radio brevity: short, factual, no repetition. Suppression happens upstream in
                    // the simulation, so anything reaching here is worth saying.
                    switch (kind)
                    {
                        case "enemy_spotted":
                            Radio(actor, actor + ": contact.");
                            break;
                        case "enemy_carrier_spotted":
                            Radio(actor, actor + ": carrier spotted!");
                            break;
                        case "egg_spotted":
                            Radio(actor, actor + ": loose egg.");
                            break;
                        case "raptor_spotted":
                            Radio(actor, actor + ": raptor.");
                            break;
                        case "reloading":
                            Radio(actor, actor + ": reloading!");
                            break;
                        case "enemy_down":
                            Radio(actor, actor + ": enemy down.");
                            break;
                        case "raptor_down":
                            Radio(actor, actor + ": raptor down!");
                            break;
                    }
                    break;
                }

                case "raptor_aggro":
                {
                    string target = Field(line, "target");
                    _lastRaptorChatterAt = _writeCount;
                    Radio(target, "raptor's locked onto " + target + "!");
                    break;
                }
                case "raptor_screech":
                    _lastRaptorChatterAt = _writeCount;
                    Say(BlueRadio, "— raptor's up at the nest! —");
                    Say(RedRadio, "— raptor's up at the nest! —");
                    Say(Commentary, "A screech splits the air — the nest is contested.");
                    break;

                case "raptor_death":
                {
                    string killer = Field(line, "killer");
                    Radio(killer, killer + ": raptor down! nest's a little safer.");
                    Say(Commentary, killer + " brings a raptor down — that's a serious ammo investment.");
                    break;
                }

                case "match_end":
                {
                    EndReason = Field(line, "reason");
                    Winner = Field(line, "winner");
                    MatchOver = true;
                    FinalResult = MatchStats.Derive(_allLines, 0, 0);

                    Say(Commentary, WinnerLine(Winner, EndReason));
                    Say(BlueRadio, Winner == "blue" ? "— that's the match. we take it. —" : "— match over. —");
                    Say(RedRadio, Winner == "red" ? "— that's the match. we take it. —" : "— match over. —");
                    break;
                }
            }
        }

        private static string WinnerLine(string winner, string reason)
        {
            switch (winner)
            {
                case "raptors":
                    return "BOTH TEAMS DOWN — the raptors kept the nest. The raptors win it.";
                case null:
                    return "That's the match — nothing left standing. A draw (" + reason + ").";
                default:
                    string how = reason == "team_wipe" ? "wiped them out"
                               : reason == "all_eggs_delivered" ? "took the eggs"
                               : "held the lead";
                    return "That's the match — " + Cap(winner) + " " + how + ".";
            }
        }

        /// <summary>
        /// Periodic situation report. Event-driven chatter alone leaves long silences during
        /// traversal, so the commentator also reads the live state of the whole match and tells the
        /// audience where things stand. Pure presentation — it only reads.
        /// </summary>
        public void StatusReport(int blueAlive, int redAlive, int raptorsAlive,
                                 string blueCarrier, string redCarrier, float secondsLeft)
        {
            if (MatchOver) return;

            // Lead with whatever is most dramatic right now, so the report is never filler.
            if (blueCarrier != null && redCarrier != null)
                Say(Commentary, $"Both teams have an egg up — {blueCarrier} and {redCarrier} are both running.");
            else if (blueCarrier != null)
                Say(Commentary, $"{blueCarrier} is carrying for Blue.  Blue {BlueEggs} – {RedEggs} Red");
            else if (redCarrier != null)
                Say(Commentary, $"{redCarrier} is carrying for Red.  Blue {BlueEggs} – {RedEggs} Red");
            else if (blueAlive != redAlive)
            {
                string up = blueAlive > redAlive ? "Blue" : "Red";
                Say(Commentary, $"{up} has the numbers, {blueAlive}v{redAlive}.  Blue {BlueEggs} – {RedEggs} Red");
            }
            else if (raptorsAlive == 0)
                Say(Commentary, $"The nest is clear — no raptors left. It's a straight fight now.  Blue {BlueEggs} – {RedEggs} Red");
            else if (secondsLeft < 60f)
                Say(Commentary, $"Under a minute.  Blue {BlueEggs} – {RedEggs} Red");
            else
                Say(Commentary, $"{blueAlive}v{redAlive}, {raptorsAlive} raptors up.  Blue {BlueEggs} – {RedEggs} Red");
        }

        private void Radio(string actorId, string message)
        {
            if (actorId != null && actorId.StartsWith("red")) Say(RedRadio, message);
            else Say(BlueRadio, message);
        }

        private static void Say(List<string> buffer, string message)
        {
            if (buffer.Count > 0 && buffer[buffer.Count - 1] == message) return; // collapse consecutive duplicates
            buffer.Add(message);
            if (buffer.Count > MaxLines) buffer.RemoveAt(0);
        }

        private static string Cap(string s) => string.IsNullOrEmpty(s) ? s : char.ToUpperInvariant(s[0]) + s.Substring(1);

        private static string Field(string line, string key) => JsonRead.Field(line, key);
    }
}
