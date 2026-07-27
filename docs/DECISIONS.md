# Decisions

Every locked decision, why it was made, and — where it matters — what it cost.

Ordered roughly by when they were settled. **Conflicts are called out explicitly**, because a
decision that contradicts something else in the project is exactly the one a future reader will
trip over.

---

## Foundations

### D-1 — Determinism, sim/render separation, and the event log are non-negotiable
Same seed, byte-identical match. Simulation never touches rendering. Statistics are derived from an
append-only log, never stored.

**Why:** without determinism, evolution fitness is noise and bugs are irreproducible. Without
sim/render separation, headless batch mode is impossible. Without the log, every statistic needs its
own bespoke plumbing.

**What it bought:** all three paid off directly. The batch runner exists *because* the simulation had
no visual dependencies and its two Unity couplings were already behind interfaces. See D-19.

### D-2 — Personality is a plain weight vector, not a system
Five floats in 0..1: aggression, greed, caution, teamplay, patience. The simulation must never know
whether they were authored, mutated, or learned.

**Why:** it keeps evolution swappable. Any learning approach can be dropped in behind the same
boundary.

### D-3 — The map is symmetric, always
Non-negotiable for AI-vs-AI. An asymmetric arena makes every result ambiguous — you cannot tell skill
from map advantage. This survives even procedural generation (D-17).

---

## Combat and the economy

### D-4 — Empty magazine → infinite low-damage sidearm, not a knife
An agent who exhausted 90 rounds previously had no combat capability at all — an unrecoverable state
with no interesting decisions in it.

**Chosen over the knife** because it preserves the ranged grammar of the game rather than introducing
a second melee-shaped one, and gives the utility AI a graded low-ammo decision instead of a binary
suicide rush.

### D-5 — Perfect aim, all body damage. No headshots, no accuracy stat.
Every shot in range with line-of-sight hits.

**Why, beyond determinism:** an aim-skill parameter would inject combat RNG and confound the primary
research question, which needs behavioural differences to be attributable to *decisions* rather than
luck. This is a research constraint as much as a design one.

**Cost:** combat is less textured than a real shooter. Accepted — this is not a shooter.

### D-6 — Kills are worth zero points
Tracked as statistics, never added to score.

**Why:** any positive point value on kills drags the whole design back toward deathmatch, which is
the thing it exists to avoid.

### D-7 — Delivering an egg is the only heal in the game
No health packs, no regeneration, no medics.

**Why:** it makes combat and objective play the same strategy rather than competing ones. A team that
only fights slowly rots, because damage is permanent. **Do not add another healing source** — it
destroys the self-balancing property.

---

## Raptors

### D-8 — Team-wipe only wins if no raptor is alive
*Amends the original victory conditions.* If a team is eliminated while raptors live, the match
continues and resolves by eggs, timer, or mutual elimination.

**Why:** outlasting your rivals is not the same as surviving the arena. It also gives raptor-clearing
real strategic weight — it gates the fastest victory condition.

### D-9 — Raptors keep silent movement *and* 30 damage. The screech is the audible tell.
The original tension flagged "silent + heavy damage" as too lethal and asked to pick one.

**Resolution: neither was the problem.** The real bug was that before perception existed, agents
sensed raptors omnisciently through walls, so silence had nothing to bite on. Once perception landed,
the fix was already in the design: raptors screech on aggro, audible across a sector. Sight is gated
by facing and cover; **sound is not**. An agent caught adjacent still gets ambushed — that is the
intended encirclement beat — but nobody beyond claw range is left oblivious.

**This is the clearest case in the project of a tension dissolving once the real cause was found,
rather than being traded away.**

### D-10 — Raptors cost exactly 2× an agent in bullets
Raptor health is pinned to a *ratio*, not an absolute: 26 rifle hits versus the 13 needed to drop an
agent (208 hp at current values).

