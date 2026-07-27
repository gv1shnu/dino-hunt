using UnityEngine;

namespace DinoHunt.Sim
{
    /// <summary>
    /// The Utility AI: each tick it scores the available actions against the world state, weighted
    /// by the agent's personality, and returns the highest-scoring one. Deterministic — pure float
    /// math, no RNG. Personality is where behavioural differences live; identical logic plus
    /// different weights must produce visibly different agents.
    ///
    /// Two properties are load-bearing and were both measured, not assumed (see docs/RESEARCH.md):
    ///
    /// 1. LEVERAGE. Every trait must decide something. If situation modifiers swamp the weights,
    ///    a cautious agent and a reckless one play identically and research question Q1 is dead on
    ///    arrival. Each trait therefore owns a primary action outright:
    ///        aggression → Engage / HuntRaptor      greed → GrabEgg / DeliverEgg / Intercept
    ///        caution    → Retreat / FleeRaptor     teamplay → Regroup / Escort
    ///        patience   → Hold
    ///
    /// 2. SURVIVAL. Agents must be able to lose a fight without dying in it. Perfect aim and long
    ///    range mean any stand-up trade is mutual destruction, so engagement is priced against the
    ///    LOCAL FORCE RATIO: outnumbered agents disengage, supported agents press. This is what
    ///    turns a firefight into a decision instead of a coin-flip.
    /// </summary>
    public sealed class UtilityBrain : IAgentBrain
    {
        private const float RaptorDangerRadius = 24f;
        private const float LowHealthFraction = 0.45f;
        private const float IsolationRadius = 30f;
        private const float SupportRadius = 45f;   // teammates this close count as backup
        private const float FleeDistance = 40f;
        private const float EscortRadius = 90f;    // won't cross the map to babysit a carrier

        private static readonly Personality Default = new Personality();

