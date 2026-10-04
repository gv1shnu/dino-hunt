using System;
using System.Collections.Generic;
using System.IO;
using DinoHunt.Arena;
using DinoHunt.Core;
using DinoHunt.Logging;
using DinoHunt.Sim;
using Unity.AI.Navigation;
using UnityEngine;

namespace DinoHunt.View
{
    /// <summary>
    /// Entry point for a played (rendered) match. Configures the fixed timestep, builds the
    /// greybox, bakes the NavMesh, spawns the agent views (art models, or capsules as fallback), and drives the Simulation from
    /// FixedUpdate. Each rendered frame it copies sim state onto the view transforms.
    ///
    /// Deliberately thin: it hosts and visualizes the Simulation but contains no game logic,
    /// so the exact same Simulation can be driven headless in batch mode without this class.
    /// </summary>
    public sealed class Bootstrap : MonoBehaviour
    {
        [SerializeField] private MatchConfig config = new MatchConfig();
        [SerializeField] private ArenaLayout layout = new ArenaLayout();

        [Tooltip("Uniform scale of the agent capsules. Bumped up so agents read from the top-down camera; purely cosmetic.")]
        [SerializeField] private float agentVisualScale = 7.5f;

        [Tooltip("Uniform scale of the egg spheres. Cosmetic; smaller than agents.")]
        [SerializeField] private float eggVisualScale = 1.8f;

        [Tooltip("Uniform scale of the raptor capsules. Larger than agents (GDD).")]
        [SerializeField] private float raptorVisualScale = 10.8f;

        [Tooltip("Tracer bullet travel speed (units/second). Cosmetic only.")]
        [SerializeField] private float bulletSpeed = 160f;

        [Tooltip("Tracer bullet thickness in units.")]
        [SerializeField] private float bulletThickness = 0.35f;

        [Header("Art models (empty prefab = greybox capsule)")]
        [SerializeField] private UnitModelSlot soldierModel = new UnitModelSlot();
        [SerializeField] private UnitModelSlot raptorModel = new UnitModelSlot { moveClipSpeed = 10f };
        [SerializeField] private Color blueTeamTint = new Color(0.25f, 0.5f, 1f);
        [SerializeField] private Color redTeamTint = new Color(1f, 0.3f, 0.28f);

        [Tooltip("Seconds between commentary situation reports. Keeps the audience oriented during quiet stretches; 0 disables.")]
        [SerializeField] private float commentaryReportInterval = 20f;

        private float _nextReportTime;

        public Simulation Simulation { get; private set; }

        private readonly List<Transform> _agentViews = new List<Transform>();
        private readonly List<Transform> _agentGuns = new List<Transform>();
        private readonly List<Transform> _eggViews = new List<Transform>();
        private readonly List<Transform> _raptorViews = new List<Transform>();
        private readonly List<UnitModel> _agentModels = new List<UnitModel>();
        private readonly List<UnitModel> _raptorModels = new List<UnitModel>();
        private readonly List<float> _lastAgentCooldown = new List<float>();
        private readonly List<float> _lastRaptorCooldown = new List<float>();
        private static readonly Quaternion ReloadTilt = Quaternion.Euler(-78f, 0f, 0f); // gun raised while reloading
        private JsonlFileSink _logSink;
        private JsonlFileSink _snapshotSink;
        private MatchNarrator _narrator;