**Why:** against the finite 90-round pool, one raptor is ~29% of an agent's entire match ammunition.
Raptor-clearing becomes a genuine expenditure that trades against fighting the other team later —
"an expensive team investment" expressed as a number rather than a wish.

### D-11 — No raptor respawn. The nest is clearable.
Raptors are a finite match resource, exactly like eggs.

**Why:** a team can decide to spend ammunition converting the map's dangerous centre into its own
farm. That is a real strategic choice with a real cost.

**⚠ Accepted risk, explicitly:** the late game can go quiet once the nest is cleared. On the
watch-list alongside base camping. To be observed in play, not pre-solved.

### D-12 — Solo raptor hunts are permitted, and usually fatal
The design does *not* forbid a lone agent from attacking a raptor.

**Why:** a reckless, low-caution agent throwing itself at one and dying is legible, emergent, and
worth watching. It is the personality system visibly making a bad decision — which is more valuable
than a system that cannot make bad decisions.

**Correction on the record:** this was originally justified with the claim that a lone agent
*mathematically cannot* kill a raptor (176 damage dealt < 208 health). That was wrong — it ignored the
free-firing window while the raptor closes the distance. The real outcome is borderline and depends
on engagement range. The decision stands; the arithmetic behind it did not.

---

## Perception and coordination

### D-13 — The radio *is* the coordination mechanism. No shared blackboard.
Agents know only what they perceived themselves or what a teammate said out loud.

**Why, and this is the harder choice:** a blackboard would make information sharing free, instant,
and perfectly accurate — quietly undoing the partial observability the perception system exists to
create. Routing everything through explicit callouts means shared knowledge is **sparse, delayed, and
sometimes wrong**, which is what makes flanking and baiting meaningful and what makes research
question Q2 possible at all.

**Cost:** coordination is weaker and harder to tune than a blackboard would give. That is the price
of the drama, and it is being paid deliberately.

### D-14 — Teammate positions and the nest location are always known
A deliberate simplification. Modelling squad-mate awareness as imperfect would require a stale-position
memory for allies too; "you roughly know where your own squad is" is cheaper and more plausible. The
nest is a fixed landmark, not intelligence.

---

## Scope

### D-15 — LLMs are out of this project entirely
Not as gameplay, not as commentary. The former "LLM commentary" milestone is removed.

**Why:** as gameplay, an LLM commander breaks determinism — which corrupts evolution fitness into
noise and makes bugs irreproducible. As commentary, the rule-based narrator already works, is
deterministic, and is free.

**Conflict noted:** the GDD had reopened this question on the grounds that the original real-time
objection was void. It is void — the objection now is reproducibility, which is a different and
stronger one.

### D-16 — Evolution is implemented in C#, not Python
**⚠ This contradicts the GDD**, which specified "Python only later, only for evolution."

**Why the deviation:** the search space is five floats. A Python implementation would need a
serialisation bridge, a second toolchain, and its own determinism story — in exchange for library
convenience that a 5-dimensional search does not need. The C# version reuses `BatchRunner` directly
and runs from one editor button.

**Reversible:** `Evolution.ToCsv` exports the full population, so any external tool can take over
without touching the simulation.

### D-17 — Procedural arena generation varies the arena, never the symmetry
Route shape, cover density, and structure density are re-rolled per match seed. Left/right mirroring
and the three-route structure are never varied.

**Why it matters beyond variety:** agents tuned or evolved against a single fixed arena learn *that
arena*, not the game. Varying it is what keeps a strategy general — which directly protects the
research from overfitting.

**Conflict noted:** the GDD placed arena generation last, arguing "you cannot know what an arena needs
before agents have fought in one." Agents have now fought in many, and the generator is deliberately
conservative — it varies dimensions of an authored layout rather than inventing structure from
nothing. The GDD's caution is respected, not ignored.

### D-18 — Stay in Unity 3D
**Conflict noted, and it is a real one.** The GDD originally rejected a 2D/web version on the grounds
that it would mean losing NavMesh and physics raycasts. **That argument is now void** — the simulation
no longer uses either (see the seams in ARCHITECTURE §3), and the core is portable to TypeScript in
days.

