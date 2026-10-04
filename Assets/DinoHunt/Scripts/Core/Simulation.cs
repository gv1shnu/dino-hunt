using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using DinoHunt.Logging;
using DinoHunt.Sim;
using UnityEngine;

namespace DinoHunt.Core
{
    /// <summary>
    /// Root of the deterministic simulation. Owns the clock, the master RNG, the agents, the
    /// combat resolution, and the event log, and drives the fixed-step update of every system.
    ///
    /// Hard rule: never touches cameras, renderers, or materials — it must run identically
    /// headless. Vector3 math, NavMesh path queries, and LOS queries are allowed (navigation,
    /// not rendering) and are injected as seams so the sim stays testable without Unity subsystems.
    /// </summary>
    public sealed class Simulation
    {
        private const float EyeHeight = 1.2f;

        public MatchConfig Config { get; }
        public SimClock Clock { get; }
        public EventLog Log { get; }

        /// <summary>Master RNG. Each agent forks its own stream from this at spawn.</summary>
        public DeterministicRandom Rng;

        public bool IsRunning { get; private set; }

        /// <summary>Raised when a shot is fired: (shooter position, target position). View-only hook for tracers; no sim state depends on it.</summary>
        public event Action<Vector3, Vector3> ShotFired;

        private readonly ArenaPoints _points;
        private readonly IPathfinder _pathfinder;
        private readonly ILineOfSight _los;
        private readonly IAgentBrain _brain;
        private WorldView _world;
        private readonly List<Agent> _agents = new List<Agent>();
        private readonly List<Egg> _eggs = new List<Egg>();
        private readonly List<Raptor> _raptors = new List<Raptor>();
        private Vector3 _lastGunfirePos;
        private double _lastGunfireTime = double.NegativeInfinity;

        /// <summary>Last time each team reported a given topic, for radio discipline. Key = team + topic.</summary>
        private readonly Dictionary<string, double> _lastTeamCallout = new Dictionary<string, double>();

        /// <summary>
        /// Shots fired this tick, applied only after every agent has acted.
        ///
        /// Agents are stepped in a fixed list order, so applying damage immediately let the
        /// lower-indexed agent kill its mirror before that mirror could return fire. In a symmetric
        /// game with a mirrored cast, that handed Blue an enormous structural advantage — measured at
        /// a 90% win skew. Buffering makes simultaneous trades genuinely simultaneous: both shots
        /// land, and two agents can kill each other on the same tick, which is the correct outcome.
        /// </summary>
        private readonly List<PendingDamage> _pendingDamage = new List<PendingDamage>();

        private struct PendingDamage
        {
            public Agent Target;
            public float Amount;
            public string AttackerId;
            public string Cause;
            public string Weapon;
            public Agent Attacker;
        }
        private readonly IEventSink _snapshotSink;
        private readonly StringBuilder _snapshotRow = new StringBuilder(160);
        private bool _snapshotHeaderWritten;
        private bool _ended;

        public IReadOnlyList<Agent> Agents => _agents;
        public IReadOnlyList<Egg> Eggs => _eggs;
        public IReadOnlyList<Raptor> Raptors => _raptors;

        public Simulation(MatchConfig config, ArenaPoints points = null, IPathfinder pathfinder = null,
                          ILineOfSight los = null, IEventSink sink = null, IEventSink snapshotSink = null,
                          IAgentBrain brain = null)
        {
            Config = config;
            Clock = new SimClock(config.fixedDeltaTime);
            Rng = new DeterministicRandom(config.seed);
            _points = points;
            _pathfinder = pathfinder;
            _los = los ?? new AlwaysVisibleLineOfSight();
            _brain = brain ?? new UtilityBrain();
            _snapshotSink = snapshotSink;
            Log = new EventLog(sink ?? new NullEventSink(), Clock, "m_" + config.seed, config.seed);

            if (_points != null)
            {
                SpawnAgents();
                SpawnEggs();
                SpawnRaptors();
            }

            _world = new WorldView(_agents, _raptors, _eggs, _points, _los, config.agentSightRange, config.agentFovDegrees)
            {
                RadioMemorySeconds = config.radioMemorySeconds
            };
        }

        public void Start()
        {
            IsRunning = true;
            Log.MatchStart(Config.teamSize, Config.eggCount);
            for (int i = 0; i < _agents.Count; i++)
                Log.Spawn(_agents[i]);
            for (int i = 0; i < _raptors.Count; i++)
                Log.RaptorSpawn(_raptors[i]);
        }

        /// <summary>Advance by exactly one fixed step. Called from FixedUpdate, or in a tight headless loop.</summary>
        public void Step()
        {
            if (!IsRunning) return;

            Clock.Step();
            _world.BeginTick(Clock.Time);

            // Alternate the stepping order every tick.
            //
            // The roster interleaves teams (blue even, red odd), so always iterating forward meant
            // blue acted first on every single tick — and since an egg goes to whoever calls dibs
            // first, blue won every tied race to the nest. Matches are decided by egg grabs far more
            // than by firefights, so that sub-tick edge compounded into a measured 92% win skew in a
            // supposedly symmetric game. Flipping the order by tick parity removes the systematic
            // advantage while staying perfectly deterministic.
            bool forward = (Clock.Tick & 1) == 0;
            for (int i = 0; i < _agents.Count; i++)
                StepAgent(_agents[forward ? i : _agents.Count - 1 - i]);

            FlushPendingDamage(); // simultaneous fire resolves simultaneously — see _pendingDamage

            ResolveSeparation();

            for (int i = 0; i < _raptors.Count; i++)
                StepRaptor(_raptors[i]);

            if (_snapshotSink != null && Config.logStateSnapshots && Clock.Tick % Config.snapshotIntervalTicks == 0)
                WriteSnapshots();

            if (!_ended && Clock.Time >= Config.matchTimerSeconds)
                EndMatch("timeout", WinnerByEggs());
        }

