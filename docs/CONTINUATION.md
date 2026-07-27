# Continuation prompt

Paste the block below into a fresh session on the new machine. It is written to be self-contained.

> **Note on what survives the move:** everything that matters is in `docs/` inside the repo. Assistant
> session memory does **not** transfer between machines, which is exactly why the decision log,
> architecture notes, and research findings were written into the repository rather than left in a
> conversation. Treat `docs/` as the single source of truth.

---

## Copy from here

I'm continuing an in-progress project called **Dino Hunt**, now on a Mac (it was previously on
Windows). Working directory: `<PATH TO REPO>`

**What it is:** an AI-ecosystem sandbox built in Unity. Two teams of five autonomous agents raid a
raptor nest to steal eggs and carry them home, while five raptors — hostile to both teams — hunt
whoever is most isolated. There is no human player and none is planned; a human only spectates. The
game is the environment, the AI is the product. My goal is to watch AI agents compete and take
pleasure in their abilities, with **coordination and unexpected behaviour** as the target.

**Read these before doing anything. They are the source of truth, not my memory or yours:**

- `docs/README.md` — index and current state
- `docs/DECISIONS.md` — **read this one properly.** D-1 to D-33, every locked decision and *why*,
  with conflicts flagged. It is the reasoning the code cannot tell you.
- `docs/ARCHITECTURE.md` — seams, determinism rules, the tick order, and a checklist for adding a
  system without breaking anything
- `docs/GDD.md` — the design
- `docs/RESEARCH.md` — the four research questions, method, and measured findings
- `docs/BATCH.md` — how to run thousands of matches headlessly
- `docs/PYTHON.md` — the plan for Python-side search, learning, and analysis

**Three non-negotiables** (details in ARCHITECTURE §1): determinism — same seed, byte-identical match,
never `UnityEngine.Random` or `Time.deltaTime` in sim logic; sim/render separation — the simulation
must run with rendering disabled; and statistics are always *derived from the event log*, never
accumulated during the sim.

**State:** the simulation is feature-complete against the GDD. Built: utility AI with 11 actions,
per-agent personalities from a named roster, perception (FOV + line of sight + sound), a two-way radio
that is the team's only coordination mechanism, killable raptors with pack encirclement, procedural
arena generation, headless batch mode, statistics, evolutionary weight search, an end-of-match
scoreboard, and a `MatchHealth` check that grades every batch PASS/WARN/FAIL and diffs it against the
previous run. Remaining: art (buildings, grass, humanoids, lighting — my call, deferred for now),
replay from event log, and actually running the four experiments.

**Next up: Python.** See `docs/PYTHON.md`. Phase 0 is a headless CLI for the sim (`--config x.json
--out results/`), then Phase 1 is the Q1 clustering analysis, then raptor personality vectors, then
MAP-Elites. The boundary rule is absolute: **weights cross between Python and C#, decisions never do**
— no per-tick RPC, because it would destroy determinism.

**How I work:** explain the architecture before implementing and wait for my OK; propose one
recommendation rather than a menu; raise design tensions instead of silently resolving them; don't
work ahead of what we agreed. When you find something wrong with my reasoning, say so directly — that
has been more useful than agreement several times over.

**How we verify — this matters, please read.** I usually have the Unity editor open, which locks the
project, so `Unity -batchmode -runTests` will not work. Instead:

1. **Compile-check** the C# against Unity's DLLs without opening the editor.
2. **Run the simulation headlessly** by compiling the sim subset and executing it outside Unity
   entirely. This is how balance and determinism are actually verified, and it is the single most
   valuable tool in the project — it has caught bugs that were invisible when watching the game,
   including one that made the core objective completely non-functional.

The exact Windows command lines are in `docs/BATCH.md`. **They will need rewriting for macOS** — the
Unity install lives at `/Applications/Unity/Hub/Editor/<version>/Unity.app/Contents/`, with managed
DLLs under `Contents/Managed/UnityEngine/` and mono under `Contents/MonoBleedingEdge/`. Please work
out the Mac equivalents early and **update `docs/BATCH.md` with them**, since everything downstream
depends on being able to run a batch.

**Gotcha that has bitten us in nearly every session:** `MatchConfig` and `ArenaLayout` are serialized
onto the Bootstrap object in the scene. Changing a default *in code does not reach the existing
scene*. After any config change, remind me to re-run `DinoHunt → Setup Arena Scene` or edit the
Inspector.

Start by reading the docs and telling me what you think the highest-value next step is.

## Copy to here

---

## Notes for whoever picks this up

**Toolchain differences to expect on macOS**

| | Windows | macOS |
|---|---|---|
| Unity root | `C:\Program Files\Unity\Hub\Editor\<ver>\Editor\` | `/Applications/Unity/Hub/Editor/<ver>/Unity.app/Contents/` |
| Managed DLLs | `Data\Managed\UnityEngine\` | `Managed/UnityEngine/` |
| Mono | `Data\MonoBleedingEdge\` | `MonoBleedingEdge/` |
| Roslyn | `Data\DotNetSdkRoslyn\csc.dll` via `dotnet` | same, if `dotnet` is installed |

Three things that cost real time on Windows and will again:

- `mcs` must be invoked **through** `mono`, not run directly.
- `-langversion:latest` is required, or the bundled compiler rejects modern C# syntax.
- `MONO_PATH` must include both the UnityEngine managed folder **and** the mono Facades folder, or
  `Vector3` fails to load at runtime.

**Verifying the move worked**

Run a batch and check the health report. Current healthy baseline, roughly: ~4.8 of 5 eggs delivered
per match, ~2–3 of 10 agents dying, 100% decisive, ~70–80% delivery rate, team skew under 35%,
120–200s mean duration. If any `MatchHealth` check fails after the move, something in the toolchain
differs — the simulation itself is deterministic and platform-independent apart from floating-point
edge cases.

**Do not re-litigate** anything in DECISIONS.md without reading the entry first. Several of those
calls look wrong until you read why they were made — particularly D-9 (raptors keep silent movement
*and* 30 damage), D-13 (no shared blackboard), D-20 (fitness is eggs, not wins), and D-30 (the 92%
win skew that was hiding in tick ordering, where the obvious suspect turned out to be wrong).