**Staying anyway, because:** the art direction calls for buildings, grass, and humanoid assets, which
is precisely 3D Unity's strength and a web-2D build's weakness. The dimension is also not the
bottleneck — the open problems are coordination and tuning, which a port would not touch.

**The option stays open at no cost.** Keeping the simulation Unity-free is what preserves the exit.

### D-19 — Build the batch runner *before* trusting anything you watched
**The most consequential process decision in the project.**

Until headless batch mode existed, the simulation was verified by watching it. Within seconds of the
first batch run, two problems appeared that watching had not revealed:

1. **The core objective was completely non-functional.** Zero eggs delivered, every match a 0–0
   timeout. Cause: the raptor screech alert had been implemented as *global and unbounded* rather than
   bounded to a sector. With five raptors re-aggroing constantly, the "a raptor is hunting" signal was
   permanently true for every agent on the map, which pushed nest-robbing below fleeing forever.
2. **Personality had no measurable effect on behaviour.** A cautious vector and a reckless one
   produced statistically identical matches — which would have made the primary research question
   unanswerable.

Neither is the kind of thing an observer spots. The first looks like "agents milling around near
their base."

**The general lesson, recorded because it will apply again: an always-on signal is indistinguishable
from no signal, except that it suppresses everything else. And a simulation you can only watch is a
simulation you cannot check.**

### D-20 — Fitness is eggs delivered, not win rate
**Why:** optimising for winning reliably finds degenerate equilibria — camp the chokepoint, run the
clock, win 0–0. Technically optimal, unwatchable. Scoring the objective keeps evolved agents doing the
thing that is interesting to watch, which is the entire point of the project.

A small per-death penalty is included so evolution does not discover that suicide-rushing maximises
pickups.

### D-21 — Art is an active parallel workstream, in Unity 3D
**Conflict noted:** the project's original operating rules said art waits until the AI is proven
interesting, and named character models, terrain, and lighting as explicitly out of scope.

**Overridden by the director**, with the concern raised and reaffirmed. Art now proceeds alongside the
AI work rather than replacing it.

**The engineering constraint that must hold:** the collision / NavMesh / line-of-sight proxy stays as
simple boxes, with detailed visual meshes hung off them as non-colliding children. Detailed mesh
colliders would wreck per-tick raycast cost and NavMesh bake time, and — far worse — would silently
change which sightlines are blocked, which changes decisions, which changes match outcomes. Humanoid
animation must be view-only with **root motion off**: the simulation owns position.

### D-22 — All four research questions are in scope; Q1 is primary
Order: Q1 (personalities) → Q3 (raptor effects) → Q4 (sustain decay) → Q2 (communication).

**Why all four are affordable:** they share infrastructure. Once batch mode and log-derived statistics
exist, Q3 is a config flag and Q4 is pure log analysis.

**Why Q1 is primary — "primary" means it wins when they conflict, and they do:** Q2 varies radio
vocabulary, which changes behaviour and would confound Q1's clustering. And Q1 is load-bearing for Q3
and Q4, because without distinct personalities those questions are running statistics on clones.

### D-23 — The project ends at v1.0, "research complete"
Batch mode, statistics, evolution, replay, and the four questions answered.

**Why this is written down:** the named risk in this project was never technical. It was that an
open-ended sandbox accretes features weekly and concludes nothing. "Done" means the questions are
answered, not that there is nothing left to add. There will always be something left to add.

### D-24 — The raptors can win the match
If both teams are eliminated and raptors still hold the nest, the raptors win outright. Previously
this was scored as a draw.

**Why:** the design's structural claim is that there are *three* forces, not two. A result where both
human teams destroyed each other and the animals kept the nest is not "nobody won" — it is the third
body winning, and calling it a draw hid the most interesting outcome the three-body structure can
produce. Only a board with nothing at all left standing is a true draw.