        // ---- spawning ----

        private void SpawnAgents()
        {
            int id = 0;
            for (int i = 0; i < Config.teamSize; i++)
            {
                _agents.Add(MakeAgent(id++, Team.Blue, _points.BlueBase, i));
                _agents.Add(MakeAgent(id++, Team.Red, _points.RedBase, i));
            }
        }

        private Agent MakeAgent(int id, Team team, Vector3 baseCenter, int indexInTeam)
        {
            float offset = (indexInTeam - (Config.teamSize - 1) * 0.5f) * _points.SpawnSpacing;
            Vector3 spawn = baseCenter + new Vector3(0f, 0f, offset);
            var weapon = new WeaponState(Config.rifle, Config.sidearm);
            var agent = new Agent(id, team, spawn, Config.agentSpeed, Rng.Fork(), Config.agentHealth, weapon);

            // Both teams draw the same roster slot for the same index, so the cast is mirrored and
            // the match stays symmetric (GDD §9.1) while individual agents differ from each other.
            if (Config.perAgentPersonalities)
            {
                RosterEntry entry = Roster.For(Config.roster, indexInTeam);
                agent.Personality = entry.personality;
                agent.Name = entry.name;
            }
            else
            {
                agent.Personality = Config.personality;
                agent.Name = TeamName(agent) + "_" + id;
            }

            agent.Facing = Flatten(_points.Nest - spawn); // start facing the nest they set out for
            return agent;
        }

        private void SpawnEggs()
        {
            // Arrange the eggs in a small deterministic ring inside the nest.
            float r = _points.NestRadius * 0.45f;
            for (int i = 0; i < Config.eggCount; i++)
            {
                float ang = (i / (float)Mathf.Max(1, Config.eggCount)) * Mathf.PI * 2f;
                Vector3 pos = _points.Nest + new Vector3(Mathf.Cos(ang) * r, 0f, Mathf.Sin(ang) * r);
                _eggs.Add(new Egg(i, pos));
            }
        }

        private void SpawnRaptors()
        {
            for (int i = 0; i < Config.raptorCount; i++)
            {
                float ang = Config.raptorCount <= 1 ? 0f : (i / (float)Config.raptorCount) * Mathf.PI * 2f;
                Vector3 spawn = _points.Nest + new Vector3(Mathf.Cos(ang) * _points.NestRadius, 0f, Mathf.Sin(ang) * _points.NestRadius);
                _raptors.Add(new Raptor(i, spawn, Config.raptorHealth, Rng.Fork()));
            }
        }

        // ---- per-agent update ----

        private void StepAgent(Agent agent)
        {
            if (!agent.IsAlive) return;

            agent.Weapon.Tick(Config.fixedDeltaTime);
            UpdatePerceptionCallouts(agent);

            Decision d = _brain.Decide(agent, _world);
            SetIntent(agent, d.Intent);

            // Engage and Intercept differ in why they were chosen, not in how they execute:
            // close to weapon range, then hold and fire.
            if ((d.Type == ActionType.Engage || d.Type == ActionType.Intercept) && d.TargetEnemy != null && d.TargetEnemy.IsAlive)
            {
                Agent t = d.TargetEnemy;
                float range = agent.Weapon.Active.range;
                bool inRange = (t.Position - agent.Position).sqrMagnitude <= range * range
                               && _los.CanSee(agent.Position + Vector3.up * EyeHeight, t.Position + Vector3.up * EyeHeight);

                if (inRange)
                {
                    agent.Facing = Flatten(t.Position - agent.Position);
                    agent.Path.Clear(); agent.PathIndex = 0; // hold and fire (carriers can still shoot)
                    TryShoot(agent, t);
                    UpdateEggInteractions(agent);
                    return;
                }

                MoveTo(agent, t.Position); // close the distance
                UpdateEggInteractions(agent);
                return;
            }

            if (d.Type == ActionType.HuntRaptor && d.TargetRaptor != null && d.TargetRaptor.IsAlive)
            {
                Raptor r = d.TargetRaptor;
                float range = agent.Weapon.Active.range;
                bool inRange = (r.Position - agent.Position).sqrMagnitude <= range * range
                               && _los.CanSee(agent.Position + Vector3.up * EyeHeight, r.Position + Vector3.up * EyeHeight);

                if (inRange)
                {
                    agent.Facing = Flatten(r.Position - agent.Position);
                    agent.Path.Clear(); agent.PathIndex = 0; // hold and fire
                    TryShootRaptor(agent, r);
                    UpdateEggInteractions(agent);
                    return;
                }

                MoveTo(agent, r.Position); // close the distance
                UpdateEggInteractions(agent);
                return;
            }

            // Movement-based actions (Grab / Deliver / Retreat / Flee / Regroup / Hold).
            MoveTo(agent, d.Destination);
            UpdateEggInteractions(agent);
        }