        private void Awake()
        {
            // Fixed timestep for all simulation. FixedUpdate now fires at exactly config.fixedDeltaTime.
            Time.fixedDeltaTime = config.fixedDeltaTime;
            Application.runInBackground = true;

            var arena = new ArenaBuilder(layout);
            arena.Build(config.seed);
            BakeNavMesh(arena.Root);

            // Use the RESOLVED layout — procedural variation may have changed the arena for this seed,
            // and the waypoints must match the geometry that was actually built.
            var points = ArenaGeometry.MakePoints(arena.ResolvedLayout);

            // Disk logging is a desktop/editor concern. On a WebGL build the Emscripten
            // filesystem is in-memory and never synced back to the browser, so a per-event
            // log would only burn memory writing a file the player can never retrieve. Skip
            // it there; the live narrator below still receives every event for commentary.
#if UNITY_WEBGL && !UNITY_EDITOR
            _logSink = null;
            _snapshotSink = null;
#else
            _logSink = CreateLogSink(config.seed, "jsonl");
            _snapshotSink = config.logStateSnapshots ? CreateLogSink(config.seed, "snapshots.csv") : null;
#endif
            _narrator = new MatchNarrator();
            var eventSink = new MultiSink(_logSink, _narrator); // file log + live radio/commentary
            Simulation = new Simulation(config, points, new NavMeshPathfinder(), new RaycastLineOfSight(), eventSink, _snapshotSink);
            Simulation.ShotFired += OnShotFired;
            if (soldierModel.HasModel || raptorModel.HasModel) EnsureModelLighting();
            SpawnAgentViews();
            SpawnEggViews();
            SpawnRaptorViews();

            var camera = GetOrCreateCamera();
            camera.Frame(arena.NestCenter, arena.HalfX, arena.HalfZ);
            camera.gameObject.AddComponent<FreeCameraController>();

            var overlay = camera.gameObject.AddComponent<IntentOverlay>();
            overlay.Init(camera.GetComponent<Camera>(), Simulation.Agents, Simulation.Raptors);

            var chatter = camera.gameObject.AddComponent<ChatterOverlay>();
            chatter.Init(_narrator);

            var scoreboard = camera.gameObject.AddComponent<ScoreboardOverlay>();
            scoreboard.Init(_narrator);

            Simulation.Start();

            Debug.Log($"[DinoHunt] Match started. seed={config.seed} dt={config.fixedDeltaTime:F5} " +
                      $"teamSize={config.teamSize} agents={Simulation.Agents.Count} " +
                      $"eggs={config.eggCount} timer={config.matchTimerSeconds}s");
            if (_logSink != null)
                Debug.Log($"[DinoHunt] Event log: {_logSink.Path}");
            if (_snapshotSink != null)
                Debug.Log($"[DinoHunt] Training snapshots: {_snapshotSink.Path}");
        }

        private void FixedUpdate()
        {
            Simulation.Step();
        }

        private void Update()
        {
            var agents = Simulation.Agents;
            for (int i = 0; i < agents.Count; i++)
            {
                var view = _agentViews[i];
                if (view == null) continue; // body already removed

                var agent = agents[i];
                if (!agent.IsAlive)
                {
                    // Remove the body from the game on death.
                    Destroy(view.gameObject);
                    _agentViews[i] = null;
                    continue;
                }

                view.position = agent.Position;
                if (agent.Facing.sqrMagnitude > 1e-6f)
                    view.rotation = Quaternion.LookRotation(agent.Facing);

                // Raise the gun while reloading (mag of 30 -> a reload every 30 rounds).
                var gun = _agentGuns[i];
                if (gun != null)
                {
                    Quaternion target = agent.Weapon.IsReloading ? ReloadTilt : Quaternion.identity;
                    gun.localRotation = Quaternion.Slerp(gun.localRotation, target, Time.deltaTime * 10f);
                }

                // A jump in weapon cooldown means a shot was fired this frame.
                float cd = agent.Weapon.Cooldown;
                if (cd > _lastAgentCooldown[i] + 1e-4f) _agentModels[i]?.TriggerAction();
                _lastAgentCooldown[i] = cd;
            }

            SyncEggViews();
            SyncRaptorViews();
            UpdateCommentaryReport();
        }

        /// <summary>
        /// Feed the commentator the live state of the whole match at intervals. Event-driven chatter
        /// alone leaves long silences while agents cross the map; this keeps the observer oriented.
        /// Read-only — the simulation never learns this happened.
        /// </summary>
        private void UpdateCommentaryReport()
        {
            if (_narrator == null || commentaryReportInterval <= 0f) return;
            if (!Simulation.IsRunning || Simulation.Clock.Time < _nextReportTime) return;
            _nextReportTime = (float)Simulation.Clock.Time + commentaryReportInterval;

            int blueAlive = 0, redAlive = 0, raptorsAlive = 0;
            string blueCarrier = null, redCarrier = null;

            foreach (var a in Simulation.Agents)
            {
                if (!a.IsAlive) continue;
                if (a.Team == Team.Blue) blueAlive++; else redAlive++;
                if (a.CarriedEgg == null) continue;
                string who = string.IsNullOrEmpty(a.Name) ? "blue_" + a.Id : a.Name;
                if (a.Team == Team.Blue) blueCarrier = who; else redCarrier = who;
            }

            foreach (var r in Simulation.Raptors)
                if (r.IsAlive) raptorsAlive++;

            _narrator.StatusReport(blueAlive, redAlive, raptorsAlive, blueCarrier, redCarrier,
                                   config.matchTimerSeconds - (float)Simulation.Clock.Time);
        }