Observed at 0% under default settings and ~50% when raptors are made numerous and fast, which is the
right shape: a raptor victory should mean both teams overreached, not that the balance is off.

### D-25 — The scoreboard reports points and kills side by side, never summed
End-of-match scoreboard shows deliveries, steals, kills, deaths **split by cause**, raptors downed,
and delivery rate.

**Why the columns are these:** the board is meant to indict as much as praise. An agent with an
excellent K/D and zero deliveries should read as having had a *bad* match — in a normal shooter that
agent tops the table. Deaths are split by cause because that split is a personality fingerprint: a
high raptor-death ratio means an agent living too close to the nest.

The scoreboard is derived from the event log using the **same code the batch runner uses**, so the
on-screen numbers and the research numbers cannot disagree.

### D-26 — The commentator reads the whole match, not just the last event
Event-driven chatter alone leaves long silences while agents cross the map. A periodic situation
report reads live match state — score, who is carrying, force balance, raptors remaining, time left —
and leads with whatever is most dramatic rather than emitting filler.

Strictly read-only: the simulation never learns the commentator exists.

### D-27 — No team switching. Truce instead, and not until Q3 has a baseline.
Two proposals were considered together: agents defecting to the other team, and enemies temporarily
allying against the raptors.

**Team switching rejected.** It breaks symmetry (a 4v6 makes the result ambiguous), bypasses the
carefully priced steal economy, and adds noise to the primary research question. But the decisive
reason is simpler: **there is nothing to defect for.** Kills are worth zero and the only reward is
team score, so defection has no payoff. Manufacturing one means adding individual incentives, which
is precisely what D-6 exists to prevent.

Filed as **premature, not wrong** — it becomes meaningful only if agents ever acquire individual
histories via roster persistence.

**Truce accepted in principle**, because the incentive already exists and agents merely cannot act on
it: raptors are team-blind, a team wipe cannot win while a raptor lives (D-8), and raptors can win
outright (D-24). Both teams already share an interest in dead raptors. A ceasefire costs no symmetry
and transfers nothing, so neither the map nor the economy is disturbed.

**The synthesis worth remembering: truce-breaking *is* betrayal, in a dramatically better spot.**
Defection changes a label; breaking a truce is a decision made under mutual vulnerability, visible to
the observer for seconds beforehand. That is the dramatic irony the project is built around, obtained
without any of the structural costs.

**Gated on Q3 deliberately.** Q3 is a clean A/B — raptors on versus off. Building alliance behaviour
first would destroy the two-body baseline before it is measured. Run Q3, add the truce, re-run: the
comparison *is* the finding.

Full analysis in GDD §11.3.1.

### D-28 — Radio discipline: the first agent to see it calls it, nobody repeats
Vocabulary extended to `raptor_spotted`, `reloading`, `enemy_down`, `raptor_down`, alongside the
existing enemy/carrier/egg callouts.

**Suppression is split by callout type, deliberately:**

- **World-state reports** (enemy, carrier, egg, raptor spotted) are suppressed team-wide for
  `calloutRepeatSuppressionSeconds` after anyone makes them. Five agents each announcing the same
  raptor is noise, not information — real radio traffic is terse because repeating a known fact
  wastes the channel.
- **Kill confirmations and reload calls are exempt.** Each refers to a distinct event or a specific
  speaker, so they are never redundant. Two people reloading at once is precisely when the team needs
  to hear it.

### D-29 — Raptors out-sense agents qualitatively, not at longer range
Raptors gain **scent**: they detect agents within `raptorScentRadius` (20 u) *through cover*, and at
double that against a wounded agent.

**Why not simply longer sight?** It would be wasted. Raptors are leashed 60 u from the nest, so extra
detection range beyond that has nothing to act on. Sight range is capped by the leash, not by timidity.

**Why this is balanced:** the scent radius sits far inside the leash, so raptors gain no additional
*reach* — only certainty within territory they already control. You can break a raptor's line of
sight; you cannot hide from its nose. The counterplay is unchanged and still the right one: stay out
of their bubble, or leave it fast. Wounded agents being smelled from twice as far adds a predator
dynamic that punishes lingering at the nest, which is exactly where lingering should be punished.

