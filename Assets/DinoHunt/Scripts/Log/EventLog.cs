using DinoHunt.Core;
using DinoHunt.Sim;
using UnityEngine;

namespace DinoHunt.Logging
{
    /// <summary>
    /// The spine of the project: a sparse, semantic, append-only stream of match events. Every
    /// line carries the sim time and match id; each event type adds its own fields. Statistics,
    /// replay, evolution fitness, and commentary are all downstream readers of this — nothing
    /// stores computed stats, they are derived from here.
    ///
    /// This begins at M3 (combat) and grows with every later system.
    /// </summary>
    public sealed class EventLog
    {
        private readonly IEventSink _sink;
        private readonly SimClock _clock;
        private readonly string _matchId;
        private readonly ulong _seed;
        private readonly JsonLine _j = new JsonLine();

        public EventLog(IEventSink sink, SimClock clock, string matchId, ulong seed)
        {
            _sink = sink;
            _clock = clock;
            _matchId = matchId;
            _seed = seed;
        }

        private JsonLine Head(string type) =>
            _j.Begin().Num("t", _clock.Time).Str("match", _matchId).Str("type", type);

        public void MatchStart(int teamSize, int eggs) =>
            _sink.Write(Head("match_start").Int("seed", (long)_seed).Int("team_size", teamSize).Int("eggs", eggs).End());

        public void Spawn(Agent a) =>
            _sink.Write(Head("spawn").Str("actor", Id(a)).Str("team", TeamName(a)).Pos("pos", a.Position).End());

        public void IntentChange(Agent a, string intent) =>
            _sink.Write(Head("intent_change").Str("actor", Id(a)).Str("intent", intent).End());

        public void ShotFired(Agent shooter, string weapon, Agent target) =>
            _sink.Write(Head("shot_fired").Str("actor", Id(shooter)).Str("weapon", weapon).Str("target", Id(target)).End());

        /// <summary>Shot at a non-agent target (a raptor), identified by its log id.</summary>
        public void ShotFired(Agent shooter, string weapon, string targetId) =>
            _sink.Write(Head("shot_fired").Str("actor", Id(shooter)).Str("weapon", weapon).Str("target", targetId).End());

        public void Damage(Agent target, float amount, string byId, float remainingHp) =>
            _sink.Write(Head("damage").Str("actor", Id(target)).Num("amount", amount).Str("by", byId).Num("hp", remainingHp).End());

        /// <summary>An agent's shot landing on a raptor. Feeds raptor-kill / assist attribution downstream.</summary>
        public void RaptorDamage(Raptor target, float amount, string byId, float remainingHp) =>
            _sink.Write(Head("raptor_damage").Str("actor", target.LogId).Num("amount", amount).Str("by", byId).Num("hp", remainingHp).End());

        public void Death(Agent victim, string cause, string killerId, string weapon, string carriedEgg)
        {
            var j = Head("death").Str("actor", Id(victim)).Str("cause", cause);
            if (killerId != null) j.Str("killer", killerId);
            if (weapon != null) j.Str("weapon", weapon);
            if (carriedEgg != null) j.Str("carrying", carriedEgg);
            _sink.Write(j.End());
        }

        public void RaptorSpawn(Raptor r) =>
            _sink.Write(Head("raptor_spawn").Str("actor", r.LogId).Pos("pos", r.Position).End());

        public void RaptorAggro(Raptor r, Agent target) =>
            _sink.Write(Head("raptor_aggro").Str("actor", r.LogId).Str("target", Id(target)).End());

        public void RaptorScreech(Raptor r) =>
            _sink.Write(Head("raptor_screech").Str("actor", r.LogId).Pos("pos", r.Position).End());

        public void RaptorDeath(Raptor r, string killerId) =>
            _sink.Write(Head("raptor_death").Str("actor", r.LogId).Str("killer", killerId).End());

        public void EggPickup(Agent a, Egg egg) =>
            _sink.Write(Head("egg_pickup").Str("actor", Id(a)).Str("team", TeamName(a)).Str("egg", egg.LogId).Pos("pos", a.Position).End());

        public void EggStolen(Agent a, Egg egg, Team fromTeam) =>
            _sink.Write(Head("egg_stolen").Str("actor", Id(a)).Str("team", TeamName(a)).Str("egg", egg.LogId).Str("from", fromTeam == Team.Blue ? "blue" : "red").End());

        public void EggDrop(Agent formerCarrier, Egg egg, Vector3 pos) =>
            _sink.Write(Head("egg_drop").Str("actor", Id(formerCarrier)).Str("egg", egg.LogId).Pos("pos", pos).End());

        /// <summary>An agent perceiving something worth calling out — enemy/carrier/dropped-egg spotted (M7).</summary>
        public void RadioCallout(Agent a, string kind, Vector3 pos) =>
            _sink.Write(Head("radio_callout").Str("actor", Id(a)).Str("team", TeamName(a)).Str("kind", kind).Pos("pos", pos).End());

        public void EggDelivered(Agent a, Egg egg, bool stolen) =>
            _sink.Write(Head("egg_delivered").Str("actor", Id(a)).Str("team", TeamName(a)).Str("egg", egg.LogId).Bool("stolen", stolen).End());

        public void MatchEnd(string reason, string winner)
        {
            var j = Head("match_end").Str("reason", reason);
            if (winner != null) j.Str("winner", winner);
            _sink.Write(j.End());
        }

        private static string Id(Agent a) => TeamName(a) + "_" + a.Id;
        private static string TeamName(Agent a) => a.Team == Team.Blue ? "blue" : "red";
    }
}