        private void SyncRaptorViews()
        {
            var raptors = Simulation.Raptors;
            for (int i = 0; i < raptors.Count; i++)
            {
                var view = _raptorViews[i];
                if (view == null) continue;

                var raptor = raptors[i];
                if (!raptor.IsAlive)
                {
                    Destroy(view.gameObject);
                    _raptorViews[i] = null;
                    continue;
                }

                view.position = raptor.Position;
                if (raptor.Facing.sqrMagnitude > 1e-6f)
                    view.rotation = Quaternion.LookRotation(raptor.Facing);

                // A jump in attack cooldown means the raptor just struck.
                float cd = raptor.AttackCooldown;
                if (cd > _lastRaptorCooldown[i] + 1e-4f) _raptorModels[i]?.TriggerAction();
                _lastRaptorCooldown[i] = cd;
            }
        }

        private void SyncEggViews()
        {
            var eggs = Simulation.Eggs;
            for (int i = 0; i < eggs.Count; i++)
            {
                var view = _eggViews[i];
                if (view == null) continue;

                var egg = eggs[i];
                switch (egg.State)
                {
                    case EggState.Delivered:
                        Destroy(view.gameObject);
                        _eggViews[i] = null;
                        break;
                    case EggState.Carried:
                        // Float above the carrier's capsule.
                        view.position = egg.Carrier.Position + Vector3.up * (agentVisualScale * 2f + eggVisualScale);
                        break;
                    default: // InNest or Dropped
                        view.position = egg.Position + Vector3.up * eggVisualScale;
                        break;
                }
            }
        }

        private void OnDestroy()
        {
            if (Simulation != null) Simulation.ShotFired -= OnShotFired;
            _logSink?.Dispose();
            _snapshotSink?.Dispose();
        }

        private JsonlFileSink CreateLogSink(ulong seed, string extension)
        {
            string dir = Path.Combine(Application.persistentDataPath, "DinoHunt", "logs");
            string file = $"match_{seed}_{DateTime.Now:yyyyMMdd_HHmmss}.{extension}";
            return new JsonlFileSink(Path.Combine(dir, file));
        }

        private void BakeNavMesh(Transform arenaRoot)
        {
            // Runtime bake: the arena is procedural, so there is no pre-baked scene NavMesh.
            var surfaceGo = new GameObject("NavMeshSurface");
            surfaceGo.transform.SetParent(arenaRoot, false);
            var surface = surfaceGo.AddComponent<NavMeshSurface>();
            surface.collectObjects = CollectObjects.All;
            surface.BuildNavMesh();
        }

        /// <summary>
        /// The greybox is unlit and the scene ships with no lights, but the art models use lit
        /// shaders. Add a sun and a flat ambient so they don't render near-black. Cosmetic only.
        /// </summary>
        private static void EnsureModelLighting()
        {
            foreach (var l in FindObjectsByType<Light>(FindObjectsSortMode.None))
                if (l.type == LightType.Directional) return;

            var sun = new GameObject("Sun").AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.intensity = 1.2f;
            sun.shadows = LightShadows.Soft;
            sun.transform.rotation = Quaternion.Euler(55f, -35f, 0f);

            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.55f, 0.55f, 0.6f);
        }