        private void MoveTo(Agent agent, Vector3 destination)
        {
            // Repath only when needed: no path yet, or the destination has moved enough.
            if (!agent.HasPath || (agent.Destination - destination).sqrMagnitude > 9f)
            {
                agent.Destination = destination;
                RecomputePath(agent);
            }

            Vector3 before = agent.Position;
            MoveAlongPath(agent);
            Vector3 moved = agent.Position - before;
            if (moved.sqrMagnitude > 1e-6f)
                agent.Facing = Flatten(moved);
        }

        // ---- perception / radio callouts (M7) ----

        /// <summary>
        /// Edge-triggered radio callouts: fires once when something newly enters this agent's
        /// perception, not every tick it stays in view. Purely observational — never affects the
        /// brain's decision, only the event log / narrator.
        /// </summary>
        private void UpdatePerceptionCallouts(Agent agent)
        {
            // Radio off = the team has no voice at all: nothing is said and nothing is heard. This is
            // the experimental control for research question Q2, so it must be a clean absence rather
            // than "they still talk but nobody listens".
            if (!Config.radioEnabled) return;

            Agent carrier = _world.NearestVisibleEnemyCarrier(agent, out _);
            bool seesCarrier = carrier != null && Config.calloutCarrier;
            if (seesCarrier && !agent.CalledOutCarrier && TeamMaySay(agent, "carrier"))
            {
                Say(agent, "enemy_carrier_spotted", carrier.Position);
                BroadcastEnemyReport(agent, carrier.Position);
            }
            agent.CalledOutCarrier = seesCarrier;

            // A plain enemy sighting is redundant chatter once the (higher-priority) carrier callout fired.
            Agent enemy = _world.NearestEnemy(agent, out _);
            bool seesEnemy = enemy != null && !seesCarrier && Config.calloutEnemy;
            if (seesEnemy && !agent.CalledOutEnemy && TeamMaySay(agent, "enemy"))
            {
                Say(agent, "enemy_spotted", enemy.Position);
                BroadcastEnemyReport(agent, enemy.Position);
            }
            agent.CalledOutEnemy = seesEnemy;

            Raptor raptor = Config.calloutRaptor ? _world.NearestRaptor(agent, out _) : null;
            bool seesRaptor = raptor != null;
            if (seesRaptor && !agent.CalledOutRaptor && TeamMaySay(agent, "raptor"))
                Say(agent, "raptor_spotted", raptor.Position);
            agent.CalledOutRaptor = seesRaptor;

            Egg dropped = Config.calloutEgg ? _world.NearestVisibleDroppedEgg(agent, out _) : null;
            int droppedId = dropped != null ? dropped.Id : -1;
            if (dropped != null && agent.CalledOutDroppedEggId != droppedId && TeamMaySay(agent, "egg"))
            {
                Say(agent, "egg_spotted", dropped.Position);
                BroadcastEggReport(agent, dropped.Position);
            }
            agent.CalledOutDroppedEggId = droppedId;

            // "Reloading" — announced on the rising edge, because it means "I am out of the fight
            // for two seconds, cover me". Exempt from team suppression: it is about the speaker, and
            // two people reloading at once is exactly when the team needs to know.
            bool reloadingNow = agent.Weapon.IsReloading;
            if (Config.calloutReloading && reloadingNow && !agent.WasReloading)
                Say(agent, "reloading", agent.Position);
            agent.WasReloading = reloadingNow;
        }

        /// <summary>Emit a callout to the log. One place, so every callout is recorded identically.</summary>
        private void Say(Agent speaker, string kind, Vector3 pos) => Log.RadioCallout(speaker, kind, pos);

        /// <summary>
        /// Radio discipline: the first agent to report a piece of world state calls it, and the rest
        /// of the team stays off the air for a while. Real radio traffic is terse because repeating a
        /// known fact wastes the channel — and because five agents each announcing the same raptor is
        /// noise, not information.
        ///
        /// Applies only to world-state reports. Kill confirmations and reload calls are exempt: each
        /// refers to a distinct event or a specific speaker, so they are never redundant.
        /// </summary>
        private bool TeamMaySay(Agent speaker, string topic)
        {
            string key = (speaker.Team == Team.Blue ? "b|" : "r|") + topic;
            if (_lastTeamCallout.TryGetValue(key, out double last)
                && Clock.Time - last < Config.calloutRepeatSuppressionSeconds)
                return false;

            _lastTeamCallout[key] = Clock.Time;
            return true;
        }

        /// <summary>
        /// Push a sighting to the speaker's living teammates. This is the coordination mechanism
        /// (GDD §11.5): what a teammate stores is a SNAPSHOT of where something was when it was
        /// seen, never a live feed. It ages, it goes wrong, and agents commit to it anyway.
        /// </summary>
        private void BroadcastEnemyReport(Agent speaker, Vector3 pos)
        {
            if (!Config.radioEnabled) return;
            for (int i = 0; i < _agents.Count; i++)
            {
                Agent listener = _agents[i];
                if (listener == speaker || !listener.IsAlive || listener.Team != speaker.Team) continue;
                listener.HeardEnemyPos = pos;
                listener.HeardEnemyAt = Clock.Time;
            }
        }

