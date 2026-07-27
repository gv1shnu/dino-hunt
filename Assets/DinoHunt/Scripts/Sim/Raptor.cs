using DinoHunt.Core;
using UnityEngine;

namespace DinoHunt.Sim
{
    public enum RaptorState { Idle, Hunting, Returning }

    /// <summary>
    /// A raptor: neutral hostile, team-blind, leashed to the nest. Scripted in M5 (chase / leash /
    /// return home; no pack behavior yet). Deterministic — driven by fixed timestep and the same
    /// line-of-sight seam agents use.
    /// </summary>
    public sealed class Raptor
    {
        public readonly int Id;

        public Vector3 Position;
        public Vector3 Facing = Vector3.forward;
        public float Health;
        public float MaxHealth;
        public bool IsAlive;

        public RaptorState State;
        public Agent Target;
        public float AttackCooldown;
        public float LostSightTimer;

        /// <summary>Most recent agent to damage this raptor, and when (sim time). Feeds the low-priority
        /// "whoever last hurt it" target rule (GDD §8.3 #4) — shooting a raptor tugs its aggro toward you.</summary>
        public Agent LastAttacker;
        public double LastAttackerTime = double.NegativeInfinity;

        /// <summary>
        /// Approach arc claimed on the current hunt, in radians around the prey. Pack members claim
        /// distinct arcs so they enclose rather than queue up behind one another (GDD §8.5).
        /// </summary>
        public float ApproachAngle;

        /// <summary>Sim time this raptor commits from stalking to the final rush. Staggered per raptor.</summary>
        public double CommitTime = double.NegativeInfinity;

        /// <summary>True once this raptor has stopped circling and charged.</summary>
        public bool Committed;

        /// <summary>Independent deterministic stream, forked from the match seed at spawn.</summary>
        public DeterministicRandom Rng;

        public Raptor(int id, Vector3 spawn, float health, DeterministicRandom rng = default)
        {
            Id = id;
            Position = spawn;
            Health = health;
            MaxHealth = health;
            IsAlive = true;
            State = RaptorState.Idle;
            Rng = rng;
        }

        public string LogId => "raptor_" + Id;
    }
}
