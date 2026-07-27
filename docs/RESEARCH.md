# Research

The questions this project exists to answer, how they are measured, and what the data says so far.

---

## 1. The questions

All four are in scope. **Q1 is primary**, meaning it wins when they conflict.

| # | Question | Status |
|---|---|---|
| **Q1** | Do weight-space personalities produce measurably distinct behaviour? | **Instrumented. Preliminary answer: yes — but only after a fix.** |
| **Q3** | How does a neutral hostile population change competitive equilibria? | Instrumented, not yet run at scale |
| **Q4** | Does objective-gated sustain self-balance? | Instrumented, not yet run at scale |
| **Q2** | Constrained communication under partial observability | Instrumented, not yet run at scale |

**Order: Q1 → Q3 → Q4 → Q2.** This is a dependency chain, not a preference:

- **Q2 contaminates Q1.** Varying radio vocabulary changes behaviour, making communication a confound
  in personality clustering.
- **Q1 is load-bearing for Q3 and Q4.** Without distinct personalities, "strategy distributions" and
  "does combat decay" are statistics over identical clones.

---

## 2. Finding: personality had no leverage, and the objective was broken

This is the most important result so far, and it is a result about the *simulation*, not about agents.

### What happened

Before headless batch mode existed, the simulation was verified by watching it. The first batch run
took four seconds and reported:

```
=== 5 matches, 300s timer ===
eggs delivered  0.00 per match
end reasons     timeout 5
```

**Zero eggs delivered. Every match a scoreless timeout.** The core objective of the game was
completely non-functional and had been for some time.

**Cause:** the raptor screech alert — the sound-based signal that lets agents react to a raptor they
cannot see — had been implemented as *global and unbounded*. No position, no radius. With five
raptors re-aggroing constantly and a six-second window, the signal was permanently true for every
agent on the map. That pushed nest-robbing permanently below fleeing, so no agent ever took an egg.

**The general lesson:** an always-on signal is indistinguishable from no signal, except that it
suppresses everything else.

### The second finding

With the objective restored, a follow-up run tested whether personality mattered at all. Three very
different weight vectors, both teams sharing each vector in turn:

| Vector | eggs/match | deaths/match | mutual elimination |
|---|---|---|---|
| brawler (aggression .9, caution .2) | 2.80 | 10.0 | 10/10 |
| thief (greed .9) | 3.10 | 9.7 | 9/10 |
| turtle (caution .9, patience .8) | 3.00 | 10.0 | 10/10 |

**A turtle match was indistinguishable from a brawler match.** Deaths were 10.0 out of 10 agents in
almost every configuration — *everyone died, every match*, so there were never survivors and the
team-wipe victory condition could not fire.

This would have made Q1 unanswerable. Not because the premise is wrong, but because the utility
scoring gave personality no authority over the outcome.

**A hypothesis that the data killed:** the mutual elimination was initially assumed to be an artifact
of both teams sharing one personality on a mirrored map. Wildly different vectors changed nothing, so
that explanation was wrong. The real cause was structural: with perfect aim and no force-awareness,
any stand-up fight is mutual destruction, and no personality trait could opt out of it.

### The fix

Two changes to `UtilityBrain`, both aimed at the measured problem:

1. **Leverage.** Each trait now owns a primary action outright, with wide enough coefficients to
   decide outcomes rather than nudge them.
2. **Survival.** Engagement is priced against the **local force ratio** — outnumbered agents
   disengage, supported agents press. Agents can now lose a fight without dying in it.

### Results

| Metric | Before | After |
|---|---|---|
| Eggs delivered / match | 0.00–0.30 | **4.42–4.92** (of 5) |
| Deaths / match (of 10 agents) | 10.0 | **3.4** |
| Mutual elimination | 10/10 | **1/12** |
| Decisive results | 0/10 | **11/12** |

And personality now changes behaviour measurably — mean match duration by roster:

| Configuration | duration | decisive | callouts |
|---|---|---|---|
| uniform roster (control) | 169s | 11/12 | 15 |
| per-agent roster | 269s | 9/12 | 24 |

A mixed cast plays a visibly longer, more talkative, less decisive match than a monoculture. That is
the signal Q1 needs.

---

## 3. Method

### Measurement