        private void BroadcastEggReport(Agent speaker, Vector3 pos)
        {
            if (!Config.radioEnabled) return;
            for (int i = 0; i < _agents.Count; i++)
            {
                Agent listener = _agents[i];
                if (listener == speaker || !listener.IsAlive || listener.Team != speaker.Team) continue;
                listener.HeardEggPos = pos;
                listener.HeardEggAt = Clock.Time;
            }
        }

        // ---- combat ----


        private void TryShoot(Agent shooter, Agent target)
        {
            WeaponSpec spec = shooter.Weapon.Fire();
            if (spec == null) return; // cooling down or forced to reload this tick

            Log.ShotFired(shooter, spec.name, target);
            ShotFired?.Invoke(shooter.Position, target.Position); // view-only tracer hook
            _lastGunfirePos = shooter.Position;                   // raptors aggro on nearby gunfire
            _lastGunfireTime = Clock.Time;
            // Queued, not applied: see _pendingDamage. Perfect aim — range and LOS already confirmed.
            _pendingDamage.Add(new PendingDamage
            {
                Target = target, Amount = spec.damage, AttackerId = AgentId(shooter),
                Cause = "enemy", Weapon = spec.name, Attacker = shooter
            });
        }

        /// <summary>Apply every shot fired this tick, after all agents have had their turn.</summary>
        private void FlushPendingDamage()
        {
            for (int i = 0; i < _pendingDamage.Count; i++)
            {
                PendingDamage d = _pendingDamage[i];
                ApplyDamage(d.Target, d.Amount, d.AttackerId, d.Cause, d.Weapon, d.Attacker);
            }
            _pendingDamage.Clear();
        }

        private void TryShootRaptor(Agent shooter, Raptor target)
        {
            WeaponSpec spec = shooter.Weapon.Fire();
            if (spec == null) return; // cooling down or forced to reload this tick

            Log.ShotFired(shooter, spec.name, target.LogId);
            ShotFired?.Invoke(shooter.Position, target.Position); // view-only tracer hook
            _lastGunfirePos = shooter.Position;                   // shooting a raptor is still gunfire — may wake others
            _lastGunfireTime = Clock.Time;
            ApplyRaptorDamage(target, spec.damage, shooter);
        }

        private void ApplyRaptorDamage(Raptor target, float amount, Agent attacker)
        {
            if (!target.IsAlive) return;

            target.Health -= amount;
            target.LastAttacker = attacker;            // low-priority "whoever last hurt it" aggro (GDD §8.3 #4)
            target.LastAttackerTime = Clock.Time;
            Log.RaptorDamage(target, amount, AgentId(attacker), Mathf.Max(0f, target.Health));

            if (target.Health <= 0f)
            {
                target.IsAlive = false;
                target.Target = null;
                Log.RaptorDeath(target, AgentId(attacker));

                if (attacker.IsAlive && Config.radioEnabled && Config.calloutKill)
                    Say(attacker, "raptor_down", target.Position);
                // Raptors are a finite, no-respawn resource (director decision): clearing them can
                // make an already-eliminated team's opponents win, so re-check the wipe now.
                CheckTeamWipe();
            }
        }

        private void ApplyDamage(Agent target, float amount, string attackerId, string cause, string weapon, Agent attacker = null)
        {
            if (!target.IsAlive) return;

            target.Health -= amount;
            Log.Damage(target, amount, attackerId, Mathf.Max(0f, target.Health));

            if (target.Health <= 0f)
            {
                target.IsAlive = false;
                string carried = target.CarriedEgg != null ? target.CarriedEgg.LogId : null;

                // Enemy kill drops the egg where it falls; a raptor kill drags it back to the nest.
                if (cause == "raptor") ReturnEggToNest(target);
                else DropEggOnDeath(target);

                Log.Death(target, cause, attackerId, weapon, carried);
                SetIntent(target, "down");

                // Kill confirmation. Not suppressed — each death is a distinct fact worth the airtime.
                if (attacker != null && attacker.IsAlive && Config.radioEnabled && Config.calloutKill)
                    Say(attacker, "enemy_down", target.Position);

                CheckTeamWipe();
            }
        }

        private void ReturnEggToNest(Agent victim)
        {
            Egg egg = victim.CarriedEgg;
            if (egg == null) return;

            egg.State = EggState.InNest;
            egg.Position = _points.Nest;
            egg.Carrier = null;
            egg.Stolen = false;
            egg.DroppedByTeam = null;
            victim.CarriedEgg = null;
        }

        // ---- raptors ----

