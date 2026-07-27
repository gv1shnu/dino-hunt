# Architecture

How the code is organised, which rules must never be broken, and how to extend it safely.

---

## 1. The three non-negotiables

Everything below exists to protect these. They are cheap to honour from the start and extremely
painful to retrofit.

### Determinism

The same seed must produce a byte-identical match, every time, on every run.

- Every random draw comes from `DeterministicRandom` (SplitMix64), explicitly seeded from the match
  seed. **Never `UnityEngine.Random`** — it is process-global and unseeded.
- Time advances only through `SimClock` at a fixed timestep. Time is computed as `tick × dt`, never
  accumulated, so floating-point error cannot drift.
- **Never `Time.deltaTime` in simulation logic.** Frame rate must not touch outcomes.
- Systems that need randomness fork their own stream (`Rng.Fork()`), so adding a system does not
  shift every existing system's random sequence.

Without this, evolution fitness scores are noise and bugs are irreproducible.

### Sim/render separation

Simulation code must never touch cameras, renderers, materials, or `GameObject`s. If a system cannot
run with rendering disabled, it is in the wrong place.

The test is simple: **the entire simulation compiles and runs outside Unity**, against nothing but
`Vector3` and `Mathf`. This is not theoretical — it is how the batch runner works ([BATCH.md](BATCH.md)).

### The event log

A structured, append-only JSONL stream of semantic events. Statistics, replay, and commentary are all
*readers* of it.

**Statistics are never stored during the simulation — they are derived from the log afterwards.**
If you find yourself adding a `killCount` field to `Agent`, stop: emit an event and count it later.

---

## 2. Layout

```
Assets/DinoHunt/
├── Scripts/
│   ├── Core/      Simulation, SimClock, DeterministicRandom, MatchConfig
│   ├── Sim/       Agent, Raptor, Egg, WorldView, UtilityBrain, Roster, weapons, seams
│   ├── Arena/     ArenaGeometry (pure data), ArenaBuilder (GameObjects), ArenaLayout
│   ├── Log/       EventLog, sinks, JSON read/write
│   ├── Batch/     MatchRunner, BatchRunner, MatchStats, Evolution, headless seams
│   └── View/      Bootstrap, camera, overlays, narrator   ← the only folder that may touch Unity rendering
├── Editor/        Scene setup, Batch Runner window
└── Tests/EditMode/
```

The dependency rule is one-directional: **View depends on Sim. Sim never depends on View.**
`Batch` also depends only on Sim, which is why it runs headless.

---

## 3. The seams

Four interfaces are the entire coupling between the simulation and the world it runs in. Each has a
real implementation and a headless one, and that is what makes the same code run in both places.

| Seam | Rendered game | Headless batch |
|---|---|---|
| `IPathfinder` | `NavMeshPathfinder` — Unity NavMesh as an oracle | `GridPathfinder` — A* over an occupancy grid |
| `ILineOfSight` | `RaycastLineOfSight` — Unity physics | `AnalyticLineOfSight` — segment/AABB slab test |
| `IEventSink` | `JsonlFileSink` + `MatchNarrator` | `InMemoryEventSink` |
| `IAgentBrain` | `UtilityBrain` | same, or `StubBrain` to isolate a mechanic in tests |

**Why the LOS swap is not a fidelity compromise:** every sight-blocking object in the arena is an
axis-aligned box, so a slab test answers exactly the question Unity's raycast answers. The
pathfinding swap *is* an approximation — grid A* does not reproduce NavMesh routes step for step.
Batch runs are internally consistent and reproducible, which is what research requires; they are not
claimed to be identical to a rendered match.

> **Movement note:** `NavMeshAgent` steering is deliberately not used anywhere. NavMesh is queried
> only as a path oracle (`NavMesh.CalculatePath`); the simulation walks agents along the returned
> corners itself, at a fixed speed, in fixed steps. Agent steering is not deterministic.

---

## 4. The frame

One tick, in order:

```
Simulation.Step()
  ├── Clock.Step()                     time = tick × dt
  ├── world.BeginTick(now)             perception clock
  ├── for each agent:  StepAgent()
  │     ├── weapon.Tick()
  │     ├── UpdatePerceptionCallouts() edge-triggered radio; broadcasts to teammates
  │     ├── brain.Decide(agent, world) → Decision      ← "what to want"
  │     └── execute the Decision                        ← "how to do it"
  ├── ResolveSeparation()              push overlapping bodies apart
  ├── for each raptor: StepRaptor()    acquire → encircle → commit → strike
  ├── WriteSnapshots()                 optional Tier-2 training rows
  └── timeout check
```

**Ordering fairness — two mechanisms, both load-bearing.** The roster interleaves teams (blue on even
indices, red on odd), so naive iteration hands blue a systematic edge. Two fixes:

1. **Two-phase damage.** Shots are queued during the agent loop and applied only after every agent
   has acted, so simultaneous fire resolves simultaneously and two agents can kill each other on the
   same tick.
2. **Alternating step order.** The agent loop runs forward on even ticks and backward on odd ones.

The second matters far more than the first, which is not obvious. Matches are decided by egg grabs
much more than by firefights — typically under one combat death per match — and an egg goes to
whoever reaches `UpdateEggInteractions` first. Blue therefore won every *tied* race to the nest, and
that sub-tick edge compounded into a **measured 92% win skew** in a supposedly symmetric game. It was
invisible until the health check (§9) started asserting team balance.

Both fixes preserve determinism exactly.

---