        private void SpawnAgentViews()
        {
            var agentsRoot = new GameObject("Agents").transform;
            foreach (var agent in Simulation.Agents)
            {
                // The root is what we move and rotate (to the agent's facing). Capsule + gun are
                // unscaled children so the sim never has to know about visual scale.
                var root = new GameObject($"Agent_{agent.Team}_{agent.Id}").transform;
                root.SetParent(agentsRoot, false);

                root.position = agent.Position;
                root.rotation = Quaternion.LookRotation(agent.Facing);
                _agentViews.Add(root);
                _lastAgentCooldown.Add(agent.Weapon.Cooldown);

                if (soldierModel.HasModel)
                {
                    var tint = agent.Team == Team.Blue ? blueTeamTint : redTeamTint;
                    _agentModels.Add(UnitModel.Spawn(soldierModel, root, tint));
                    _agentGuns.Add(null); // the model carries its own weapon
                    continue;
                }
                _agentModels.Add(null);

                var cap = GameObject.CreatePrimitive(PrimitiveType.Capsule);
                cap.transform.SetParent(root, false);
                cap.GetComponent<Renderer>().sharedMaterial =
                    GreyboxMaterials.Get(agent.Team == Team.Blue ? GreyboxMaterials.BlueAgent : GreyboxMaterials.RedAgent);
                // Visualization only; drop the collider so no physics enters the deterministic world.
                Destroy(cap.GetComponent<Collider>());
                cap.transform.localScale = Vector3.one * agentVisualScale;
                cap.transform.localPosition = Vector3.up * agentVisualScale;

                // A thin gun rectangle held forward on the right side, at roughly hand height.
                var gun = GameObject.CreatePrimitive(PrimitiveType.Cube);
                gun.name = "Gun";
                gun.transform.SetParent(root, false);
                Destroy(gun.GetComponent<Collider>());
                gun.GetComponent<Renderer>().sharedMaterial = GreyboxMaterials.Get(GreyboxMaterials.Gun);
                float s = agentVisualScale;
                gun.transform.localScale = new Vector3(0.22f * s, 0.22f * s, 1.1f * s);
                gun.transform.localPosition = new Vector3(0.45f * s, 1.0f * s, 0.7f * s);
                _agentGuns.Add(gun.transform);
            }
        }

        private void OnShotFired(Vector3 shooterPos, Vector3 targetPos)
        {
            float muzzle = agentVisualScale; // roughly gun/hand height
            BulletFx.Spawn(shooterPos + Vector3.up * muzzle, targetPos + Vector3.up * muzzle, bulletSpeed, bulletThickness);
        }

        private void SpawnRaptorViews()
        {
            var raptorsRoot = new GameObject("Raptors").transform;
            foreach (var raptor in Simulation.Raptors)
            {
                var root = new GameObject($"Raptor_{raptor.Id}").transform;
                root.SetParent(raptorsRoot, false);
                root.position = raptor.Position;
                root.rotation = Quaternion.LookRotation(raptor.Facing);
                _raptorViews.Add(root);
                _lastRaptorCooldown.Add(raptor.AttackCooldown);

                if (raptorModel.HasModel)
                {
                    _raptorModels.Add(UnitModel.Spawn(raptorModel, root, null)); // raptors keep their own colors
                    continue;
                }
                _raptorModels.Add(null);

                var cap = GameObject.CreatePrimitive(PrimitiveType.Capsule);
                cap.transform.SetParent(root, false);
                cap.GetComponent<Renderer>().sharedMaterial = GreyboxMaterials.Get(GreyboxMaterials.Raptor);
                Destroy(cap.GetComponent<Collider>());
                cap.transform.localScale = Vector3.one * raptorVisualScale;
                cap.transform.localPosition = Vector3.up * raptorVisualScale;
            }
        }

        private void SpawnEggViews()
        {
            var eggsRoot = new GameObject("Eggs").transform;
            foreach (var egg in Simulation.Eggs)
            {
                var sphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                sphere.name = $"Egg_{egg.Id}";
                sphere.transform.SetParent(eggsRoot, false);
                sphere.GetComponent<Renderer>().sharedMaterial = GreyboxMaterials.Get(GreyboxMaterials.Egg);
                Destroy(sphere.GetComponent<Collider>());

                sphere.transform.localScale = Vector3.one * eggVisualScale;
                sphere.transform.position = egg.Position + Vector3.up * eggVisualScale;
                _eggViews.Add(sphere.transform);
            }
        }

        private SpectatorCamera GetOrCreateCamera()
        {
            var existing = Camera.main;
            if (existing != null)
            {
                return existing.GetComponent<SpectatorCamera>() ?? existing.gameObject.AddComponent<SpectatorCamera>();
            }

            var go = new GameObject("SpectatorCamera");
            go.tag = "MainCamera";
            go.AddComponent<Camera>();
            return go.AddComponent<SpectatorCamera>();
        }
    }
}