        private void StepRaptor(Raptor r)
        {
            if (!r.IsAlive) return;
            float dt = Config.fixedDeltaTime;
            if (r.AttackCooldown > 0f) r.AttackCooldown -= dt;

            // Drop a dead or out-of-sight target (give-up timer).
            if (r.Target != null)
            {
                if (!r.Target.IsAlive)
                {
                    r.Target = null;
                }
                else if (CanRaptorSense(r, r.Target))
                {
                    r.LostSightTimer = 0f;
                }
                else
                {
                    r.LostSightTimer += dt;
                    if (r.LostSightTimer >= Config.raptorGiveUpSeconds) r.Target = null;
                }
            }

            // Leash: too far from the nest -> abandon the hunt and head back.
            if (Vector3.Distance(r.Position, _points.Nest) > Config.raptorLeashDistance)
                r.Target = null;

            if (r.Target == null)
            {
                Agent prey = AcquireRaptorTarget(r);
                if (prey != null)
                {
                    r.Target = prey;
                    r.LostSightTimer = 0f;
                    r.State = RaptorState.Hunting;
                    ClaimApproachArc(r, prey);
                    Log.RaptorAggro(r, prey);
                    Log.RaptorScreech(r); // the theft can't be quiet — announces across the sector
                    _world.RaiseRaptorAlert(r.Position, Clock.Time, Config.raptorAlertSeconds, Config.raptorAlertRadius); // sound: bounded in time and earshot, not blocked by facing/cover
                }
            }

            if (r.Target != null)
            {
                r.State = RaptorState.Hunting;
                Vector3 to = r.Target.Position - r.Position; to.y = 0f;
                float d = to.magnitude;
                if (d > 1e-4f) r.Facing = to / d;

                if (d <= Config.clawRange)
                {
                    if (r.AttackCooldown <= 0f)
                    {
                        r.AttackCooldown = Config.raptorAttackInterval;
                        RaptorAttack(r, r.Target);
                    }
                }
                else
                {
                    MoveRaptor(r, PackApproachPoint(r));
                }
                return;
            }

            // No prey: return to the nest, then idle.
            float distNest = Vector3.Distance(r.Position, _points.Nest);
            if (distNest > _points.NestRadius)
            {
                r.State = RaptorState.Returning;
                MoveRaptor(r, _points.Nest);
            }
            else
            {
                r.State = RaptorState.Idle;
            }
        }

        // ---- pack behaviour (GDD §8.5) ----
        //
        // The design target is "unpredictable yet coordinated". Coordination comes from raptors
        // claiming DISTINCT approach arcs around shared prey, so they enclose instead of queueing up
        // behind one another. Unpredictability comes from re-rolling which arc and when each commits,
        // every hunt. The pattern is learnable — raptors encircle — while the instance never is.

        /// <summary>Claim an approach arc distinct from other raptors already hunting this prey.</summary>
        private void ClaimApproachArc(Raptor claimant, Agent prey)
        {
            int sharing = 0;
            for (int i = 0; i < _raptors.Count; i++)
            {
                Raptor o = _raptors[i];
                if (o == claimant || !o.IsAlive) continue;
                if (o.Target == prey) sharing++;
            }

            float spread = Mathf.PI * 2f / Mathf.Max(1, Config.raptorCount);
            float jitter = claimant.Rng.NextFloat(-spread * 0.25f, spread * 0.25f);
            claimant.ApproachAngle = sharing * spread + jitter;

            claimant.Committed = false;
            claimant.CommitTime = Clock.Time + Config.packCommitDelay
                                + claimant.Rng.NextFloat(-Config.packCommitJitter, Config.packCommitJitter);
        }

        /// <summary>Where this raptor should move: its claimed arc while stalking, the prey itself once committed.</summary>
        private Vector3 PackApproachPoint(Raptor r)
        {
            if (Config.packEncircleRadius <= 0f) return r.Target.Position; // packing disabled: charge straight in
            if (!r.Committed && Clock.Time >= r.CommitTime) r.Committed = true;
            if (r.Committed) return r.Target.Position;

            // Long patient nothing, then sudden total commitment — hold station on the arc for now.
            return r.Target.Position + new Vector3(Mathf.Cos(r.ApproachAngle), 0f, Mathf.Sin(r.ApproachAngle)) * Config.packEncircleRadius;
        }

        /// <summary>
        /// Whether a raptor is aware of an agent — sight, or scent.
        ///
        /// Raptors out-sense agents qualitatively rather than at longer range, which is the balance
        /// answer: extending their sight would be wasted, since the leash caps how far they can act
        /// on it anyway. Instead, scent ignores cover entirely. You can break a raptor's line of
        /// sight; you cannot hide from its nose. Wounded agents are smelled from twice as far.
        ///
        /// The scent radius is deliberately far inside the leash, so this makes the nest more
        /// frightening without giving raptors any additional reach. The counterplay is unchanged and
        /// still the right one: stay out of their territory, or leave it quickly.
        /// </summary>
        private bool CanRaptorSense(Raptor r, Agent a)
        {
            float sq = (a.Position - r.Position).sqrMagnitude;

            float scent = Config.raptorScentRadius;
            if (a.Health < a.MaxHealth * 0.5f) scent *= Config.raptorWoundedScentMultiplier;
            if (sq <= scent * scent) return true;

            if (sq > Config.raptorSightRange * Config.raptorSightRange) return false;
            return _los.CanSee(r.Position + Vector3.up * EyeHeight, a.Position + Vector3.up * EyeHeight);
        }