### D-30 — Ordering fairness, and the 92% skew it was hiding
Two changes: shots are **queued and applied after all agents act**, and the agent loop **alternates
direction by tick parity**.

**The finding that forced this is worth remembering.** The roster interleaves teams, so blue was
stepped first on every tick. The suspicion was combat — that blue won simultaneous duels. Two-phase
damage fixed that and **changed nothing**, because matches are decided by egg grabs, not firefights:
under one combat death per match. The real bias was that an egg goes to whoever calls dibs first, so
blue won every *tied* race to the nest. Alternating the order took the skew from **92% to 30%**.

Two lessons: **the obvious suspect was wrong**, and a systematic unfairness sat undetected through the
entire project until something started asserting balance automatically.

### D-31 — Every batch reports a verdict, not just numbers
`MatchHealth` evaluates six criteria and prints PASS/WARN/FAIL, and the Batch Runner keeps the
previous batch to print a delta.

**Why:** "is this build better than the last one" is the question that actually matters, and raw
statistics cannot answer it. Every threshold encodes a failure that has genuinely occurred at least
once — most importantly the objective-works check, which would have caught the shipped bug that made
every match a scoreless timeout.

### D-32 — Python is added for search, learning, and analysis. **This reverses D-16.**
D-16 put evolution in C# to avoid a second toolchain, on the grounds that five floats do not justify
another language. That was correct then and stops being correct now: the work ahead is **clustering,
quality-diversity archives, and neural policies**, and research question Q1 *is* an unsupervised
learning problem. Hand-rolling k-means and MAP-Elites in C# would be stubbornness.

**The boundary rule, which is not negotiable: weights cross, decisions never do.** Python writes
config and reads results; a trained network's forward pass runs *inside* the simulation from a plain
weights file. Per-tick RPC is excluded outright — it would cost milliseconds across millions of
decisions and, far worse, make matches unreproducible, destroying evolution fitness, regression
testing, and every research result simultaneously.

This is also why a network is admissible where an LLM was not (D-15): the objection was never "it's a
model", it was "it's nondeterministic and external". A fixed-weight network is a pure function.

**The C# `Evolution` class stays** as the zero-dependency path and the reference implementation any
Python search is validated against. **The simulation must always run and be tunable with no Python
installed** — Python is a research tool bolted on the side, never a runtime requirement.

Full plan in [PYTHON.md](PYTHON.md).

### D-33 — Raptor evolution targets balance, not victory
When raptors get a genome, their fitness is **distance from target match dynamics**, scored against
`MatchHealth` — not agents killed or deliveries denied.

**Why:** raptors are not a competitor, they are the reason the map's centre matters. Optimising them
to win converges on "nobody ever gets an egg" — technically optimal, unwatchable. It is the same
degenerate-strategy trap that made agent fitness eggs-delivered rather than win-rate (D-20), and it
bites harder here because nothing pulls raptors back.

Target-based evolution is automated balance tuning: it converges, it cannot run away, and it is safe
by construction — raptors are team-blind and both teams face the same ones, so evolving them can never
violate symmetry.

---

## Working values

Rules of thumb that emerged and should survive future changes.

1. **Legibility over intelligence.** A readable agent beats a smart one.
2. **Kills are never worth points.**
3. **Delivery is the only healing.**
4. **Three bodies, not two.** The raptors must remain hostile to everyone.
5. **Reward the completed action, not the possession.**
6. **The pattern is fixed; the instance is random.** This is what makes raptors feel intelligent forever.
7. **The observer knows more than the agents.** Protect this gap — it is the source of the best moments.
8. **Symmetric map.** Ambiguous results are worse than boring ones.
9. **Every number is provisional** until observed in a running build.
10. **Measure before believing.** Added after D-19, at some expense.
