using System.Collections.Generic;
using UnityEngine;

namespace DinoHunt.Sim
{
    /// <summary>
    /// Read-only view of the world handed to a brain each tick. Perception-gated as of M7: an
    /// agent only perceives enemies/raptors within sight range, its facing cone, and line of
    /// sight (GDD §7.1 — no hive mind, individual perception). Teammates stay fully known; treating
    /// "roughly know where your own squad is" as a deliberate simplification rather than building a
    /// stale-position memory for teammates too.
    ///
    /// Callers that don't care about perception gating (most existing crafted-state tests) can use
    /// the 4-arg constructor, which defaults to the old full-observability behavior.
    /// </summary>
    public sealed class WorldView
    {
        private const float EyeHeight = 1.2f;

        public readonly IReadOnlyList<Agent> Agents;
        public readonly IReadOnlyList<Raptor> Raptors;
        public readonly IReadOnlyList<Egg> Eggs;
        public readonly ArenaPoints Points;

        private readonly ILineOfSight _los;
        private readonly float _sightRange;
        private readonly float _fovCosHalfAngle;

        // Sound-based raptor threat: screeches carry across a sector (GDD §7.3/§8.4) and are not
        // blocked by facing or cover the way sight is. Simulation raises this when a screech fires;
        // brains can react to "something's hunting" even when the raptor itself isn't in view.
        //
        // The alert is bounded in BOTH time and space. An unbounded version is worse than useless:
        // with several raptors re-aggroing constantly it stays permanently true for every agent on
        // the map, and a permanent threat signal is indistinguishable from no signal at all — except
        // that it suppresses nest-robbing forever.
        private double _now;
        private double _raptorAlertUntil = double.NegativeInfinity;
        private Vector3 _raptorAlertPos;
        private float _raptorAlertRadius;

        public WorldView(IReadOnlyList<Agent> agents, IReadOnlyList<Raptor> raptors, IReadOnlyList<Egg> eggs, ArenaPoints points,
                          ILineOfSight los = null, float sightRange = 1000f, float fovDegrees = 360f)
        {
            Agents = agents;
            Raptors = raptors;
            Eggs = eggs;
            Points = points;
            _los = los ?? new AlwaysVisibleLineOfSight();
            _sightRange = sightRange;
            _fovCosHalfAngle = Mathf.Cos(Mathf.Clamp(fovDegrees, 0f, 360f) * 0.5f * Mathf.Deg2Rad);
        }

        /// <summary>Current simulation time, for reasoning about how stale a radio report is.</summary>
        public double Now => _now;

        /// <summary>How long a teammate's callout stays actionable.</summary>
        public float RadioMemorySeconds = 12f;

        /// <summary>Advance the perception clock. Call once per tick before use.</summary>
        public void BeginTick(double now) => _now = now;

        /// <summary>True if this agent was told about an enemy recently enough to still act on it.</summary>
        public bool HasFreshEnemyReport(Agent self) => _now - self.HeardEnemyAt <= RadioMemorySeconds;

        /// <summary>True if this agent was told about a dropped egg recently enough to still act on it.</summary>
        public bool HasFreshEggReport(Agent self) => _now - self.HeardEggAt <= RadioMemorySeconds;

        /// <summary>A screech went off at a position — heard within earshot regardless of facing or cover.</summary>
        public void RaiseRaptorAlert(Vector3 pos, double now, float alertSeconds, float radius)
        {
            _raptorAlertPos = pos;
            _raptorAlertUntil = now + alertSeconds;
            _raptorAlertRadius = radius;
        }

        /// <summary>True if this agent is close enough to have heard a still-ringing screech.</summary>
        public bool RaptorAlertActive(Agent self)
        {
            if (_now > _raptorAlertUntil) return false;
            Vector3 d = self.Position - _raptorAlertPos; d.y = 0f;
            return d.sqrMagnitude <= _raptorAlertRadius * _raptorAlertRadius;
        }

        /// <summary>Range + facing-cone + line-of-sight check. The perception primitive everything else here is built on.</summary>
        public bool CanPerceive(Agent self, Vector3 targetPos)
        {
            Vector3 to = targetPos - self.Position; to.y = 0f;
            float dist = to.magnitude;
            if (dist > _sightRange) return false;

            if (dist > 0.01f)
            {
                float cos = Vector3.Dot(self.Facing, to / dist);
                if (cos < _fovCosHalfAngle) return false;
            }

            return _los.CanSee(self.Position + Vector3.up * EyeHeight, targetPos + Vector3.up * EyeHeight);
        }

        public Vector3 OwnBase(Agent a) => a.Team == Team.Blue ? Points.BlueBase : Points.RedBase;

        public Agent NearestEnemy(Agent self, out float dist)
        {
            Agent best = null; float bestSq = float.PositiveInfinity;
            for (int i = 0; i < Agents.Count; i++)
            {
                Agent o = Agents[i];
                if (o == self || !o.IsAlive || o.Team == self.Team) continue;
                if (!CanPerceive(self, o.Position)) continue;
                float sq = (o.Position - self.Position).sqrMagnitude;
                if (sq < bestSq) { bestSq = sq; best = o; }
            }
            dist = best != null ? Mathf.Sqrt(bestSq) : -1f;
            return best;
        }