        private Agent AcquireRaptorTarget(Raptor r)
        {
            bool gunfireAlert = (Clock.Time - _lastGunfireTime) < 2.0
                                && (r.Position - _lastGunfirePos).sqrMagnitude <= Config.gunfireAggroRadius * Config.gunfireAggroRadius;

            Agent best = null;
            float bestScore = float.NegativeInfinity;

            for (int i = 0; i < _agents.Count; i++)
            {
                Agent a = _agents[i];
                if (!a.IsAlive) continue;

                bool nearNest = (a.Position - _points.Nest).sqrMagnitude <= Config.raptorAggroRadius * Config.raptorAggroRadius;
                bool inSight = CanRaptorSense(r, a);
                if (!nearNest && !inSight && !gunfireAlert) continue;
                if (gunfireAlert && !inSight && !nearNest) continue; // noise draws it, but it still needs the prey reachable-ish

                // Priority (GDD §8.3): carriers first, then the most isolated agent, nearest as
                // tiebreak, and a modest pull toward whoever recently shot it (#4, deliberately weak).
                float isolation = NearestDistance(a, enemy: false); // distance to nearest teammate; -1 if alone
                if (isolation < 0f) isolation = Config.raptorSightRange; // truly alone = maximally isolated
                float distToRaptor = (a.Position - r.Position).magnitude;

                bool recentAttacker = r.LastAttacker == a && (Clock.Time - r.LastAttackerTime) < Config.raptorRetaliationSeconds;
                float retaliation = recentAttacker ? Config.raptorRetaliationWeight : 0f;

                float score = (a.CarriedEgg != null ? 10000f : 0f) + isolation * 10f - distToRaptor + retaliation;
                if (score > bestScore) { bestScore = score; best = a; }
            }
            return best;
        }

        private void MoveRaptor(Raptor r, Vector3 dest)
        {
            Vector3 flat = dest - r.Position; flat.y = 0f;
            if (flat.sqrMagnitude < 1e-8f) return;

            // Route around cover like agents do. Repath only when the goal has moved enough
            // (hunting goals track moving prey) or the current path is used up.
            if (!r.HasPath || (r.Destination - dest).sqrMagnitude > 9f)
            {
                r.Destination = dest;
                r.Path.Clear();
                r.PathIndex = 0;
                if (_pathfinder == null || !_pathfinder.TryFindPath(r.Position, dest, r.Path))
                    r.Path.Add(dest); // fallback: straight line
            }

            float budget = Config.raptorSpeed * Config.fixedDeltaTime;
            Vector3 next = r.Position;
            int guard = r.Path.Count + 1;
            while (budget > 0f && r.HasPath && guard-- > 0)
            {
                Vector3 target = r.Path[r.PathIndex];
                Vector3 to = target - next; to.y = 0f;
                float dist = to.magnitude;
                if (dist <= budget)
                {
                    next = new Vector3(target.x, next.y, target.z);
                    budget -= dist;
                    r.PathIndex++;
                }
                else
                {
                    next += (to / dist) * budget;
                    budget = 0f;
                }
            }

            // Clamp to the leash sphere around the nest.
            Vector3 fromNest = next - _points.Nest; fromNest.y = 0f;
            if (fromNest.magnitude > Config.raptorLeashDistance)
                next = _points.Nest + fromNest.normalized * Config.raptorLeashDistance;

            r.Position = next;
        }

        private void RaptorAttack(Raptor r, Agent target)
        {
            r.Facing = Flatten(target.Position - r.Position);
            ApplyDamage(target, Config.clawDamage, r.LogId, "raptor", "claw");
        }

        // ---- eggs ----

        private void UpdateEggInteractions(Agent agent)
        {
            if (agent.CarriedEgg != null)
            {
                Vector3 ownBase = agent.Team == Team.Blue ? _points.BlueBase : _points.RedBase;
                if ((agent.Position - ownBase).sqrMagnitude <= _points.DeliveryRadius * _points.DeliveryRadius)
                {
                    agent.DeliveryTimer += Config.fixedDeltaTime;
                    if (agent.DeliveryTimer >= Config.deliverySeconds)
                        DeliverEgg(agent);
                }
                else
                {
                    agent.DeliveryTimer = 0f;
                }
                return;
            }

            agent.DeliveryTimer = 0f;

            // Grab from the nest.
            if ((agent.Position - _points.Nest).sqrMagnitude <= Config.grabRadius * Config.grabRadius)
            {
                Egg fromNest = NearestEgg(EggState.InNest, agent.Position);
                if (fromNest != null) { GrabEgg(agent, fromNest); return; }
            }

            // Opportunistically pick up a dropped egg you pass close to.
            Egg dropped = NearestEgg(EggState.Dropped, agent.Position, Config.pickupRadius);
            if (dropped != null) GrabEgg(agent, dropped);
        }

        private void GrabEgg(Agent agent, Egg egg)
        {
            bool wasDropped = egg.State == EggState.Dropped;
            bool isSteal = wasDropped && egg.DroppedByTeam.HasValue && egg.DroppedByTeam.Value != agent.Team;

            egg.State = EggState.Carried;
            egg.Carrier = agent;
            agent.CarriedEgg = egg;
            agent.DeliveryTimer = 0f;

            if (isSteal)
            {
                egg.Stolen = true;
                Log.EggStolen(agent, egg, egg.DroppedByTeam.Value);
            }
            else
            {
                Log.EggPickup(agent, egg);
            }
            egg.DroppedByTeam = null;
        }

