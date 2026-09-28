using System.Collections.Generic;
using DinoHunt.Core;
using UnityEngine;

namespace DinoHunt.Sim
{
    public enum Team { Blue, Red }

    /// <summary>
    /// A single agent's simulation state. Pure data + its own deterministic RNG stream.
    /// No visual references — the render layer reads Position/Intent and never writes them.
    ///
    /// Movement is path-based: the sim asks a pathfinder for corner points and walks the
    /// agent along them at a fixed speed each step. NavMeshAgent is deliberately not used,
    /// so movement stays deterministic (GDD requirement).
    /// </summary>
    public sealed class Agent
    {
        public readonly int Id;
        public readonly Team Team;

        /// <summary>Display name. Named agents make the intent overlay and radio readable as characters rather than indices.</summary>
        public string Name;

        public Vector3 Position;
        public float Speed;

        /// <summary>Horizontal facing/aim direction (XZ, normalized). Drives the gun model and, later, perception.</summary>
        public Vector3 Facing = Vector3.forward;

        public float Health;
        public float MaxHealth;
        public bool IsAlive;
        public WeaponState Weapon;

        /// <summary>This agent's personality (weight vector). Drives UtilityBrain scoring.</summary>
        public Personality Personality;

        /// <summary>The egg this agent is carrying, or null. Carrying it slows movement and is a liability.</summary>
        public Egg CarriedEgg;

        /// <summary>Seconds spent standing in the delivery zone with an egg (resets when leaving / not carrying).</summary>
        public float DeliveryTimer;

        /// <summary>Human-readable current decision. The whole "legible minds" feature hangs off this.</summary>
        public string Intent;

        // Perception-edge bookkeeping (M7): tracks what this agent already called out over radio,
        // so callouts fire once on the rising edge of perception rather than every tick something
        // stays in view. Not used for scoring — purely to de-duplicate radio_callout events.
        public bool CalledOutEnemy;
        public bool CalledOutCarrier;
        public bool CalledOutRaptor;
        public int CalledOutDroppedEggId = -1;

        /// <summary>Previous tick's reload state, so "reloading" is called on the rising edge only.</summary>
        public bool WasReloading;

        // ---- radio memory: what teammates have TOLD this agent ----
        //
        // This is the whole point of the radio being the coordination mechanism (GDD §11.5). These
        // are snapshots of where something WAS when a teammate saw it, not live positions. They go
        // stale, and agents act on them anyway — which is exactly the dramatic irony the design is
        // built around (§7.6): confident action on information that is quietly already wrong.
        public Vector3 HeardEnemyPos;
        public double HeardEnemyAt = double.NegativeInfinity;

        public Vector3 HeardEggPos;
        public double HeardEggAt = double.NegativeInfinity;

        public Vector3 Destination;

        /// <summary>Current path corners (from the pathfinder) and the index of the next corner to reach.</summary>
        public readonly List<Vector3> Path = new List<Vector3>();
        public int PathIndex;

        /// <summary>Independent deterministic stream, forked from the match seed at spawn.</summary>
        public DeterministicRandom Rng;

        public bool HasPath => PathIndex < Path.Count;

        public Agent(int id, Team team, Vector3 spawn, float speed, DeterministicRandom rng, float health, WeaponState weapon)
        {
            Id = id;
            Team = team;
            Position = spawn;
            Speed = speed;
            Rng = rng;
            Health = health;
            MaxHealth = health;
            IsAlive = true;
            Weapon = weapon;
            Intent = "spawning";
            Destination = spawn;
        }
    }
}