All statistics are **derived from the event log**, never accumulated during the simulation
(`Batch/MatchStats.cs`). Per agent, per match:

- eggs picked up, stolen, delivered, dropped
- kills, deaths split by **cause** (enemy vs raptor), raptors downed
- shots fired, damage dealt, callouts made
- **delivery rate** = delivered ÷ taken — the single best measure of an agent
- **cause-of-death ratio** = raptor deaths ÷ total deaths — a readable greed dial

The death split is a personality fingerprint. High raptor-death ratio means an agent living too close
to the nest; the scoreboard should be able to expose an agent with an excellent K/D and zero eggs as
having had a *bad* match.

### Controls

Each question has a clean control condition, switchable from config:

| Question | Variable | Control |
|---|---|---|
| Q1 | `perAgentPersonalities` | uniform roster — every agent identical |
| Q2 | `radioEnabled`, `calloutEnemy/Carrier/Egg` | radio off — nothing said, nothing heard |
| Q3 | `raptorCount` | zero raptors — a two-body game |
| Q4 | match duration analysis | no switch; measured from logs |

`radioEnabled = false` is a *complete absence* — no callouts logged and none broadcast — rather than
"they still talk but nobody listens", so the control is clean.

### Determinism

Every result is reproducible. Same seed → byte-identical event stream, verified including with
procedural arena generation active. Without this, fitness scores would be noise.

---

## 4. Evolution

Rung 1 of the training ladder: a generational GA with elitism and tournament selection over the
five-float personality vector (`Batch/Evolution.cs`). No neural networks, no GPU.

**Fitness is eggs delivered, minus a small per-death penalty. Not win rate.** Optimising for winning
reliably discovers degenerate equilibria — camp the chokepoint, run the clock, win 0–0. Technically
optimal, unwatchable. The penalty exists so evolution does not instead discover that suicide-rushing
maximises pickups.

Candidates are evaluated against the fixed authored roster on seeds that change every generation, so
a vector cannot win by fitting one match. **This means reported best fitness is not monotone** —
elites are re-evaluated on fresh problems. A hall-of-fame tracks the best-ever separately.

A short exploratory run (16 individuals, 6 generations, 3 matches each) produced:

```
aggression 0.55   greed 0.95   caution 0.09   teamplay 0.90   patience 0.25
```

A reckless, highly cooperative thief — high greed and high teamplay with almost no self-preservation.
Not a vector anyone would have hand-authored, and exactly the kind of result that makes weight search
worth running.

**This is a single short run and should not be read as a finding.** It is evidence the instrument
works.

---

## 5. What remains

1. **Run Q1 properly.** Large batch, per-agent roster, export per-agent CSV, cluster on behavioural
   statistics, and test whether the clusters recover the underlying weight vectors. The apparatus is
   built; the analysis is not.
2. **Q3:** batches with `raptorCount = 0` versus 5, comparing strategy distributions.
   **Run this before building the proposed truce mechanic** (GDD §11.3.1). Alliance behaviour would
   change the two-body equilibrium, so the baseline has to be measured first — then the truce is added
   and the batch re-run, and the *comparison* is the result. Building it first destroys the control.
3. **Q4:** measure whether combat-heavy agents decay over match time relative to objective-focused
   ones, from existing logs.
4. **Q2:** sweep radio vocabulary size against team performance.
5. **Tuning that needs data, not design:** raptor speed versus carrier speed (the most consequential
   single number in the game), match timer, nest-to-base distance, delivery zone geometry.

---

## 6. Honest caveats

- **Batch pathfinding is not NavMesh.** Grid A* does not reproduce Unity's routes step for step.
  Batch runs are internally consistent and reproducible — what research requires — but are not
  identical to a rendered match. Line-of-sight *is* exact, because every blocker is an axis-aligned
  box.
- **Sample sizes so far are small.** The numbers in §2 come from 10–12 match batches. They are large
  enough to show a broken objective and a 3× shift in death rate; they are not large enough for
  claims about subtle personality differences.
- **The step-order bias is unfixed.** Agents act in fixed list order, so in a perfectly simultaneous
  mirror duel the lower-indexed agent shoots first. This slightly favours blue in symmetric trades.
- **No result here has been confirmed in a rendered match.** Everything in this document comes from
  headless runs.