        private void DeliverEgg(Agent agent)
        {
            Egg egg = agent.CarriedEgg;
            egg.State = EggState.Delivered;
            egg.DeliveredBy = agent.Team;
            egg.Carrier = null;
            agent.CarriedEgg = null;
            agent.DeliveryTimer = 0f;

            agent.Health = Config.agentHealth; // delivery is the only heal in the game

            Log.EggDelivered(agent, egg, egg.Stolen);
            CheckAllEggsDelivered();
        }

        private void DropEggOnDeath(Agent victim)
        {
            Egg egg = victim.CarriedEgg;
            if (egg == null) return;

            egg.State = EggState.Dropped;
            egg.Position = victim.Position;
            egg.Carrier = null;
            egg.DroppedByTeam = victim.Team;
            victim.CarriedEgg = null;
            Log.EggDrop(victim, egg, victim.Position);
        }

        private Egg NearestEgg(EggState state, Vector3 from, float maxDist = float.PositiveInfinity)
        {
            float bestSq = maxDist * maxDist;
            Egg best = null;
            for (int i = 0; i < _eggs.Count; i++)
            {
                Egg e = _eggs[i];
                if (e.State != state) continue;
                float sq = (e.Position - from).sqrMagnitude;
                if (sq <= bestSq) { bestSq = sq; best = e; }
            }
            return best;
        }

        private void CheckAllEggsDelivered()
        {
            for (int i = 0; i < _eggs.Count; i++)
                if (_eggs[i].State != EggState.Delivered) return;

            EndMatch("all_eggs_delivered", WinnerByEggs());
        }

        private int DeliveredCount(Team team)
        {
            int n = 0;
            for (int i = 0; i < _eggs.Count; i++)
                if (_eggs[i].State == EggState.Delivered && _eggs[i].DeliveredBy == team) n++;
            return n;
        }

        private string WinnerByEggs()
        {
            int blue = DeliveredCount(Team.Blue);
            int red = DeliveredCount(Team.Red);
            if (blue == red) return null;
            return blue > red ? "blue" : "red";
        }

        private void CheckTeamWipe()
        {
            if (_ended) return;
            int blue = AliveCount(Team.Blue);
            int red = AliveCount(Team.Red);

            // Both teams gone. If raptors still hold the nest, the raptors outlasted everyone and
            // won it outright — this is the third body actually winning, not a technicality
            // (GDD §1.4). Only if nothing at all is left standing is it a genuine draw.
            if (blue == 0 && red == 0)
            {
                EndMatch("mutual_elimination", AnyRaptorAlive() ? "raptors" : null);
                return;
            }

            if (blue == 0 || red == 0)
            {
                // A team is eliminated, but while a raptor still lives the survivors haven't "won":
                // the nest is still deadly. The match continues (resolves by eggs, the timer, or a
                // later mutual elimination). Elimination only wins once the raptors are cleared too.
                if (AnyRaptorAlive()) return;
                EndMatch("team_wipe", blue > 0 ? "blue" : "red");
            }
        }

        private bool AnyRaptorAlive()
        {
            for (int i = 0; i < _raptors.Count; i++)
                if (_raptors[i].IsAlive) return true;
            return false;
        }

        private void EndMatch(string reason, string winner)
        {
            if (_ended) return;
            _ended = true;
            IsRunning = false;
            Log.MatchEnd(reason, winner);
        }

        // ---- movement ----

        private void RecomputePath(Agent agent)
        {
            agent.Path.Clear();
            agent.PathIndex = 0;

            if (_pathfinder != null && _pathfinder.TryFindPath(agent.Position, agent.Destination, agent.Path))
                return;

            agent.Path.Add(agent.Destination); // fallback: straight line
        }

        /// <summary>
        /// Push overlapping agents apart so bodies don't stack. Deterministic: fixed pair order,
        /// pure float math; exact overlaps are nudged along an index-derived axis so the result is
        /// still reproducible.
        /// </summary>
        private void ResolveSeparation()
        {
            float minSep = Config.agentSeparation;
            if (minSep <= 0f) return;
            float minSq = minSep * minSep;

            for (int i = 0; i < _agents.Count; i++)
            {
                Agent a = _agents[i];
                if (!a.IsAlive) continue;
                for (int j = i + 1; j < _agents.Count; j++)
                {
                    Agent b = _agents[j];
                    if (!b.IsAlive) continue;

                    Vector3 d = b.Position - a.Position;
                    d.y = 0f;
                    float sq = d.sqrMagnitude;
                    if (sq >= minSq) continue;

                    if (sq < 1e-8f)
                        d = new Vector3(j - i, 0f, i - j); // deterministic nudge for exact overlap

                    float dist = d.magnitude;
                    Vector3 push = (d / dist) * ((minSep - dist) * 0.5f);
                    a.Position -= push;
                    b.Position += push;
                }
            }
        }

