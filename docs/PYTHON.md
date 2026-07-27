# Python and advanced AI

The plan for putting real search, learning, and analysis on top of the simulation.

---

## 1. The one architectural rule

**Weights cross the boundary. Decisions never do.**

Python searches, trains, and analyses. C# simulates. The only things that pass between them are
**configuration going in** and **numbers coming out** — never a per-tick decision.

```
Python                          C#
------                          --
config.json      ──────────►    headless match runner
                                     │
results.csv / events.jsonl ◄─────────┘
weights.bin      ──────────►    inference inside the sim
```

This is not fastidiousness. A per-tick RPC would cost milliseconds per decision across millions of
decisions, and — far worse — it would make matches unreproducible, which destroys evolution fitness,
regression testing, and every research result at once. Determinism is the project's most valuable
technical property and nothing here is permitted to spend it.

A neural policy therefore runs its **forward pass in C#**, reading a plain weights file Python wrote.
A fixed-weight network is a pure function: same input, same output, forever. That is why a network is
admissible where an LLM was not (see DECISIONS D-15) — the objection to LLMs was never "it's a model",
it was "it's nondeterministic and external".

---

## 2. Why Python now

**This reverses DECISIONS D-16**, which put evolution in C# to avoid a second toolchain. That was the
right call for a 5-float genome; it stops being right the moment the work is clustering, archives,
and networks.

What Python actually buys:

| Need | Tool | Why C# is worse |
|---|---|---|
| **Q1 clustering** — the primary research question | scikit-learn | Q1 *is* an unsupervised learning problem; hand-rolling k-means is silly |
| Behaviour-space plots | matplotlib | no realistic C# equivalent |
| Better search than a hand GA | `cma` (CMA-ES) | mature, well-understood, tiny |
| Quality-diversity archives | MAP-Elites | needs real array handling |
| Neural policies | PyTorch | training only; inference stays in C# |
| Log/CSV analysis | pandas | the event log is already the data product |

The C# `Evolution` class stays. It remains the zero-dependency path, and it is the reference
implementation any Python search is checked against.

---

## 3. Phases

### Phase 0 — The bridge *(prerequisite for everything)*

A proper headless CLI for the simulation. Today it can only be driven from the Editor window or an
ad-hoc mono harness; neither is scriptable.

- `dinohunt-sim --config run.json --out results/` — runs N matches, writes `results.csv` + `events.jsonl`
- JSON schema mirroring `MatchConfig` + `ArenaLayout`, so every experiment is a file
- Exit code non-zero if a `MatchHealth` check fails, so batches can gate CI

Small, dull, and unblocks everything else.

### Phase 1 — Analysis, and answering Q1

Zero risk, immediate research value, no changes to the simulation at all.

- pandas over the per-agent CSV
- Cluster agents on behavioural statistics (delivery rate, cause-of-death ratio, K/D, callouts, damage)
- Test whether the clusters **recover the underlying weight vectors** — that is Q1, stated precisely
- Plot the behaviour space

This is the first thing to build. The apparatus for Q1 has existed for a while; the analysis has not.

### Phase 2 — CMA-ES over the personality vector

Drop-in replacement for the hand-written GA. Better convergence on a continuous 5-dimensional space,
handles the noisy fitness (elites are re-scored on fresh seeds) far more gracefully.

Validation: it must beat or match the C# GA on the same seeds. If it doesn't, the bridge is wrong.

### Phase 3 — MAP-Elites *(the one that serves the vision)*

A standard GA converges. Its entire job is to discard variety and return one answer — which is the
opposite of "unexpected behaviour".

Quality-diversity keeps an **archive of the best performer in each region of behaviour space**, binned
on axes like *delivery rate* × *share of time in combat*. The output is not a champion but a **map of
viable ways to play**, including strange corners: never fights and still delivers; kills constantly,
delivers nothing, denies everything.

Three things fall out for free:

- The archive **is** a roster — a cast of genuinely distinct agents, which is what the game wants
- Q1 gets much richer: which regions of behaviour space are even reachable?
- Same batch runner, no gradients, no GPU