        /// <summary>Nearest perceived enemy currently carrying an egg — the priority sighting for callouts/intercept.</summary>
        public Agent NearestVisibleEnemyCarrier(Agent self, out float dist)
        {
            Agent best = null; float bestSq = float.PositiveInfinity;
            for (int i = 0; i < Agents.Count; i++)
            {
                Agent o = Agents[i];
                if (o == self || !o.IsAlive || o.Team == self.Team || o.CarriedEgg == null) continue;
                if (!CanPerceive(self, o.Position)) continue;
                float sq = (o.Position - self.Position).sqrMagnitude;
                if (sq < bestSq) { bestSq = sq; best = o; }
            }
            dist = best != null ? Mathf.Sqrt(bestSq) : -1f;
            return best;
        }

        public Agent NearestTeammate(Agent self, out float dist)
        {
            Agent best = null; float bestSq = float.PositiveInfinity;
            for (int i = 0; i < Agents.Count; i++)
            {
                Agent o = Agents[i];
                if (o == self || !o.IsAlive || o.Team != self.Team) continue;
                float sq = (o.Position - self.Position).sqrMagnitude;
                if (sq < bestSq) { bestSq = sq; best = o; }
            }
            dist = best != null ? Mathf.Sqrt(bestSq) : -1f;
            return best;
        }

        public Raptor NearestRaptor(Agent self, out float dist)
        {
            Raptor best = null; float bestSq = float.PositiveInfinity;
            for (int i = 0; i < Raptors.Count; i++)
            {
                Raptor r = Raptors[i];
                if (!r.IsAlive) continue;
                if (!CanPerceive(self, r.Position)) continue;
                float sq = (r.Position - self.Position).sqrMagnitude;
                if (sq < bestSq) { bestSq = sq; best = r; }
            }
            dist = best != null ? Mathf.Sqrt(bestSq) : -1f;
            return best;
        }

        /// <summary>Nearest perceived dropped (not carried, not in-nest) egg — for spotted-egg callouts.</summary>
        public Egg NearestVisibleDroppedEgg(Agent self, out float dist)
        {
            Egg best = null; float bestSq = float.PositiveInfinity;
            for (int i = 0; i < Eggs.Count; i++)
            {
                Egg e = Eggs[i];
                if (e.State != EggState.Dropped) continue;
                if (!CanPerceive(self, e.Position)) continue;
                float sq = (e.Position - self.Position).sqrMagnitude;
                if (sq < bestSq) { bestSq = sq; best = e; }
            }
            dist = best != null ? Mathf.Sqrt(bestSq) : -1f;
            return best;
        }

        public bool AnyEggInNest()
        {
            for (int i = 0; i < Eggs.Count; i++)
                if (Eggs[i].State == EggState.InNest) return true;
            return false;
        }

        // ---- local tactical picture ----
        //
        // Force awareness is what turns a firefight into a decision. Without it every agent that can
        // see an enemy simply shoots until someone dies, so evenly matched teams annihilate each
        // other and no personality trait can change the outcome. Comparing local numbers gives
        // aggression something to be brave *about* and caution something to run *from*.

        /// <summary>Living enemies this agent can actually perceive.</summary>
        public int VisibleEnemyCount(Agent self)
        {
            int n = 0;
            for (int i = 0; i < Agents.Count; i++)
            {
                Agent o = Agents[i];
                if (o == self || !o.IsAlive || o.Team == self.Team) continue;
                if (CanPerceive(self, o.Position)) n++;
            }
            return n;
        }

        /// <summary>Living teammates within supporting distance (position is always known — §7.1).</summary>
        public int TeammatesWithin(Agent self, float radius)
        {
            float r2 = radius * radius;
            int n = 0;
            for (int i = 0; i < Agents.Count; i++)
            {
                Agent o = Agents[i];
                if (o == self || !o.IsAlive || o.Team != self.Team) continue;
                if ((o.Position - self.Position).sqrMagnitude <= r2) n++;
            }
            return n;
        }

        /// <summary>
        /// Local force ratio: 1 = even, &gt;1 = this agent has support, &lt;1 = outnumbered.
        /// Counts the agent itself, so a lone agent facing one enemy reads exactly 1.
        /// </summary>
        public float ForceRatio(Agent self, float supportRadius)
        {
            float friends = 1f + TeammatesWithin(self, supportRadius);
            float foes = VisibleEnemyCount(self);
            return foes <= 0f ? 2f : friends / foes; // nobody visible reads as a comfortable advantage
        }

        /// <summary>Nearest living teammate who is carrying an egg — the escort target.</summary>
        public Agent NearestCarryingTeammate(Agent self, out float dist)
        {
            Agent best = null; float bestSq = float.PositiveInfinity;
            for (int i = 0; i < Agents.Count; i++)
            {
                Agent o = Agents[i];
                if (o == self || !o.IsAlive || o.Team != self.Team || o.CarriedEgg == null) continue;
                float sq = (o.Position - self.Position).sqrMagnitude;
                if (sq < bestSq) { bestSq = sq; best = o; }
            }
            dist = best != null ? Mathf.Sqrt(bestSq) : -1f;
            return best;
        }
    }
}
