using System;
using DinoHunt.Sim;
using UnityEngine;

namespace DinoHunt.Core
{
    /// <summary>
    /// Plain, serializable match parameters. This is the tuning surface — edit it in the
    /// Inspector, not in code. It carries no behavior; it is data handed to the Simulation.
    ///
    /// Values are v0.1 starting points from the GDD and are all provisional (±50%) until
    /// observed in a running build.
    /// </summary>
    [Serializable]
    public sealed class MatchConfig
    {
        /// <summary>
        /// Independent copy, so a batch can vary one field per match without mutating the caller's
        /// config. Uses MemberwiseClone deliberately: a hand-written field-by-field copy silently
        /// drops newly added fields, which produced a batch where every run was accidentally
        /// identical. Reference members that a run may mutate are deep-copied below; WeaponSpec is
        /// shared because it is treated as immutable data.
        /// </summary>
        public MatchConfig Clone()
        {
            var c = (MatchConfig)MemberwiseClone();
            c.personality = personality?.Clone();
            if (roster != null)
            {
                c.roster = new RosterEntry[roster.Length];
                for (int i = 0; i < roster.Length; i++) c.roster[i] = roster[i]?.Clone();
            }
            return c;
        }

        [Header("Determinism")]
        [Tooltip("Master seed. Same seed + same config must reproduce the match exactly.")]
        public ulong seed = 1;

        [Tooltip("Fixed simulation timestep in seconds (1/60 = 0.0166...). Never use Time.deltaTime in sim logic.")]
        public float fixedDeltaTime = 1f / 60f;

        [Header("Teams")]
        [Tooltip("Agents per team. 3 = a proper skirmish; a death no longer instantly ends the match.")]
        public int teamSize = 5;

        [Tooltip("Fallback personality, used only when perAgentPersonalities is off. Kept so a batch can hold every agent identical as an experimental control.")]
        public Personality personality = new Personality();

        [Tooltip("Give each agent its own weight vector from the roster. This is what makes agents behave like distinct characters; turn it off to run a uniform-population control.")]
        public bool perAgentPersonalities = true;

        [Tooltip("The cast. Each team draws from this by slot, so both teams field the same spread of temperaments — symmetry is non-negotiable for fair AI-vs-AI.")]
        public RosterEntry[] roster = Roster.Default();

        [Tooltip("Base agent movement speed in units/second (unencumbered). Bumped for the much larger map so traversal + repositioning stay watchable.")]
        public float agentSpeed = 11f;

        [Tooltip("Minimum distance kept between agents so bodies don't overlap. 0 disables separation.")]
        public float agentSeparation = 3f;

        [Header("Objective")]
        [Tooltip("Eggs in the match. Odd (5) to make tied splits unlikely. No respawn.")]
        public int eggCount = 5;

        [Tooltip("Movement speed multiplier while carrying an egg. 0.727 x agentSpeed 11 = 8.0 u/s. NOTE: that equals raptorSpeed, so a fleeing carrier can no longer be run down in a straight line.")]
        public float carrySpeedMultiplier = 0.727f;

        [Tooltip("Seconds standing in your delivery zone to bank an egg.")]
        public float deliverySeconds = 2f;

        [Tooltip("Distance to the nest center within which an agent can grab an egg.")]
        public float grabRadius = 10f;

        [Tooltip("Distance to a dropped egg within which an agent picks it up.")]
        public float pickupRadius = 3f;

        [Header("Timing")]
        [Tooltip("Match timer in seconds — a backstop that should almost never fire. Large so the map isn't distance-constrained; resolves by eggs delivered if it fires.")]
        public float matchTimerSeconds = 900f;

        [Header("Raptors")]
        [Tooltip("Number of raptors at the nest. No pack coordination yet (M5 scripted FSM only) — multiple raptors act as independent hunters, not a coordinated pack.")]
        public int raptorCount = 5;

        [Tooltip("Raptor health. 208 = 26 rifle hits (2x the ~13 needed to kill an agent) — an expensive team investment out of the finite 90-round pool.")]
        public float raptorHealth = 208f;

        [Tooltip("Raptor movement speed. Must sit between carrier speed and runner speed to be scary without making eggs impossible.")]
        public float raptorSpeed = 8f;

        [Tooltip("Claw damage per hit. GDD flags silent + 30 as likely too lethal; M7 kept it unchanged since the screech already serves as the audible tell.")]
        public float clawDamage = 30f;

        [Tooltip("Melee reach of the claw.")]
        public float clawRange = 3.5f;

        [Tooltip("Seconds between claw strikes.")]
        public float raptorAttackInterval = 1f;

        [Tooltip("An agent within this distance of the nest wakes the raptor.")]
        public float raptorAggroRadius = 26f;

        [Tooltip("Line-of-sight detection range.")]
        public float raptorSightRange = 42f;

        [Tooltip("Scent radius. Within this, a raptor senses an agent THROUGH cover — sight can be broken, a nose cannot. Deliberately far shorter than the leash so it makes the nest scarier without extending the raptors' reach.")]
        public float raptorScentRadius = 20f;

        [Tooltip("Scent radius multiplier against a wounded agent (below half health). Predators track blood — being hurt near the nest marks you as prey.")]
        public float raptorWoundedScentMultiplier = 2f;

