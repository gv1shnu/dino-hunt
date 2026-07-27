# Batch mode

How to run thousands of matches without watching any of them.

This is the data pipeline the research questions depend on, and it doubles as the regression harness:
a batch that suddenly reports zero eggs delivered, or a 100% draw rate, is a behavioural bug report.

---

## From the editor

**`DinoHunt → Batch Runner`**

Runs with the editor open. No Play mode, no scene loading, no domain reload — you can keep working
while it runs.

Set match count, first seed, and the config you want to vary, then press **Run**. You get:

- a summary in the window (win split, mean duration, eggs, deaths by cause, delivery rate, end reasons)
- a per-agent CSV written to `<persistentDataPath>/DinoHunt/batch/`, one row per agent per match

The same window has **Run evolution**, which searches the personality space and exports the final
population as CSV.

Throughput is roughly 1–3 matches per second depending on match length and arena size.

---

## What batch mode swaps out

Only two things differ from a rendered match:

| Seam | Rendered | Headless |
|---|---|---|
| `ILineOfSight` | Unity physics raycast | `AnalyticLineOfSight` — segment/AABB slab test |
| `IPathfinder` | Unity NavMesh | `GridPathfinder` — A* over an occupancy grid |

Everything else — utility AI, combat, eggs, raptors, perception, radio, the event log — is *literally
the same code*.

**Fidelity note.** Line-of-sight is exact: every sight-blocking object in the arena is an axis-aligned
box, so the slab test answers the same question Unity's raycast does. Pathfinding *is* an
approximation — grid A* does not reproduce NavMesh routes step for step. Batch results are internally
consistent and reproducible, which is what research requires; they are not claimed to be identical to
a played match.

---

## Running the simulation outside Unity entirely

The whole simulation compiles and runs as plain C#, with no Unity subsystems. This is the fastest way
to check behaviour, and it works even when the editor has the project locked.

Exclude the two Unity-coupled seam implementations — batch injects its own — and compile everything
else against `UnityEngine.CoreModule` for `Vector3`/`Mathf`:

```bash
UNITY="C:/Program Files/Unity/Hub/Editor/6000.3.10f1/Editor"
MONO="$UNITY/Data/MonoBleedingEdge"
UENGINE="$UNITY/Data/Managed/UnityEngine"
FACADES="$MONO/lib/mono/4.5/Facades"

SRC=$(find Assets/DinoHunt/Scripts/Core Assets/DinoHunt/Scripts/Sim \
           Assets/DinoHunt/Scripts/Log  Assets/DinoHunt/Scripts/Batch -name '*.cs' \
        ! -name 'NavMeshPathfinder.cs' ! -name 'RaycastLineOfSight.cs')
SRC="$SRC Assets/DinoHunt/Scripts/Arena/ArenaGeometry.cs Assets/DinoHunt/Scripts/Arena/ArenaLayout.cs"

"$MONO/bin/mono.exe" "$MONO/lib/mono/4.5/mcs.exe" -langversion:latest -out:run.exe \
  -r:"$UENGINE/UnityEngine.CoreModule.dll" -r:"$FACADES/netstandard.dll" \
  $SRC YourHarness.cs

MONO_PATH="$UENGINE;$FACADES" "$MONO/bin/mono.exe" run.exe
```

Three details that will otherwise waste an afternoon:

- **`-langversion:latest` is required.** The bundled mcs defaults to an old C# version and rejects
  named arguments and other modern syntax.
- **`MONO_PATH` must include both** the UnityEngine managed folder and the mono Facades folder, or
  `Vector3` fails to load at runtime.
- **Run `mcs.exe` through `mono.exe`.** Invoking it directly uses the .NET runtime and crashes.

A minimal harness:

```csharp
var config = new MatchConfig { matchTimerSeconds = 600f };
var layout = new ArenaLayout();
var results = BatchRunner.Run(config, layout, matches: 50, firstSeed: 1);
Console.WriteLine(BatchRunner.Summarize(results));
```

---

## Reading the output

`BatchRunner.Summarize` gives the shape of a run at a glance:

```
=== 12 matches ===
blue 5 (42%)   red 6 (50%)   draw 1 (8%)
mean duration   269s
eggs delivered  4.42 per match  (blue 26, red 27)
raptors killed  0.30 per match
deaths          3.40 per match  (by enemy 31, by raptor 10)
delivery rate   61% (mean over agents who took an egg)
cause-of-death  24% raptor
end reasons     all_eggs_delivered 9   timeout 2   mutual_elimination 1
```

What to look at first:

- **eggs delivered near zero** → the objective loop is broken, not the balance
- **deaths approaching the agent count** → nobody is disengaging; fights are mutual destruction
- **mostly `timeout`** → the arena is too large or the timer too long for anything to resolve
- **mostly `mutual_elimination`** → same as high deaths; a draw-heavy game is a degenerate one
- **delivery rate** → the single best measure of whether agents are actually playing the game

`BatchRunner.ToCsv` gives one row per agent per match, which is the input for clustering and any
offline analysis.

---

## Determinism

Same seed produces a byte-identical event stream, verified including with procedural arena generation
active. If two runs of the same seed ever differ, something has reached for unseeded randomness or
frame-dependent time — see ARCHITECTURE §1.

Batch never writes Tier-2 snapshot rows, regardless of config.

---

## Experiment switches

The controls used by the research questions, all on `MatchConfig` / `ArenaLayout`:

| Switch | Effect |
|---|---|
| `perAgentPersonalities` | off = every agent identical (Q1 control) |
| `radioEnabled` | off = nothing said, nothing heard (Q2 control) |
| `calloutEnemy` / `calloutCarrier` / `calloutEgg` | vocabulary size (Q2 variable) |
| `radioMemorySeconds` | how stale a report may be and still be acted on |
| `raptorCount` | 0 = a two-body game (Q3 control) |
| `proceduralVariation` / `variationAmount` | vary the arena per seed so strategies cannot overfit one map |
| `packEncircleRadius` | 0 disables pack behaviour; raptors charge straight in |