Cost: several thousand evaluations. An overnight run, not a coffee break.

### Phase 4 — Raptors get a genome

Raptors currently have **no personality at all** — five identical FSMs sharing ~15 config numbers.

Give them a vector (`aggression`, `patience`, `persistence`, `territoriality`, `focus`) so a pack
stops behaving like clones: one commits early, another stalks. **Worth doing even with no evolution.**

Then evolve them — but **not against the agents**. Raptor fitness is distance from *target match
dynamics*, scored against `MatchHealth`:

```
fitness = −|delivery_rate − target|
          − |raptor_deaths − enemy_deaths|      # a balanced threat, not a dominant one
          − duration_outside_watchable_band
```

Optimising raptors to *win* converges on "nobody ever gets an egg" — technically optimal, unwatchable.
Raptors are not a competitor; they are the reason the map's centre matters. Target-based evolution is
automated balance tuning, it converges, and it cannot run away.

Safe by construction: raptors are team-blind and both teams face the same ones, so evolving them can
never violate the symmetry rule.

### Phase 5 — Neural policies, without losing legibility

The rule: **the network chooses temperament, not actions.**

```
observation (~12 features) → MLP → aggression, greed, caution, teamplay, patience
                                          ↓
                                 existing UtilityBrain scoring
                                          ↓
                                 named action + intent string
```

Every action stays named, every intent label still reads, the scoreboard still means what it means —
while agents gain *context-dependent personality*: desperate when losing, cautious when wounded and
alone, reckless in the last minute.

Trained by neuroevolution or CMA-ES over ~220 weights. **No gradients required**, which sidesteps
sparse rewards entirely. Inference is ~40 lines of C# reading a weights file.

The observation vector already exists — Tier-2 snapshots define it.

Honest failure mode: evolution may find that constant weights beat adaptive ones and the network
flatlines into a fixed personality. That is a **result**, not a bug, and an interesting one.

### Phase 6 — Reinforcement learning *(only with eyes open)*

Possible, not recommended, and last.

Two objections, in order of importance:

1. **It destroys legibility.** Replace the utility layer with a policy network and the honest intent
   label becomes "output 3 was highest". Readable minds are the project's stated core pleasure; a
   black box trades it away for capability nobody asked for.
2. **This environment is hard for RL on four axes at once** — sparse rewards, partial observability,
   multi-agent non-stationarity, long horizons. Any one is manageable. All four is a paper.

If attempted: PPO with recurrence, self-play, heavy reward shaping — and reward shaping reintroduces
exactly the degenerate-strategy risk that scoring on eggs was chosen to avoid.

---

## 4. What is deliberately excluded

- **Per-tick Python in the loop.** Breaks determinism and speed.
- **LLMs.** Still out, for the reasons in D-15.
- **Python as a dependency of the game.** The simulation must always run, and be tunable, with no
  Python installed. Python is a research tool bolted on the side, never a runtime requirement.

---

## 5. Suggested layout

```
tools/
├── dinohunt/
│   ├── runner.py      invoke the headless sim, parse results
│   ├── analysis.py    pandas + sklearn: clustering, Q1
│   ├── search.py      CMA-ES
│   ├── mapelites.py   quality-diversity archive
│   └── plots.py       behaviour-space visualisation
├── experiments/       one file per research question
└── requirements.txt   numpy, pandas, scikit-learn, matplotlib, cma
```

Nothing here imports Unity. Nothing in `Assets/` imports Python.

---

## 6. Order of work

1. **Phase 0** — the bridge. Nothing else moves without it.
2. **Phase 1** — Q1 analysis. The primary research question has been waiting on this, not on features.
3. **Phase 4a** — raptor personality vector. Cheap, and improves the game with or without evolution.
4. **Phase 3** — MAP-Elites. The diversity payoff.
5. **Phase 2 / 4b** — CMA-ES and raptor balance tuning.
6. **Phase 5** — adaptive personality, if static evolved agents turn out not to be interesting enough.

Phases 1 and 4a are the highest value per hour and carry essentially no risk.