        [Tooltip("Max distance from the nest before the raptor gives up and returns.")]
        public float raptorLeashDistance = 60f;

        [Tooltip("Losing sight of the target for this long makes the raptor give up.")]
        public float raptorGiveUpSeconds = 4f;

        [Tooltip("Gunfire within this distance of the raptor alerts it.")]
        public float gunfireAggroRadius = 40f;

        [Header("Raptor pack behaviour (GDD §8.5)")]
        [Tooltip("Raptors claim distinct approach arcs around shared prey and circle at this radius before committing. 0 disables packing (raptors charge straight in).")]
        public float packEncircleRadius = 22f;

        [Tooltip("Seconds a raptor stalks its arc before charging. Randomised per hunt within ±packCommitJitter so the pattern is learnable but the instance never is.")]
        public float packCommitDelay = 2.2f;

        [Tooltip("Random spread on the commit delay. This is the 'pattern is fixed, instance is random' rule that keeps raptors feeling intelligent forever (GDD principle 6).")]
        public float packCommitJitter = 1.6f;

        [Tooltip("How long (seconds) a raptor remembers who shot it, for the retaliation aggro pull.")]
        public float raptorRetaliationSeconds = 3f;

        [Tooltip("How strongly a recent attacker is prioritized as a target. GDD ranks this LOWEST (§8.3 #4) — kept modest so it tiebreaks rather than overrides carrier/isolation. Only re-evaluated when the raptor picks a new target, so a committed hunt isn't interrupted.")]
        public float raptorRetaliationWeight = 15f;

        [Header("Radio — the team coordination mechanism (GDD §11.5)")]
        [Tooltip("Teammates act on each other's callouts. Off = agents know only what they personally perceive, which is the experimental control for research question Q2.")]
        public bool radioEnabled = true;

        [Tooltip("How long a callout stays actionable. This is the staleness window: longer means agents chase older, more often wrong information.")]
        public float radioMemorySeconds = 12f;

        // Vocabulary size is the independent variable for research question Q2 (constrained
        // communication under partial observability). Disabling a channel removes that concept
        // from the team's language entirely.
        [Tooltip("Vocabulary: can agents report a plain enemy sighting?")]
        public bool calloutEnemy = true;

        [Tooltip("Vocabulary: can agents report an enemy carrying an egg?")]
        public bool calloutCarrier = true;

        [Tooltip("Vocabulary: can agents report a dropped egg's location?")]
        public bool calloutEgg = true;

        [Tooltip("Vocabulary: can agents report a raptor sighting?")]
        public bool calloutRaptor = true;

        [Tooltip("Vocabulary: can agents call out that they are reloading and briefly out of the fight?")]
        public bool calloutReloading = true;

        [Tooltip("Vocabulary: can agents confirm a kill — enemy down, or raptor down?")]
        public bool calloutKill = true;

        [Tooltip("Radio discipline. A world-state report (enemy/raptor/egg spotted) is suppressed for the whole team for this long after someone makes it — the first person to see it calls it, nobody repeats. Kill confirmations and reload calls are exempt, since each refers to a distinct event.")]
        public float calloutRepeatSuppressionSeconds = 8f;

        [Header("Perception (M7)")]
        [Tooltip("Max distance an agent can directly perceive enemies/raptors (sight — no hive mind, GDD §7.1).")]
        public float agentSightRange = 80f;

        [Tooltip("Full field-of-view cone width in degrees, centered on facing. Wide 'alert' cone, not human FPS-narrow.")]
        public float agentFovDegrees = 140f;

        [Tooltip("Seconds a raptor screech stays as a 'something's hunting' alert for agents who can't see the raptor directly (sound, not sight — GDD §8.4).")]
        public float raptorAlertSeconds = 6f;

        [Tooltip("Earshot of a raptor screech. Must stay well short of the whole map: an alert that every agent always hears is a permanent threat signal, which reads as no signal and suppresses nest-robbing entirely.")]
        public float raptorAlertRadius = 260f;

        [Header("Training data (Tier-2 snapshots)")]
        [Tooltip("Write per-agent numeric (observation, action) rows to a CSV alongside the event log. GDD Tier-2 — off by default; enable when collecting training data.")]
        public bool logStateSnapshots = false;

        [Tooltip("Ticks between snapshot rows. 6 ticks @ 60Hz = 10 Hz.")]
        public int snapshotIntervalTicks = 6;

        [Header("Combat")]
        [Tooltip("Agent starting health. Delivery is the only heal (added later); damage is permanent.")]
        public float agentHealth = 100f;

        [Tooltip("Primary weapon: finite 90-round pool (30 mag + 60 reserve), long TTK.")]
        public WeaponSpec rifle = new WeaponSpec();

        [Tooltip("Sidearm — the floor when the rifle is empty. 100-round magazine, 1s reload, never runs out of reserve. At 20 damage it matches the rifle's sustained DPS exactly (20/0.45 = 8/0.18 = 44.4), and only gives up range.")]
        public WeaponSpec sidearm = new WeaponSpec
        {
            name = "sidearm",
            damage = 20f,
            range = 30f,
            fireInterval = 0.45f,
            magazineSize = 100,
            reloadTime = 1f,
            startingReserve = 0,
            infinite = true
        };
    }
}