        public Decision Decide(Agent a, WorldView w)
        {
            Personality p = a.Personality ?? Default;

            bool carrying = a.CarriedEgg != null;
            float hf = a.Health / Mathf.Max(1f, a.MaxHealth);
            bool wounded = hf < LowHealthFraction;

            Agent enemy = w.NearestEnemy(a, out _);
            Agent carrierTarget = w.NearestVisibleEnemyCarrier(a, out _);
            Raptor raptor = w.NearestRaptor(a, out float dRaptor);
            Agent mate = w.NearestTeammate(a, out float dMate);
            Vector3 ownBase = w.OwnBase(a);

            // Local tactical picture.
            float force = w.ForceRatio(a, SupportRadius);      // >1 supported, <1 outnumbered
            bool outnumbered = force < 1f;
            float pressure = Mathf.Clamp01(1f - force);        // 0 when even or better, →1 when badly outnumbered
            bool matesNear = w.TeammatesWithin(a, SupportRadius) > 0;

            bool raptorVisible = raptor != null && dRaptor < RaptorDangerRadius;
            float raptorProx = raptorVisible ? Mathf.Clamp01(1f - dRaptor / RaptorDangerRadius) : 0f;
            bool raptorHeard = w.RaptorAlertActive(a);
            bool raptorThreat = raptorVisible || raptorHeard;

            var best = new Decision { Type = ActionType.Hold, Destination = a.Position, Intent = "holding" };
            float bestScore = 0.10f + p.patience * 0.30f;

            // ---- raptor: run or fight ----
            if (raptorVisible)
            {
                float flee = (0.4f + p.caution * 1.9f) * raptorProx + (carrying ? 0.7f * raptorProx : 0f);
                Vector3 away = a.Position - raptor.Position; away.y = 0f;
                Vector3 fleeDest = a.Position + (away.sqrMagnitude > 1e-4f ? away.normalized : (ownBase - a.Position).normalized) * FleeDistance;
                Consider(ref best, ref bestScore, flee, ActionType.FleeRaptor, null, fleeDest, "fleeing the raptor!");

                // Hunting is a team investment (a raptor costs 2x an agent in bullets). Solo attempts
                // are permitted and usually fatal — a deliberate design choice, see docs/DECISIONS.md.
                if (!carrying)
                {
                    float raptorHf = raptor.Health / Mathf.Max(1f, raptor.MaxHealth);
                    float hunt = 0.10f + p.aggression * 1.2f;
                    hunt += matesNear ? p.teamplay * 1.0f : -0.40f;
                    hunt += (1f - raptorHf) * 0.8f;
                    if (wounded) hunt -= p.caution * 1.2f;
                    ConsiderRaptor(ref best, ref bestScore, hunt, raptor, "hunting the raptor");
                }
            }
            else if (raptorHeard)
            {
                // A scream somewhere in the sector should colour the decision, not veto the heist.
                float s = 0.08f + p.caution * 0.50f;
                Consider(ref best, ref bestScore, s, ActionType.FleeRaptor, null, ownBase, "raptor's out there — pulling back");
            }

            // ---- objective ----
            if (carrying)
            {
                float s = 0.80f + p.greed * 0.80f;
                if (wounded) s += p.caution * 1.2f + (1f - hf) * 0.8f;  // get it home before you drop it
                if (raptorThreat) s += p.caution * 0.5f;
                if (outnumbered) s += p.caution * 0.6f * pressure;
                Consider(ref best, ref bestScore, s, ActionType.DeliverEgg, null, ownBase, "delivering the egg");
            }
            else if (w.AnyEggInNest())
            {
                float s = 0.25f + p.greed * 1.6f;
                if (raptorVisible) s -= p.caution * 1.4f + raptorProx;
                else if (raptorHeard) s -= p.caution * 0.35f;
                Consider(ref best, ref bestScore, s, ActionType.GrabEgg, null, w.Points.Nest, "going for an egg");
            }

            // ---- combat ----
            if (enemy != null)
            {
                float enemyHf = enemy.Health / Mathf.Max(1f, enemy.MaxHealth);
                float s = 0.15f + p.aggression * 1.7f;
                s += (force - 1f) * 0.8f;                 // press an advantage, break off when losing one
                if (enemyHf < 0.5f) s += 0.5f;            // finish the wounded
                if (wounded) s -= p.caution * 1.6f;
                if (carrying) s -= 0.6f;                  // carriers would rather run
                Consider(ref best, ref bestScore, s, ActionType.Engage, enemy, enemy.Position, "engaging " + Name(enemy));
            }

            // Intercepting a carrier is greed expressed through violence — worth more than a normal
            // kill because kills are worth zero points and a stolen egg is worth two.
            if (carrierTarget != null && !carrying)
            {
                float s = 0.20f + p.greed * 1.2f + p.aggression * 0.7f;
                s += (force - 1f) * 0.5f;
                if (wounded) s -= p.caution * 1.3f;
                Consider(ref best, ref bestScore, s, ActionType.Intercept, carrierTarget, carrierTarget.Position,
                         "intercepting " + Name(carrierTarget));
            }

            // ---- self-preservation ----
            if (wounded || outnumbered)
            {
                float s = p.caution * 1.8f * (1f - hf) + p.caution * 1.2f * pressure;
                if (carrying) s *= 0.5f; // the egg is the priority; DeliverEgg already handles fleeing home
                Consider(ref best, ref bestScore, s, ActionType.Retreat, null, ownBase,
                         wounded ? "retreating, hurt" : "falling back, outnumbered");
            }

            // ---- team ----
            Agent friendCarrier = w.NearestCarryingTeammate(a, out float dCarrier);
            if (friendCarrier != null && !carrying && dCarrier < EscortRadius)
            {
                // Escort exists only because carriers are slow and raptor-flagged (GDD §11.4).
                float s = 0.15f + p.teamplay * 1.6f;
                if (outnumbered) s += p.teamplay * 0.4f;
                Consider(ref best, ref bestScore, s, ActionType.Escort, null, friendCarrier.Position,
                         "escorting " + Name(friendCarrier));
            }

            // ---- acting on what the team said ----
            //
            // Only when nothing is actually in view: a radio report is a substitute for perception,
            // never an override of it. These positions are snapshots and may already be wrong, which
            // is deliberate — an agent confidently walking to where an enemy *was* is the single
            // best moment this design produces (GDD §7.6).
            if (enemy == null && w.HasFreshEnemyReport(a) && !carrying)
            {
                float s = 0.12f + p.teamplay * 0.9f + p.aggression * 0.6f;
                Consider(ref best, ref bestScore, s, ActionType.Investigate, null, a.HeardEnemyPos, "moving on a callout");
            }

            if (!carrying && w.HasFreshEggReport(a))
            {
                float s = 0.10f + p.greed * 1.1f + p.teamplay * 0.4f;
                Consider(ref best, ref bestScore, s, ActionType.Investigate, null, a.HeardEggPos, "going for a called egg");
            }

            if (mate != null && dMate > IsolationRadius)
            {
                float iso = Mathf.Clamp01(dMate / (IsolationRadius * 2f));
                float s = p.teamplay * 1.5f * iso;
                if (raptorThreat) s += p.teamplay * 0.6f;   // isolation is what raptors hunt
                if (outnumbered) s += p.teamplay * 0.8f * pressure;
                Consider(ref best, ref bestScore, s, ActionType.Regroup, null, mate.Position, "regrouping");
            }

            return best;
        }

        private static void Consider(ref Decision best, ref float bestScore, float score,
                                     ActionType type, Agent target, Vector3 dest, string intent)
        {
            if (score <= bestScore) return;
            bestScore = score;
            best = new Decision { Type = type, TargetEnemy = target, Destination = dest, Intent = intent };
        }

        private static void ConsiderRaptor(ref Decision best, ref float bestScore, float score, Raptor target, string intent)
        {
            if (score <= bestScore) return;
            bestScore = score;
            best = new Decision { Type = ActionType.HuntRaptor, TargetRaptor = target, Destination = target.Position, Intent = intent };
        }

        private static string Name(Agent a) => (a.Team == Team.Blue ? "blue" : "red") + "_" + a.Id;
    }
}
