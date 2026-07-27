# Dino Hunt — Documentation

**Dino Hunt is an AI ecosystem sandbox.** Two teams of autonomous agents raid a raptor nest to steal
eggs and carry them home, while raptors — hostile to both teams — hunt whoever is most isolated.
A human watches. A human never plays.

**The game is the environment. The AI is the product.**

---

## Start here

| Document | What it is |
|---|---|
| **[GDD.md](GDD.md)** | The design. What the game is, what every rule is for, and which questions are still open. |
| **[ARCHITECTURE.md](ARCHITECTURE.md)** | How the code is built. The seams, the determinism rules, and how to add a system without breaking either. |
| **[DECISIONS.md](DECISIONS.md)** | Every locked decision and *why*, including the conflicts and how they were resolved. |
| **[RESEARCH.md](RESEARCH.md)** | The four research questions, the method, and what the data actually says so far. |
| **[BATCH.md](BATCH.md)** | How to run thousands of matches without watching any of them. |
| **[PYTHON.md](PYTHON.md)** | The plan for Python-side search, learning, and analysis. |
| **[CONTINUATION.md](CONTINUATION.md)** | Handoff prompt for picking this up on another machine. |

If you read one thing after this page, read **DECISIONS.md** — it is the reasoning that the code
cannot tell you.

---

## Where the project actually is

Milestones 1 through 9 and 11 are built, plus dynamic arena generation (13). What remains is
mostly art, replay, and running the experiments.

| Built | Not yet built |
|---|---|
| Determinism, fixed timestep, seeded RNG | Art: buildings, grass, humanoids, lighting |
| Movement on NavMesh, intent labels | Replay from event log |
| Combat, finite ammo, sidearm floor | Corpse looting for ammo |
| Event log (JSONL) — the spine | Auto-director camera |
| Eggs, carry penalty, delivery, heal-on-deliver | Roster persistence between sessions |
| Raptors: chase, leash, screech, **killable**, **pack encirclement** | Web distribution |
| Utility AI — 11 actions | |
| Perception: FOV, line of sight, sound alerts | |
| **Radio as a two-way coordination mechanism** | |
| **End-of-match scoreboard; raptors can win outright** | |
| **Commentary that reads live match state** | |
| **Per-agent personalities** | |
| **Headless batch mode + statistics** | |
| **Evolutionary weight search** | |
| **Procedural arena generation** | |
| | *Proposed:* truce vs raptors (GDD §11.3.1, gated on Q3) |

---

## The one-paragraph version of what happened

The simulation was verified only by watching it until headless batch mode was built. Within seconds
of the first batch run, two problems appeared that watching had not revealed: **the core objective
was completely non-functional** (zero eggs delivered, every match a 0–0 timeout, caused by a bug in
the perception system), and **personality had no measurable effect on behaviour** — a cautious agent
and a reckless one played identically, which would have made the primary research question
unanswerable.

Both are fixed. Eggs delivered went from 0.0 to ~4.6 of 5 per match, agent deaths from 10.0 (every
agent, every match) to ~3.4, and decisive results from 0/12 to 11/12. The full numbers are in
[RESEARCH.md](RESEARCH.md).

The lesson is recorded in [DECISIONS.md](DECISIONS.md) as D-19: *a simulation you can only watch is
a simulation you cannot check.*

---

## Quick start

**Watch a match:** open `Assets/DinoHunt/Scenes/Arena.unity` and press Play.

**Run a thousand matches:** `DinoHunt → Batch Runner` in the editor menu. No Play mode needed, works
while you do something else. See [BATCH.md](BATCH.md).

**Search for new personalities:** same window, *Run evolution*. See [RESEARCH.md](RESEARCH.md).

> **Remember the scene gotcha.** `MatchConfig` and `ArenaLayout` are serialized onto the Bootstrap
> object in the scene. Changing a default *in code* does not reach the existing scene — re-run
> `DinoHunt → Setup Arena Scene`, or edit the values in the Inspector. This has caused confusion in
> almost every working session.