## 5. The AI, in two layers

**Utility AI decides what to want.** `UtilityBrain` scores every available action against the world
state, weighted by the agent's personality, and takes the highest. Pure float maths, no RNG, fixed
action order as a tie-break.

**The simulation decides how to do it.** A `Decision` is executed with existing primitives: move
along a path, close and fire, grab, deliver.

**Personality is not a separate system — it is the weight vector.** Five floats in 0..1:
`aggression`, `greed`, `caution`, `teamplay`, `patience`. The simulation must never know or care
whether those numbers were hand-authored, mutated, or produced by a learning algorithm. That
boundary is what keeps evolution swappable.

Two properties of the scoring are load-bearing and were both *measured*, not assumed:

1. **Leverage.** Every trait must decide something, or agents with different personalities behave
   identically and the primary research question is unanswerable. Each trait owns a primary action
   outright — see the table in `UtilityBrain`.
2. **Survival.** Agents must be able to lose a fight without dying in it. With perfect aim, any
   stand-up trade is mutual destruction, so engagement is priced against the **local force ratio**:
   outnumbered agents disengage, supported agents press.

Both were added after batch data showed them missing. See [RESEARCH.md](RESEARCH.md) §2.

---

## 6. Perception and the radio

Agents are not omniscient. `WorldView.CanPerceive` gates in three stages, cheapest first:
**range → facing cone → line-of-sight raycast.**

Two deliberate simplifications: teammate positions and the nest location are always known. Everything
else — enemies, carriers, raptors, dropped eggs — must actually be seen.

Sound is separate from sight. A raptor's screech reaches every agent **within earshot**, regardless of
facing or cover, for a few seconds. It is bounded in both time and space, and that bound matters
enormously: an unbounded alert is permanently true for everyone, which is indistinguishable from no
signal at all except that it suppresses every other decision. That exact bug shipped and broke the
game — see [DECISIONS.md](DECISIONS.md) D-19.

**The radio is the coordination mechanism.** There is no shared blackboard. When an agent perceives
something worth reporting, the callout is logged *and* pushed to living teammates as a **snapshot of
where the thing was at that moment**. It ages out (`radioMemorySeconds`) and it is frequently already
wrong by the time it is acted on.

That staleness is the feature, not a defect to fix. An agent confidently walking to where an enemy
*was* is the single best thing this design produces.

---

## 7. Raptors

Team-blind, leashed to the nest, killable, and finite — there is no respawn, so the nest can be
permanently cleared at a cost of roughly 2× an agent's health in ammunition per raptor.

Pack behaviour follows the design target *"unpredictable yet coordinated"*:

- **Coordination:** raptors claim **distinct approach arcs** around shared prey, so they enclose
  rather than queue up behind one another.
- **Unpredictability:** which arc, and when each commits from stalking to charging, is re-rolled
  every hunt from the raptor's own RNG stream.

The pattern is learnable — raptors encircle. The instance never is.

---

## 8. Adding a system without breaking anything

1. **Does it need randomness?** Fork a stream. Never `UnityEngine.Random`.
2. **Does it need time?** Use `Config.fixedDeltaTime` and `Clock.Time`. Never `Time.deltaTime`.
3. **Does it touch rendering?** Then it belongs in `View/`, and the simulation must run correctly
   without it.
4. **Did something meaningful happen?** Emit an event. Do not store a counter.
5. **Does it need Unity?** If yes, put it behind a seam interface with a headless implementation, or
   the batch runner and every test break.
6. **Adding a config field?** `MatchConfig.Clone()` uses `MemberwiseClone`, so new fields are copied
   automatically. It was previously a hand-written field list, which silently dropped new fields and
   produced a batch where every run was accidentally identical.
7. **Changing arena generation?** The RNG call order in `ArenaGeometry.Build` is a contract. Appending
   is safe; reordering changes every existing seed's arena.
8. **Run a batch afterwards.** A sudden collapse in eggs delivered, or a 100% draw rate, is a
   behavioural bug report.

---

## 9. Verification

### The health check — "is this build better than the last one?"

`MatchHealth` turns a batch into a **verdict**, because raw statistics can't answer that question:
4.4 eggs per match means nothing without knowing what it was yesterday.

Six criteria, each a design claim rather than a magic number, and **each one exists because the
failure it detects has actually happened**:

| Check | Detects |
|---|---|
| objective works | the egg loop broken (this shipped once — zero eggs, every match a scoreless timeout) |
| agents survive | nobody disengaging; fights as mutual destruction |
| matches resolve | draws dominating |
| carriers get home | agents taking eggs but never delivering |
| **teams balanced** | asymmetry creeping into a symmetric game |
| watchable length | matches too short to read or too long to sit through |

Any `FAIL` means the build is broken, not merely different. The Batch Runner window also keeps the
previous batch and prints a **delta against it**, which is the actual answer to "is it better" — a
single run can't tell you, only a comparison can.

The team-balance check earned the whole feature on its first run by exposing a 92% win skew that had
been present, unnoticed, for the entire project (§4).

### Three levels of checking, in increasing cost

1. **Compile check** — the C# compiles against Unity's API surface without opening the editor.
2. **Headless run** — the simulation is compiled and executed outside Unity entirely, which is how
   determinism and balance are actually verified. See [BATCH.md](BATCH.md).
3. **EditMode tests** — `Tests/EditMode/`, run from Unity's Test Runner.

Levels 1 and 2 need neither the editor nor Play mode, which is what makes iteration fast.