        private void MoveAlongPath(Agent agent)
        {
            float speed = agent.CarriedEgg != null ? agent.Speed * Config.carrySpeedMultiplier : agent.Speed;
            float budget = speed * Config.fixedDeltaTime;

            int guard = agent.Path.Count + 1;
            while (budget > 0f && agent.HasPath && guard-- > 0)
            {
                Vector3 target = agent.Path[agent.PathIndex];
                Vector3 to = target - agent.Position;
                float dist = to.magnitude;

                if (dist <= budget)
                {
                    agent.Position = target;
                    budget -= dist;
                    agent.PathIndex++;
                }
                else
                {
                    agent.Position += (to / dist) * budget;
                    budget = 0f;
                }
            }
        }

        // ---- helpers ----

        private void SetIntent(Agent agent, string intent)
        {
            if (agent.Intent == intent) return;
            agent.Intent = intent;
            Log.IntentChange(agent, intent);
        }

        private int AliveCount(Team team)
        {
            int n = 0;
            for (int i = 0; i < _agents.Count; i++)
                if (_agents[i].Team == team && _agents[i].IsAlive) n++;
            return n;
        }

        private static string TeamName(Agent a) => a.Team == Team.Blue ? "blue" : "red";
        private static string AgentId(Agent a) => TeamName(a) + "_" + a.Id;

        private static Vector3 Flatten(Vector3 v)
        {
            v.y = 0f;
            return v.sqrMagnitude > 1e-6f ? v.normalized : Vector3.forward;
        }

        // ---- Tier-2 training snapshots: dense per-agent (observation, action) rows ----

        private void WriteSnapshots()
        {
            if (!_snapshotHeaderWritten)
            {
                _snapshotSink.Write("t,match,agent,team,x,z,health,ammo,has_egg,carrying_stolen,dist_enemy,dist_mate,dist_nest,dist_own_base,dist_raptor,time_left,egg_diff,intent");
                _snapshotHeaderWritten = true;
            }

            double t = Clock.Time;
            float timeLeft = Config.matchTimerSeconds - (float)t;
            int blueDelivered = DeliveredCount(Team.Blue);
            int redDelivered = DeliveredCount(Team.Red);
            string matchId = "m_" + Config.seed;

            for (int i = 0; i < _agents.Count; i++)
            {
                Agent a = _agents[i];
                if (!a.IsAlive) continue;

                int eggDiff = a.Team == Team.Blue ? blueDelivered - redDelivered : redDelivered - blueDelivered;
                float distEnemy = NearestDistance(a, enemy: true);
                float distMate = NearestDistance(a, enemy: false);
                Vector3 ownBase = a.Team == Team.Blue ? _points.BlueBase : _points.RedBase;

                var inv = CultureInfo.InvariantCulture;
                _snapshotRow.Clear();
                _snapshotRow.Append(t.ToString("0.###", inv)).Append(',')
                    .Append(matchId).Append(',')
                    .Append(TeamName(a)).Append('_').Append(a.Id).Append(',')
                    .Append(TeamName(a)).Append(',')
                    .Append(a.Position.x.ToString("0.##", inv)).Append(',')
                    .Append(a.Position.z.ToString("0.##", inv)).Append(',')
                    .Append(a.Health.ToString("0.#", inv)).Append(',')
                    .Append(a.Weapon.RifleAmmoTotal.ToString(inv)).Append(',')
                    .Append(a.CarriedEgg != null ? '1' : '0').Append(',')
                    .Append(a.CarriedEgg != null && a.CarriedEgg.Stolen ? '1' : '0').Append(',')
                    .Append(distEnemy.ToString("0.#", inv)).Append(',')
                    .Append(distMate.ToString("0.#", inv)).Append(',')
                    .Append(Vector3.Distance(a.Position, _points.Nest).ToString("0.#", inv)).Append(',')
                    .Append(Vector3.Distance(a.Position, ownBase).ToString("0.#", inv)).Append(',')
                    .Append(NearestRaptorDistance(a).ToString("0.#", inv)).Append(',')
                    .Append(timeLeft.ToString("0.#", inv)).Append(',')
                    .Append(eggDiff.ToString(inv)).Append(',')
                    .Append(a.Intent);
                _snapshotSink.Write(_snapshotRow.ToString());
            }
        }

        /// <summary>Distance to the nearest living enemy (enemy=true) or teammate (enemy=false); -1 if none.</summary>
        private float NearestDistance(Agent from, bool enemy)
        {
            float bestSq = float.PositiveInfinity;
            for (int i = 0; i < _agents.Count; i++)
            {
                Agent o = _agents[i];
                if (o == from || !o.IsAlive) continue;
                bool isEnemy = o.Team != from.Team;
                if (isEnemy != enemy) continue;
                float sq = (o.Position - from.Position).sqrMagnitude;
                if (sq < bestSq) bestSq = sq;
            }
            return float.IsPositiveInfinity(bestSq) ? -1f : Mathf.Sqrt(bestSq);
        }

        private float NearestRaptorDistance(Agent from)
        {
            float bestSq = float.PositiveInfinity;
            for (int i = 0; i < _raptors.Count; i++)
            {
                if (!_raptors[i].IsAlive) continue;
                float sq = (_raptors[i].Position - from.Position).sqrMagnitude;
                if (sq < bestSq) bestSq = sq;
            }
            return float.IsPositiveInfinity(bestSq) ? -1f : Mathf.Sqrt(bestSq);
        }
    }
}
