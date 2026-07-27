# Dino Hunt — Game Design Document

**Status:** in development — simulation feature-complete, art and experiments remaining
**Type:** AI research project — autonomous agents, no human player
**Engine:** Unity (3D project, angled spectator camera, primitive geometry pending art)
**Document version:** v0.4 — coordination, pack behaviour, per-agent personalities, batch mode, evolution, and procedural arenas all built and verified.

> This is the design document. For how the code works see **[ARCHITECTURE.md](ARCHITECTURE.md)**, for
> why each rule was chosen see **[DECISIONS.md](DECISIONS.md)**, for measured results see
> **[RESEARCH.md](RESEARCH.md)**, and for running experiments see **[BATCH.md](BATCH.md)**.

---

## 0. How to read this document

Sections marked **[LOCKED]** are decided. Sections marked **[OPEN]** are unresolved and are the most useful places to push. Sections marked **[TENSION]** are decisions that are locked but have a known problem attached — these are the highest-value discussion targets, because the fix isn't obvious. **[RESOLVED]** marks a former tension that has since been settled; the reasoning is kept rather than deleted, because *why* a tension dissolved is usually more useful than the answer.

Numbers throughout are starting points for tuning, not claims. Anything expressed as a concrete value should be assumed wrong by ±50% until observed in a running build.

### 0.1 Where the build actually is

| Built | Not yet built |
|---|---|
| Determinism, fixed timestep, seeded RNG | Art: buildings, grass, humanoids, lighting |
| Movement on NavMesh, intent labels | Replay from event log |
| Combat, finite ammo, sidearm floor | Corpse looting for ammo |
| Event log (JSONL) — the spine | Auto-director camera |
| Eggs, carry penalty, delivery, heal-on-deliver | Roster persistence between sessions |
| Raptors: chase, leash, screech, killable, **pack encirclement** | Web distribution |
| Utility AI — 11 actions incl. **Escort, Intercept, Investigate** | |
| Perception: FOV, line of sight, bounded sound alerts | |
| **Radio as a two-way coordination mechanism** | |
| **Per-agent personalities (named roster)** | |
| **Headless batch mode + statistics derived from the log** | |
| **Evolutionary weight search** | |
| **Procedural arena generation** | |

The simulation is feature-complete against this document. What remains is art, replay, and actually
running the experiments — see [RESEARCH.md](RESEARCH.md) §5.

---

## 1. Concept

### 1.1 One-line pitch

Two teams of autonomous agents raid a raptor nest to steal eggs and carry them home, while the raptors — hostile to everyone — hunt whoever is most isolated. A human watches.

### 1.2 Origin and intent

The project did not begin as a shooter. It began with the desire to **watch AI agents compete and take pleasure in their abilities**. The shooter is the arena, not the point. Every design decision should be evaluated against whether it makes agent behavior more legible and more interesting to observe, not whether it would make a good game to play.

The project has since been reframed explicitly as an **AI research project**. There is no human player and none is planned. The spectator view is an interpretability tool, not a game mode.

### 1.3 The core aesthetic goal: legible minds

The central pleasure is not "smart AI" — it is **readable AI**. Watching an agent *decide* to flank is far more satisfying than watching it flank. This is cheap to implement and disproportionately valuable. Everything that surfaces agent reasoning to the observer is a first-class feature, not UI polish:

- Per-agent intent labels ("hunting Mara", "egg secured — running")
- Team radio callouts
- Visible target priority and threat assessment
- Post-match statistics that reveal playstyle

### 1.4 The three-body structure

The design's key structural property is that it has **three forces, not two**: your team, the enemy team, and the raptors — who obey neither side. The neutral hostile population is what elevates the design above deathmatch. Every moment poses a three-way question: take the egg, fight the enemy, or flee the thing now sprinting at you.

### 1.5 Dramatic irony as the payoff

The spectator camera sees the whole arena. Agents do not. This gap is the design's highest-value moment: the observer watches a raptor pack silently encircle an agent whose intent label still reads "grabbing egg." The observer knows. The agent does not.

This is the emotional target the entire project builds toward.

---

## 2. Match structure

### 2.1 Format [LOCKED]

- Team vs team, symmetric, identical loadouts.
- Target format: **5v5**. Development format: **1v1**, scaling 1v1 → 2v2 → 5v5. **Current working format: 5v5 with 5 raptors** (reached 2026-07-19; was 3v3 with 1 raptor).
- **No respawn.** An agent that dies is out for the match.
- Single continuous match. No rounds, no round resets.

### 2.2 Victory conditions [LOCKED]

Resolved in priority order:

1. **Team wipe** — if all agents on one team are dead, the opposing team wins **only if no raptor is alive** (see below).
2. **All eggs delivered** — if every egg in the match has been banked, the team holding the majority wins.
3. **Timer expiry** — the team with more eggs delivered wins.
4. **Tie** — possible if egg counts are equal at timer expiry.

**Team-wipe raptor clause [LOCKED — amends the original rule].** A team wipe does not win while a raptor still lives. If one team is eliminated with raptors alive, there is no winner yet and the match *continues*, resolving by eggs, the timer, or a later mutual elimination.

**The raptors can win outright [LOCKED].** If both teams are eliminated and raptors still hold the nest, **the raptors win the match** — not a draw. This is the third body actually winning, and it is the cleanest expression of §1.4: there are three forces here, and only one of them lives at the nest. Only if nothing at all is left standing is the result a genuine draw.

Observed frequency: 0% under default settings, ~50% when raptors are made numerous and fast. Rare and dramatic, which is the intent — a raptor victory should mean both teams overreached.

The reasoning: surviving your rivals is not the same as surviving the arena. The nest is still lethal, and a team that outlasted the other but cannot handle the raptors has not actually won anything. This also gives raptor-clearing (§8.2) real strategic weight — it is the gate on the fastest victory condition.

### 2.3 Match timer [TENSION]

Currently specified at **10 minutes**. This is very likely far too long.

No-respawn formats resolve quickly — Valorant rounds run roughly 100 seconds for precisely this reason. A 1v1 match under these rules will likely resolve in 60–90 seconds. A 10-minute timer means most of the match is spent watching an empty arena containing one corpse.

**Recommendation:** treat the timer as a backstop that should almost never fire. Start at 3 minutes for 1v1 and scale with team size. Measure actual match durations in the first batch runs and set the timer to roughly 1.5× the observed median.

### 2.4 Ties [OPEN]

Ties are permitted. Egg count is currently **5** (odd) specifically to make clean splits unlikely. Ties remain possible if an egg becomes stranded and is never recovered — see §4.4.

Unresolved: whether a tie should be broken by a secondary metric (eggs picked up, damage dealt, agents surviving) or left as a genuine draw. For research purposes a genuine draw is cleaner; for spectacle it is unsatisfying.

---

## 3. The economy

### 3.1 Core principle [LOCKED]

Modeled on Valorant's spike: **the carrier receives nothing for carrying.** Possession is a liability. All reward is paid on the completed action — delivery.

### 3.2 Point values [LOCKED]

| Action | Points | Notes |
|---|---|---|
| Grab egg from nest | 0 | No reward for possession |
| Deliver fresh egg | 3 | Plus full heal |
| Kill enemy carrier and take egg | 1 | The steal bonus |
| Deliver stolen egg | 2 | One point less than fresh |
| Kill (no egg involved) | 0 | Deliberate |

**Kills are worth zero points.** This is load-bearing. Any positive point value on kills causes agents to drift back toward deathmatch behavior, which is the thing the design exists to avoid. Kills are tracked as statistics but do not contribute to score.

### 3.3 The heal rule [LOCKED] — most important rule in the design

**Delivering an egg fully heals the carrier.** This is the only source of healing in the game. There are no health packs, no regeneration, no medics.

Consequences, all intentional:

- A team that ignores eggs and only fights will win early engagements and then slowly rot, because every point of damage they take is permanent.
- A team that runs eggs takes greater risks but resets its health each delivery.
- Combat and objective play are not competing strategies — combat has no sustain of its own, so the objective feeds the fighting.
- The agent doing the most dangerous work is the one kept alive to keep doing it, which produces emergent "star players" over a match.

**Do not add other healing sources.** Doing so destroys the self-balancing property.

### 3.4 Why stolen eggs are worth less [LOCKED]

Without the reduction, a stolen egg would be worth more in total than a fresh one (steal bonus + full delivery value), and stealing is safer than nest-robbing because the nest has raptors. Both teams would then wait near each other's routes, nobody would visit the nest, and the map's center would stop mattering.

The one-point reduction keeps interception profitable without letting it dominate. No team can win on interception alone, because eggs must originate at the nest.

---

## 4. Eggs

### 4.1 Properties [LOCKED]

- **Count:** 5. Odd deliberately, to make tied splits unlikely.
- **No respawn.** Eggs are a finite resource for the whole match.
- **Origin:** all eggs start in the nest at map center.
- **Carry capacity:** one per agent (assumed; see §4.5).

### 4.2 Carrying costs [LOCKED]

- **Movement speed reduced** — starting value ~65% of base.
- **Raptors preferentially target carriers** — heavy weight in raptor target selection.
- **The carrier can still shoot.** This is deliberate. A carrier that cannot fight is cargo; a carrier that can fight is a participant in their own escape. Every heist becomes a fighting retreat.

### 4.3 Losing an egg [LOCKED]

- **Killed by an enemy:** egg is dropped and can be picked up by anyone. Killer receives the steal bonus if they take it.
- **Killed by a raptor:** the raptor **drags the egg back to the nest.** Nobody receives a steal bonus.
- **Eggs cannot be forced out of a carrier by damage.** The carrier must die.
- **Dropped eggs stay where they fall.** No reset, no return timer.

The asymmetry between enemy deaths and raptor deaths is intentional: dying to a raptor costs your team more, which should make agents fear raptors more than each other.

### 4.4 Stranded eggs [TENSION]

With no respawn and drop-in-place, an egg can end up in open ground that no agent is willing to cross. If neither team retrieves it, the "all eggs delivered" victory condition never triggers and every match runs to the timer.

Not fatal, but it is the main reason the timer must be a sane length. Monitor the frequency of stranded eggs in batch runs. If it is high, options include a slow drift back toward the nest, a visibility beacon after N seconds, or accepting it as a genuine feature (contested no-man's-land objects are interesting).

### 4.5 Open questions [OPEN]

- Can an agent carry more than one egg?
- Can an agent voluntarily drop an egg, or hand one to a teammate?
- Is pickup instantaneous, or a channel with a vulnerable window? (A channel would make nest robbery meaningfully riskier and would interact well with raptor aggro.)

---

## 5. Bases and delivery

### 5.1 Properties [LOCKED]

- One base per team, positioned at opposite ends of a symmetric map.
- **Delivery requires standing in the zone for 2 seconds.**
- Base interiors are impenetrable. Enemies cannot enter.
- Bases are defended by mounted guns in fiction, but these are **not implemented** and do not fire.
- The base **alerts its team over radio** when enemies are camping outside, but does not intervene.

### 5.2 Base camping [TENSION] — most likely dominant strategy

Delivery is the only source of healing. Camping the enemy delivery zone denies their sole sustain, and with no respawn the camped team has no way to reset the situation. The 2-second stand-in requirement worsens this: it is a long time to be stationary and predictable at a known location.

The base alert helps only marginally — an alert without an enforcement mechanism is a notification that you are losing.

**Watch for this in the first batch runs.** If camping dominates, likely fixes in order of preference:

1. Make the delivery zone large, or give it multiple entrances, so it cannot be covered from one position.
2. Give the delivery zone partial cover.
3. Make the mounted guns real (undesirable — removes agency).
4. Reduce the stand-in duration.

---

## 6. Combat

### 6.1 Locked decisions [LOCKED]

| Property | Value |
|---|---|
| Hit detection | Hitscan |
| Time to kill | Long |
| Damage zones | Headshots deal increased damage |
| Ammunition | Finite — 90 rounds |
| Loadout | Identical for all agents |
| Looting | Dead agents can be looted for ammo/weapons if not already taken |
| Friendly fire | Off |
| Abilities | None |
| Weapons | Rifle only — knife and pistol removed |

### 6.2 Long TTK interacts well with no respawn [LOCKED]

Long time-to-kill means fights are survivable and disengagement is possible. This is the correct pairing with permanent death — short TTK plus no respawn would make first contact decisive and matches would be over in seconds.

### 6.3 The empty magazine problem [RESOLVED]

With the knife and pistol removed, an agent who exhausted 90 rounds had **no combat capability whatsoever** — an unrecoverable state with no interesting decisions in it.

**Resolution: an infinite-ammo low-damage sidearm as a floor** (not the knife). Chosen over the melee option because it preserves the ranged-combat grammar of the game rather than introducing a second, melee-shaped one, and because it gives the utility AI a graded low-ammo decision (fight on at reduced effectiveness vs disengage) rather than a binary suicide-rush.

Implemented: rifle 30-round magazine + 60 reserve, auto-switching to the sidearm once the pool is spent. Headshots remain **deferred** — all damage is body damage, keeping TTK long and combat deterministic with no aim model.

The sidearm's damage is deliberately weak enough that running dry is still a real loss, just not a dead end.

### 6.4 Carrier survivability [TENSION]

Carriers are slow, raptor-flagged, cannot drop the egg, and face long TTK. A slow target that cannot disengage tends to absorb an entire magazine.

If observed delivery rates approach zero in testing, **carrier movement speed is the first dial to turn.**

### 6.5 Open questions

- ~~Weapon spread, recoil, or accuracy model — or is aim perfect?~~ **RESOLVED: perfect aim.** Every shot in range with line-of-sight hits.
- ~~Whether agents have an aim-skill parameter (a research variable) or perfect aim with only decisions varying.~~ **RESOLVED: perfect aim, decisions only.** An aim stat would introduce combat RNG and confound the primary research question (§14.2) — behavioral differences between agents must be attributable to decision weights, not luck.
- ~~Headshot multiplier.~~ **Deferred** — no aim model means no hit locations; all damage is body damage.
- **Current values, all provisional:**

| | damage | interval | DPS | kill agent | kill raptor | range | magazine | reload |
|---|---|---|---|---|---|---|---|---|
| Rifle | 8 | 0.18s | 44.4 | 13 hits (2.34s) | 26 hits | 45 | 30 (+60 reserve) | 2.0s |
| Sidearm | 20 | 0.45s | 44.4 | 5 hits (2.25s) | 11 hits | 30 | 100, infinite reserve | 1.0s |

Agent 100 hp. Raptor 208 hp.

**⚠ Two consequences of the current sidearm values, recorded because they are not obvious:**

1. **The sidearm never fires.** Measured across 24 matches: zero. Nobody exhausts 90 rifle rounds before a match ends, so the sidearm is presently a real weapon on paper and dead code in play. Making it matter means shrinking the rifle pool or lengthening matches — not buffing the sidearm further.
2. **If it ever does fire, it inverts the raptor pricing.** A raptor costs 26 rounds from the rifle's finite pool but only 11 from the sidearm's infinite one, so running dry would make raptor-clearing *cheaper* rather than harder — the exact opposite of §8.2's "expensive team investment". Latent today; it activates the moment matches run longer.

- Still open: whether these numbers are *right*, which needs play data rather than design. (§16)

---

## 7. Perception and communication

This system is the most distinctive part of the design and the strongest research target.

### 7.1 Vision [LOCKED — implemented]

- **No hive mind.** Agents have individual perception.
- Field of view and line-of-sight checks per agent.
- Information does not propagate automatically between teammates.

**Implementation.** Perception is a three-stage gate, cheapest test first: **range** → **facing cone** → **line-of-sight raycast**. Starting values: sight range 80 units, FOV 140° (a wide "alert" cone, deliberately not FPS-narrow — these are agents scanning a battlefield, not aiming down sights).

Two deliberate simplifications:

- **Teammate positions are always known.** Modeling squad-mate awareness as imperfect would require a stale-position memory for allies too; "you roughly know where your own squad is" is the cheaper and more plausible assumption.
- **Nest location is always known.** It is a fixed landmark, not intelligence.

Everything else — enemies, enemy carriers, raptors, dropped eggs — must actually be perceived. An agent cannot engage, flee from, or call out what it cannot see.

### 7.2 Radio [LOCKED] — the standout system

Teammates share information through **explicit radio callouts**, modeled on team comms in tactical shooters.

Why this matters beyond flavor: per-agent intent labels do not scale. Ten simultaneous thought-bubbles are unreadable. Callouts are **sparse, event-driven, and express team-level intent**, which scales to any team size and reads better than individual labels ever could.

The radio channel is the spectator's primary window into agent reasoning.

Callouts should be triggered by events, not generated continuously. A starting vocabulary:

- Enemy spotted (with location)
- Enemy carrier spotted (with location and route)
- Egg spotted (dropped eggs, with location)
- Raptor aggro / raptor at nest
- Taking fire / requesting support
- Carrying egg, returning
- Base under pressure (from the base itself)
- Agent down (with cause)

**Implementation [LOCKED — built].** Callouts are **edge-triggered on perception**: one fires on the tick something newly enters an agent's perception, not every tick it remains visible. Enemy-carrier sightings suppress the redundant plain-enemy callout. Implemented: `enemy_spotted`, `enemy_carrier_spotted`, `egg_spotted`, plus raptor aggro/screech, egg, and death events.

**The radio is two-way, and it is the team's only coordination mechanism.** A callout is logged for the observer *and* pushed to living teammates as a **snapshot of where the thing was at that moment**. It ages out after `radioMemorySeconds` and is frequently already wrong by the time it is acted on. Agents commit to it anyway — the `Investigate` action exists precisely to send an agent to where a teammate *said* something was.

That staleness is the feature. It is what makes §7.6's payoff real: an agent walking confidently to an enemy that has already moved.

**Vocabulary is a tunable variable.** Each callout kind can be switched off independently, and the whole radio can be disabled. This is the independent variable for research question Q2 — see [RESEARCH.md](RESEARCH.md).

### 7.3 Sound [LOCKED]

| Source | Audible? |
|---|---|
| Agent footsteps | Yes, when nearby |
| Gunfire | Yes |
| Raptor screech | Yes, across a whole sector |
| Raptor footsteps | **No — raptors move silently** |

### 7.4 Raptor lethality [RESOLVED]

A raptor claw strike deals **30 damage** in one hit, and raptors are silent while moving. The concern was that silent approach plus heavy damage deletes an agent from behind with no counterplay.

**Resolution: keep silent movement and keep 30 damage. No new audio tell was needed — the screech already is one.**

The tension was diagnosed wrongly at first. The real problem was never the silence; it was that before perception existed, agents sensed raptors *omnisciently through walls*, so the silence had nothing to bite on and the danger felt arbitrary. Once perception landed, the correct fix turned out to already be in the design: **raptors screech on aggro, audible across the sector** (§8.4). That is the audible tell.

So the model is now coherent:

- **Sight** is gated by range, facing, and cover — a raptor outside your cone genuinely is invisible to you.
- **Sound** is not. The screech reaches every agent regardless of facing or cover, for a few seconds after it fires.

An agent caught adjacent when aggro triggers still gets ambushed — that is intended, and it is the encirclement beat (§8.1). But nobody beyond claw range is left oblivious, because the scream reaches them. A perceptive agent reacts; a greedy one at the nest is already too close. Exactly the split the original recommendation asked for, achieved with a rule that already existed.

The two reactions this feeds are graded: seeing the raptor produces a precise flee vector *away from it*; merely hearing it produces a vaguer, position-blind pull back toward safety.

### 7.5 Information rules [LOCKED]

- Egg locations are **not** globally known. If an agent sees an egg, they may call it out; otherwise the team does not know.
- **The enemy's delivered egg count is public.** Both teams always know the score.

### 7.6 Why this design matters

Partial observability is what makes flanking, baiting, and deception meaningful. It also makes the intent overlay far more interesting, because the observer gets to watch agents act confidently on **wrong or stale information** — one of the most entertaining things an autonomous agent can do.

---

## 8. Raptors

### 8.1 Design reference

The tall grass ambush from *The Lost World: Jurassic Park*. The scene works because of four properties, none of which require the animals to be intelligent:

1. **Concealment** — you never see them, you see the wake in the grass.
2. **Encirclement** — they enclose rather than charge; there is no single direction to face.
3. **Isolation targeting** — they take whoever separates from the group.
4. **Stalk, then explode** — long patient nothing, then sudden total commitment.

The design target is **unpredictable yet coordinated**.

### 8.2 Base properties [LOCKED]

- **Killable**, but with a very slow health depletion rate compared to agents. Killing a raptor is an expensive team investment, not something one agent does casually.
- **Team-blind.** Raptors do not distinguish red from blue. This is the entire point of a third body.
- **Leashed to the nest region.** Raptors do not roam the map freely.
- **No respawn.** Raptors are a finite resource for the match, exactly like eggs. The nest is **clearable**.

**The cost of a raptor, in bullets [LOCKED].** Raptor health is pinned to a ratio, not an absolute: **killing a raptor costs twice the ammunition of killing an enemy agent.** At current values that is 208 health — 26 rifle hits, against the 13 needed to drop an agent.

This ratio is the whole design. Against the finite 90-round pool (§6.1), one raptor is roughly 29% of an agent's total ammunition for the entire match. Raptor-clearing is therefore a genuine strategic expenditure that trades directly against the ability to fight the other team later — which is precisely the "expensive team investment" this section demands, expressed as a number rather than a wish.

**No respawn is a deliberate, load-bearing choice.** It means clearing raptors permanently changes the match: the nest becomes safe, egg-robbing becomes free, and the fastest victory condition (§2.2 team wipe) unlocks. A team can decide to spend its ammunition converting the map's dangerous center into its own farm. **Accepted risk:** the late game can go quiet once the nest is cleared. This is on the watch-list alongside base camping (§5.2) — to be observed in play, not pre-solved.

### 8.2.1 Fighting a raptor: the flee-or-fight fork [LOCKED]

Agents choose, per tick, between running from a raptor and attacking it. Both options only exist when the raptor is actually perceived (§7.1) — you cannot hunt what you cannot see, though you can still flee what you merely heard (§7.4).

The choice is scored, not scripted. Hunting is favoured by aggression, by **having teammates nearby**, and by the raptor already being wounded; it is suppressed while carrying an egg (a carrier runs the egg home, it does not brawl) and while badly hurt. Fleeing is favoured by caution and by proximity.

**Solo hunts are permitted, and they are usually fatal.** The design explicitly does *not* forbid a lone agent from attacking a raptor. A reckless, low-caution agent throwing itself at one and dying is legible, emergent, and worth watching — it is the personality system visibly making a bad decision, which is more valuable than a system that cannot make bad decisions. The "safety in numbers" weighting means grouped agents commit and lone agents mostly do not, without any hard rule saying so.

**Retaliation.** A raptor remembers who recently shot it and is somewhat more likely to pick that agent as its next target. This is deliberately weak — priority #4 in §8.3, below carrier and isolation — and is only re-evaluated when the raptor next chooses a target, so a committed hunt is never interrupted mid-chase. It is enough to make shooting a raptor feel consequential and to enable the baiter role (§11.4), not enough to let agents freely puppet raptor aggro.

Leashing matters: a roaming raptor is random noise that punishes agents for existing. A leashed raptor turns the nest into a *place with rules* — a danger zone entered deliberately at a cost. This is what makes the map's center meaningful.

### 8.3 Aggro and targeting [LOCKED]

**Aggro triggers:**
- Proximity to the nest
- Line of sight
- **Gunfire nearby**

The third trigger is doing important work: fighting near the nest summons raptors onto both teams, which naturally pushes combat outward onto the routes and keeps the nest a *stealing* problem rather than a *shooting* problem.

**Target priority:**
1. Egg carriers (heavy weight — this is what makes the carrier penalty real)
2. **The most isolated agent** — weighted by distance from nearest teammate
3. Nearest visible agent
4. Whoever last damaged it

Implemented as a single weighted score rather than a strict ordering — carriers dominate outright, isolation dominates distance, and recent-attacker is a modest additive nudge (§8.2.1). A strict priority list would make the raptor snap between targets on ties; blending keeps it smooth. The relative weights are dials, not doctrine.

**Isolation targeting is the single best rule in the raptor design.** It creates a genuine dilemma that fights directly against the combat system: grouping protects against raptors but makes the team a clustered target for enemy rifles; spreading out is safe from bullets and lethal from raptors. Neither answer is correct, and the utility AI must price this continuously.

**Give up conditions:**
- Line of sight lost for ~4 seconds, or
- Leash distance from nest exceeded

### 8.4 The screech [LOCKED] — key emergent mechanic

**Raptors screech on aggro**, audible across a whole sector.

This means **the nest cannot be robbed quietly.** Any theft announces itself. The nest is therefore always contested loudly, and the radio system gets its most dramatic callout: *"raptor's up at the nest — someone's stealing."*

This single rule converts the nest from a resource node into an information battleground.

### 8.5 Pack behavior [LOCKED — built]

The mechanism for "coordinated yet unpredictable":

- **Coordination via claimed approach arcs.** Raptors hunting the same prey each claim a distinct arc around it and hold station there while stalking. No two take the same line, so the target is *enclosed* rather than charged.
- **Unpredictability via randomized instance.** Which arc each raptor claims, and when each commits from stalking to the final rush, is re-rolled every hunt from that raptor's own deterministic stream.

The result: the *pattern* is learnable (raptors encircle — this is what makes them feel intelligent) while the *instance* never is (which one, from where, when — this is what keeps them tense).

Tuned by `packEncircleRadius`, `packCommitDelay`, and `packCommitJitter`. Setting the radius to 0 disables packing entirely, which is the experimental control.

**Note on the original plan:** this was specified as needing a shared blackboard. It did not — each raptor counts how many others already target its prey and claims the next arc. The coordination is real but requires no shared structure, which keeps it consistent with the no-blackboard decision made for agents (§11.5).

### 8.6 Concealment: the nest in tall grass [OPEN]

Proposal: site the nest inside a tall grass field.

This merges the objective and the danger zone into one location and does real mechanical work — concealment cuts line of sight **both ways**. Agents robbing the nest cannot see raptors, cannot see each other, and cannot see the enemy team stealing beside them.

It also enables the **wake mechanic**: an agent does not see a raptor, they see grass moving. Partial information is both scarier and cheaper to compute than full information.

### 8.7 Open questions

- ~~Raptor respawn after death — yes/no, and delay.~~ **RESOLVED: no respawn. Raptors are finite; the nest is clearable (§8.2).**
- Raptor count and scaling formula (floor of 3 for pack behavior). Currently **5**, which clears the pack-behavior floor — but pack behavior itself is not built, so these are five independent hunters, not a pack. [OPEN]
- Raptor movement speed relative to carriers and to un-encumbered agents. **This ratio is critical:** slower than a carrier means raptors are never scary; much faster means eggs never get home. **Current values put a carrier at exactly 8.00 and a raptor at exactly 8.00 — a dead heat.** A fleeing carrier can no longer be run down in a straight line; raptors catch one only by cornering it or through the encirclement arcs (§8.5).

  Measured effect of moving the carrier from 7.15 to 8.00: deaths by raptor **halved** (49 → 26 across 24 matches), and the raptor share of all deaths fell from 68% to 51%. Match duration also nearly halved, since carriers now survive the run home.

  This is the single most consequential number in the game and it is currently balanced on a knife edge — any change to `agentSpeed` moves the carrier off the tie, because carry speed is a multiplier. [OPEN]
- Attack rate and animation commitment. [OPEN]
- Whether raptors defend the nest passively or actively patrol its perimeter. [OPEN]
- **New, introduced by killable raptors:** with 5 raptors at 208 health, fully clearing the nest costs ~130 rifle rounds — more than a single agent's entire match pool. Is a full clear meant to be achievable by a committed team, or effectively out of reach? This directly gates how often the team-wipe victory condition (§2.2) can ever fire. [OPEN — measure in play]
- **Also new:** gunfire already aggros raptors within range, so deliberately shooting one at the nest may wake the others. Whether that reads as a thrilling escalation or an unfair swarm is unknown until observed. [OPEN — measure in play]

---

## 9. Map

### 9.1 Locked decisions [LOCKED]

- **Symmetric.** Non-negotiable for AI-vs-AI. Asymmetric maps require balance testing that is not affordable solo, and make every result ambiguous — was the win skill, or the map? Symmetry also halves the level design work.
- **Mostly flat.** Mild elevation only. Verticality explodes NavMesh complexity, complicates line-of-sight logic, and degrades the spectator camera. Deferred indefinitely.
- **Three routes per side**, connecting each base to the nest. One route makes every match identical; two is a coin flip; three creates genuine rotation decisions and gives radio callouts something meaningful to communicate.
- **Cover distribution:** heavy on the flank routes, sparse on the middle route.

### 9.2 Layout

```
   [BLUE BASE] ==== north route (long, covered) ====\
        |                                            \
        |======== mid route (short, exposed) ========[ NEST ]==== (mirrored) ====[RED BASE]
        |                                            /
        \======= south route (long, covered) =======/
```

The mid route is fastest but nearly coverless — a poor carry route and an excellent sniping lane. The flanks are slower but survivable. **This tradeoff is the thing radio callouts are about.**

### 9.3 Open questions [OPEN]

- Nest-to-base distance. This is the **primary tuning dial** for the whole game — it sets how long each heist remains dangerous. Too short and eggs are free; too long and nobody scores. Start long and cut down.
- Sightline lengths, which determine whether rifle range matters.
- Cover density values.
- Delivery zone size and entrance count (see §5.2).
- Whether routes intersect or are fully separate.

---

## 10. Statistics and telemetry

### 10.1 Tracked per agent [LOCKED]

| Stat | Notes |
|---|---|
| Eggs delivered | Fresh |
| Eggs stolen | From enemy carriers |
| Kills | Enemy agents |
| Deaths — by enemy | Tracked separately |
| Deaths — by raptor | Tracked separately |
| K/D | Derived |
| Raptors downed | If raptors remain killable |

**Points and kills are explicitly unrelated.** Points measure egg work; kills measure combat ability. They are reported side by side and never summed.

### 10.2 Why the death split matters

Death attribution is a **personality fingerprint**:

- High raptor deaths, low enemy deaths → greedy. Lives at the nest, over-commits on grabs.
- High enemy deaths, low raptor deaths → reckless in fights, respects the nest.
- Low both, low eggs delivered → passive. Technically alive, contributing nothing.

The scoreboard should be able to expose an agent with an excellent K/D and zero eggs delivered as having had a *bad* match. In a normal shooter that agent tops the board; here the columns indict them.

### 10.3 Derived statistics [LOCKED]

- **Delivery rate** = eggs delivered ÷ eggs picked up. The single best measure of an agent in this game. Separates "grabs a lot and dies" from "grabs less and gets home."
- **Cause-of-death ratio** = raptor deaths ÷ total deaths. A readable greed dial. Above ~0.5 indicates an agent living too close to the nest.

### 10.4 The key insight linking stats to AI

**These statistics are downstream of the personality weights.** An agent with a high aggression weight will *produce* a high raptor-death ratio. This means the roster can be tuned empirically: run many headless matches, examine stat distributions, adjust weights until agents play as differently as their descriptions claim.

---

## 11. Agent AI

### 11.1 Architecture [LOCKED]

Two layers:

- **Utility AI decides what to want.** Scores available actions against the current world state; highest score wins. Cheap, tunable, and the source of personality.
- **Behavior tree / GOAP decides how to do it.** Executes the chosen action. Deterministic and debuggable.

The per-agent loop, running every frame or every few frames:

```
1. Sense        raycasts, positions, health, audio events, radio messages
2. Score        utility AI weighs all available actions
3. Select       highest-scoring action wins
4. Execute      behavior tree runs the chosen action
5. Act          movement, aim, fire
6. Publish      surface chosen action as intent label + any radio callout
   → loop
```

### 11.2 Personality is weights [LOCKED]

**Personality is not a separate system.** It is the utility weight vector. Two agents with identical logic and different weights on aggression, self-preservation, greed, and patience will play visibly differently.

Practical consequence: the entire roster of distinct characters is a small table of floats, tunable in the Inspector and searchable programmatically.

### 11.3 Action set

**Implemented [LOCKED]** — the utility AI scores these every tick and takes the highest:

| Action | Driven primarily by |
|---|---|
| `GrabEgg` — go rob the nest | greed |
| `DeliverEgg` — run the carried egg home | greed, caution when hurt |
| `Engage` — attack an enemy agent | aggression, local force ratio |
| `Intercept` — hunt an enemy carrier specifically | greed + aggression |
| `HuntRaptor` — attack a raptor (§8.2.1) | aggression, teammates nearby |
| `FleeRaptor` — run from a seen raptor, or pull back from a heard one | caution |
| `Retreat` — withdraw when hurt or outnumbered | caution |
| `Escort` — stay with a carrying teammate | teamplay |
| `Regroup` — close on a teammate when isolated | teamplay |
| `Investigate` — act on a teammate's radio callout | teamplay + aggression |
| `Hold` — do nothing | patience |

Each trait owns at least one action outright. This is not decoration: if situation modifiers swamp the weights, agents with different personalities behave identically and the primary research question cannot be answered. That failure was measured and fixed — see [RESEARCH.md](RESEARCH.md) §2.

**Not yet built [OPEN]:**

- Bait a raptor away from the nest deliberately. The retaliation rule (§8.2.1) is the mechanical hook, and the behaviour already emerges accidentally; making it an explicit scored action is the remaining step.
- Loot a corpse for ammo (§6.1 specifies dead agents as lootable).

### 11.3.1 Temporary alliance, and betrayal [PROPOSED — gated on Q3]

Two related ideas were considered: agents **switching teams** (betrayal), and enemies **temporarily
allying** against the raptors. They are not equally good, and the analysis is worth keeping because
the conclusion is not obvious.

**Team switching is rejected.** Four reasons, the last being decisive:

1. **It breaks symmetry.** A 5v5 that becomes 4v6 makes "red won" ambiguous — skill, or the defector?
   Symmetry is non-negotiable precisely to avoid ambiguous results (§9.1).
2. **It breaks the egg economy.** §3.4 prices stolen eggs at 2 against a fresh 3 so interception stays
   profitable without dominating. An agent walking an egg across the line is a free steal that
   bypasses that pricing entirely.
3. **It contaminates research question Q1.** Behavioural statistics would become partly a function of
   *when* an agent switched, adding noise to the primary question for a feature that does not serve it.
4. **There is nothing to defect *for*.** Kills are worth zero (§3.2) and the only reward is team score,
   so an agent gains nothing by switching. Making betrayal rational requires individual incentives —
   which is exactly what the zero-kill-points rule exists to prevent, because it drags the design back
   toward deathmatch.

Betrayal is therefore **premature rather than wrong**. It needs an individual-incentive layer the game
deliberately lacks. Should roster persistence across a season land (§15), agents gain individual
histories and reputations, and it becomes worth revisiting.

**Temporary alliance is accepted in principle.** The incentive already exists in the rules and agents
simply cannot act on it: raptors are team-blind, a team wipe does not win while a raptor lives (§2.2),
and the raptors can take the match outright. Both teams already share an existential interest in dead
raptors, and the game currently forces them to ignore it.

- It costs no symmetry — both sides get the same option, situationally.
- It costs no economy — a ceasefire transfers nothing.
- It is legible: *"holding fire — raptor problem"* reads instantly, and gives the radio a new register.
- It emerges from existing weights: caution and teamplay drive willingness to truce, aggression drives
  breaking it. No new trait strictly required.

Risks: it could flatten inter-team combat if truces are cheap, and it accelerates nest clearing, which
worsens the already-accepted "quiet late game" risk of §8.2.

**The synthesis — truce-breaking *is* betrayal, in a better place.** The moment the last raptor drops
and two teams stand in the same clearing with weapons still lowered, and someone shoots first. That
delivers the betrayal instinct without touching symmetry, the economy, or research validity. It is
also better drama than defection: defection changes a label, whereas truce-breaking is a decision
under mutual vulnerability, visible to the observer seconds before it happens — which is exactly the
dramatic irony §1.5 names as the emotional target.

**Deliberately gated on research question Q3.** Q3 is currently a clean A/B — raptors on versus off.
Adding alliance behaviour before running that baseline means never learning what the two-body
equilibrium looked like without it. Run Q3, then add the truce and re-run; **the comparison is the
finding**, and it makes this the cheapest feature in the document to justify afterwards.

### 11.4 Emergent roles

These were not designed. They fall out of the rules, and the utility AI only needs the *option* to want them:

- **Thief** — commits to the grab, accepts the movement penalty.
- **Escort** — sticks to the carrier, shoots what chases. Exists only because carriers are slow and raptor-flagged.
- **Baiter** — deliberately provokes a raptor away from the nest so a teammate can rob it clean. Never scripted; emerges from target-lock rules. Likely the best spectator moment in the game.
- **Interceptor** — ignores the nest, camps enemy routes. Economically viable because of the steal bonus.

### 11.5 Open questions

- ~~Full weight schema.~~ **RESOLVED:** five traits, each 0..1 — **aggression, greed, caution, teamplay, patience**. Every agent currently carries an identical vector; varying it per agent is the next milestone.
- ~~The scoring functions themselves.~~ **RESOLVED** and implemented (§11.3), though every constant in them is provisional and untuned.
- ~~Team coordination architecture.~~ **RESOLVED: the radio *is* the coordination mechanism.** No shared blackboard. Agents are fully independent minds that know only what they perceived or what a teammate said out loud — if it wasn't broadcast, it isn't known.

  This is the more demanding choice and the deliberate one. A blackboard would make information sharing free, instant, and perfectly accurate, which would quietly undo the partial observability that M7 exists to create. Routing everything through explicit callouts means shared knowledge is *sparse, delayed, and sometimes wrong* — which is what makes flanking, baiting, and acting on stale information meaningful (§7.6), and what makes research question Q2 possible at all.

- ~~Whether agents have an aim-skill parameter or perfect aim.~~ **RESOLVED: perfect aim, all body damage.** An accuracy stat would inject combat RNG and confound the primary research question, which needs behavioral variation to come from decisions rather than luck (§14.2).
- **New:** should personality also weight how an agent treats *received* information — a trusting agent acting on a teammate's stale callout versus a skeptical one ignoring it? This would make the radio a personality surface rather than a data bus, and is arguably the most interesting version of §14.2's second research question. [OPEN]

---

## 12. Presentation

### 12.1 Greybox decision [LOCKED]

All 3D visual content is stripped for now. **Critically: this strips the visuals, not the 3D project.**

- Remain in a **3D Unity project** with an overhead camera.
- Replace all models with primitives: capsules for agents, larger capsules for raptors, cubes for cover, a plane for ground, spheres for eggs.
- Flat unlit colors. No lighting, no post-processing, no materials.

**Why not Unity 2D:** the 2D pipeline is a separate toolchain — sprites, `Rigidbody2D`, 2D colliders, and **no built-in NavMesh**. Going true-2D means writing or buying pathfinding, and returning to 3D later means rewriting movement, collision, and line-of-sight. Staying 3D keeps `NavMeshAgent` and `Physics.Raycast` working; restoring art later is a model swap and a camera tilt.

**Cut:** Mixamo models, animation controller, terrain sculpting, materials, lighting, all art decisions.
**Keep:** 3D project, NavMesh, physics, raycast line-of-sight, all AI, all UI, callouts, statistics.

The existing Mixamo and character controller work is **shelved, not discarded** — that pipeline knowledge is exactly what the art stage will require.

### 12.2 What the greybox buys

- **Trivial level iteration.** Cubes on a plane means rebuilding the arena in minutes and testing multiple layouts in an afternoon.
- **Readable top-down layouts** that can be shared as screenshots and analyzed for sightlines, route length, cover spacing, and chokepoints.
- **No sunk cost in art** before the AI is proven fun to watch.

### 12.3 What is lost

Raptor menace. A top-down view flattens the "large thing charging at you" feeling. This matters less than it might, because the target pleasure is *spectator* pleasure — an encirclement forming around an oblivious agent reads **better** from directly overhead than from any perspective angle. Visceral scare is traded for tactical dread, and tactical dread is the actual product.

### 12.4 Spectator view

**Decided and built:**

- **Camera:** angled 3D free-fly (WASD, edge-pan, right-drag look, scroll dolly), framed at a fixed distance from the nest. The earlier "fit the whole arena in frame" auto-framing was abandoned once the arena grew — at current scale it pushed the camera uselessly far back.
- **Intent overlay:** per-agent floating label showing id, health, ammo/reload state, and the current intent string. Ammo was added because the reload animation alone (the gun tilting up) is easy to miss at 5v5.
- **Radio + commentary panels:** blue radio top-left, red radio top-right, commentary bottom-centre. Each holds a 4-line queue, newest on top, sliding in from below — deliberately sized small enough to leave the arena readable.

**Still open:**

- Auto-director camera that follows the action instead of being driven manually.
- Replay / kill-cam (see §12.5).

### 12.5 Distribution — putting it on the web [OPEN]

Two viable paths, not yet chosen:

1. **Unity WebGL export.** Nearly free given what exists; hosts on itch.io or GitHub Pages. Risk: WebGL is single-threaded, and the arena is now large with dense scatter geometry and a runtime NavMesh bake — untested under that constraint.
2. **A custom browser replay viewer** that reads the JSONL event log directly and renders it without a Unity runtime. Much lighter to load, and philosophically the better fit — the event log is already the spine (§13.2), so the sim and the viewer would share nothing but data. But it is effectively building the replay milestone twice unless it *is* the replay milestone.

**Recommendation:** WebGL first for the cheap win, and treat the custom viewer as the real answer once replay-from-log exists, at which point option 2 costs almost nothing extra.

---

## 13. Technical foundation

### 13.1 Three things that must be built now [LOCKED]

These are cheap on day one and extremely painful to retrofit.

1. **Headless batch mode.** No rendering, no camera, fixed timestep, faster than real time. Target 100+ matches per minute. Without this there is no data pipeline, only anecdotes.
2. **Determinism.** Every RNG explicitly seeded. Fixed timestep. No unseeded `Random` or frame-dependent `deltaTime` in game logic. Same seed must produce the same match every time. This makes bugs reproducible and fitness scores meaningful rather than noisy.
3. **Sim/render separation.** Game logic must never depend on anything visual. If raptor aggro lives in a script that requires a camera, headless mode breaks.

Because there is no human player, the simulation **no longer needs to run at 60fps.** It needs to be correct and fast in aggregate. Expensive per-agent reasoning is affordable; only the spectator view runs at human speed.

### 13.2 Event log — Tier 1 [LOCKED]

Sparse, semantic, JSONL. One line per event. This is simultaneously the commentary source, the statistics source, and the debugging tool. Statistics are **never stored** — they are derived from this log.

```json
{"t":12.34,"match":"m_0041","seed":88213,"type":"egg_pickup","actor":"viktor","team":"red","pos":[34.2,0,17.8],"egg":"e_03"}
{"t":19.02,"match":"m_0041","type":"death","actor":"viktor","cause":"raptor","killer":"raptor_2","carrying":"e_03","damage":30}
```

Event types to capture:

`match_start` · `match_end` · `spawn` · `intent_change` · `shot_fired` · `damage` · `death` (with `cause: enemy|raptor` and killer id) · `egg_pickup` · `egg_drop` · `egg_delivered` · `egg_stolen` · `raptor_spawn` · `raptor_aggro` · `raptor_screech` · `raptor_damage` · `raptor_death` · `radio_callout` · `ammo_pickup` · `loot`

Implemented so far: everything above except `ammo_pickup` and `loot`, which await the corpse-looting action (§11.3).

### 13.3 State snapshots — Tier 2 [OPEN — only if training]

Dense, numeric, ~10 Hz, Parquet. Per agent per tick: position, velocity, health, ammo, has_egg, visible enemy count, visible teammate count, distance to nearest raptor, distance to nest, distance to own base, time remaining, score differential — plus the action chosen.

This is the `(observation, action)` dataset. **Do not build until actually training something.**

### 13.4 Open questions

- ~~Unity version and render pipeline.~~ Unity 6.3 LTS, URP.
- Season/roster persistence format between sessions. [OPEN]
- Log rotation and storage strategy at scale. [OPEN]

### 13.5 Procedural arena generation [LOCKED — built]

Route shape, cover density, and structure density are re-rolled per match seed. **Left/right mirroring and the three-route structure are never varied** — symmetry is non-negotiable (§9.1), and an asymmetric arena would make every result ambiguous.

**Why this matters beyond variety:** agents tuned or evolved against a single fixed arena learn *that arena*, not the game. Varying it is what keeps a strategy general, which directly protects the research from overfitting.

The generator is deliberately conservative: it varies the dimensions of an authored layout rather than inventing structure from nothing. The GDD originally placed this last, arguing you cannot know what an arena needs before agents have fought in one. Agents have now fought in many, and that caution is reflected in the design — this is a variation system, not a level designer.

Controlled by `proceduralVariation` and `variationAmount` on `ArenaLayout`. Resolution is idempotent, so geometry, waypoints, and pathfinding all agree on the same resolved arena.

---

## 14. Research framing

### 14.1 The reframe

The project is no longer a game with a fun criterion. It is a research project, which removes the built-in success signal. **This is the primary risk** — without an explicit question, the project becomes an infinitely extensible sandbox that adds features weekly and concludes nothing. This failure mode is far more likely than running out of technical ability.

### 14.2 Research questions [LOCKED — all four in scope, Q1 primary]

1. **Do weight-space personalities produce measurably distinct behavior?** Cluster agents by behavioral statistics and test whether the clusters recover the underlying weight vectors. Unsupervised learning on own telemetry. **← PRIMARY**
2. **Constrained communication under partial observability.** Vary the radio vocabulary size and measure the effect on team performance. The most novel element of the design, and arrived at for aesthetic rather than derivative reasons.
3. **Effect of a neutral hostile population on competitive equilibria.** Run matches with and without raptors; measure how strategy distributions shift.
4. **Does objective-gated sustain self-balance?** The no-respawn + heal-only-on-delivery rule predicts that pure-combat strategies decay. Falsifiable.

**All four are in scope.** This is affordable because they share infrastructure: once headless batch mode and log-derived statistics exist, Q3 is a config flag plus a batch run, and Q4 is pure analysis of logs already being produced. Only Q1 needs bespoke analysis (clustering) and only Q2 needs new gameplay machinery (radio vocabulary as a tunable variable).

**Q1 is primary**, which means it wins when the questions conflict — and they do conflict, in a specific dependency chain:

- **Q2 contaminates Q1.** Varying radio vocabulary changes agent behavior, so running it concurrently makes communication a confound in the personality clustering.
- **Q1 is load-bearing for Q3 and Q4.** Without distinct personalities, "strategy distributions" and "does combat decay" are measuring a population of identical clones.

**Order: Q1 → Q3 → Q4 → Q2.** Q1 first because it is the next milestone anyway, it validates the project's central architectural claim (personality *is* the weight vector), and it produces the statistics infrastructure the rest consume. Q2 last because it requires the radio loop closed (§7.2) and it perturbs everything else.

This also settles a §6.5 question in Q1's favour: **aim stays perfect, all body damage.** An aim-skill parameter would introduce combat RNG and become a confound when attributing behavioral differences to decision weights. Behavioral variation must come from decisions, not from luck.

### 14.3 Training ladder [OPEN]

**Rung 1 — evolutionary weight search. Recommended starting point.**
The personality vector is ~10 floats. Tiny search space. Run a population of weight vectors against each other headless, keep winners, mutate, repeat (CMA-ES or a plain GA). No neural networks, no GPU, runs overnight on a laptop. Produces genuinely good agents plus a fitness landscape showing which traits win. Directly answers research question 1.

**Rung 2 — behavior cloning.**
Record good matches, train a small network to predict action from observation. Easy to train, but capped at imitating the teacher.

**Rung 3 — reinforcement learning** (Unity ML-Agents, PPO, self-play).
Verify current ML-Agents version against Unity docs before committing.

Be aware this environment is hard for RL on four axes simultaneously:
- **Sparse rewards** — a delivered egg is 20+ seconds of correct decisions with one payoff.
- **Partial observability** — FOV plus radio makes this a POMDP, requiring recurrence or frame stacking.
- **Multi-agent non-stationarity** — opponents change as they learn.
- **Long horizons.**

Any one is manageable. All four together is a paper, not a weekend. Heavy reward shaping would be required.

### 14.4 The degenerate strategy trap [TENSION]

Trained agents optimize for **winning**, not for **being interesting to watch**, and these diverge quickly. RL agents in objective games reliably discover degenerate equilibria — for instance, all five agents camping the enemy base entrance for a 0–0 timer expiry. Technically optimal, unwatchable.

Mitigations: use eggs delivered rather than win rate as fitness; add a behavioral diversity term; penalize passivity.

### 14.5 The game does not need trained AI to work

Hand-tuned utility weights will produce agents that are interesting to watch. Training is an enhancement. If the concept only works after multi-agent RL is solved, the project will not ship. **Build it so it is already good, then let training make it better.**

### 14.6 Interpretability is not decoration

Readable intent, radio callouts, and visible target priority should not be discarded as indulgence now that the project is "serious." Understanding *why* an agent acted is a legitimate research concern. The instinct to make minds legible is the strongest idea in the project.

### 14.7 The LLM question, reopened and closed [RESOLVED — out]

LLMs were originally excluded because a real-time twitch shooter cannot wait ~800ms for token generation. That constraint became void once there was no human player, so the decision was deliberately re-made rather than inherited.

**Decision: LLMs are out of this project entirely.**

- **As gameplay (strategic commanders):** rejected. It breaks determinism — a stated non-negotiable — which in turn corrupts evolution fitness scores into noise and makes bugs irreproducible. §14.5 already establishes that hand-tuned weights produce agents worth watching, so the upside does not justify surrendering the project's most valuable technical property.
- **As commentary:** rejected as unnecessary. The rule-based `MatchNarrator` already exists, produces running commentary and per-team radio chatter, and is deterministic and free. An LLM would add an external dependency, cost, and non-reproducibility for narration flavour only.

The former "M12 — LLM commentary" milestone is **removed from the roadmap**. Rule-based commentary is the shipped answer.

This may be revisited as optional post-v1.0 polish if commentary text ever feels too repetitive, but it is explicitly not on the path to completion.

---

## 15. Development roadmap

### v0.1 — Prove the concept
- 1v1, one raptor, 5 eggs
- Greybox: capsules, cubes, plane, overhead camera
- Single raptor: chase and return home. **No pack behavior.**
- Utility AI with a minimal action set (grab / deliver / engage / retreat)
- Intent labels on screen
- Event logging from day one
- Headless mode and determinism from day one

**Success criterion:** is a single thief being chased while a single rival tries to intercept interesting to watch? If yes, the concept is proven and everything after is scale.

### v0.2 — Personality and stats ← **next**
- Personality weight vectors producing visibly distinct agents
- Full statistics with the raptor/enemy death split
- Roster with persistent win/loss records
- ~~2v2~~ — already running 5v5v5 ahead of schedule

This is the inflection point. Every system underneath it already reads from a personality vector; they simply all share one identical vector today. Giving each agent its own is a small change with a disproportionate behavioral payoff, and it is the prerequisite for the emergent roles in §11.4 ever appearing.

### v0.3 — Communication and packs ✅ complete
- ~~Radio callout system~~ — built
- ~~Radio as agent input~~ — built; teammates act on callouts, including stale and wrong ones
- ~~Pack raptor behavior~~ — built (§8.5)
- ~~Escort and Intercept actions~~ — built, plus `Investigate` (§11.3)
- ~~Rule-based commentary~~ — built
- ~~5v5~~ — built
- Tall grass concealment (§8.6) — deferred to the art pass, where it belongs

### v1.0 — Research ← **the finish line [LOCKED]**
- ~~Headless batch mode + cross-run determinism verification~~ — built ([BATCH.md](BATCH.md))
- ~~Statistics derived from the event log~~ — built
- ~~Evolutionary weight search~~ — built, **in C# rather than Python** (see [DECISIONS.md](DECISIONS.md) D-16)
- ~~Procedural arena generation~~ — built (§13.5), pulled forward from M13
- Replay from event log — remaining
- **All four research questions in §14.2, Q1 primary**, run in order Q1 → Q3 → Q4 → Q2 — remaining

**v1.0 is where this project concludes.** That is a deliberate commitment against the §14.1 failure mode: the definition of done is "the research questions are answered," not "there is nothing left to add." There will always be something left to add.

### In scope, but on its own track
- **Art: buildings, grass, humanoid assets, lighting.** Formerly deferred indefinitely; now an active parallel workstream at the director's decision. It plugs in at the render seam only (§12.1) and must never alter the collision/NavMesh/LOS proxies, so it cannot affect determinism or match outcomes.

### Deferred indefinitely
- Verticality
- Weapons beyond the rifle
- Human playability
- ~~LLM anything~~ — now explicitly out (§14.7)
- Data-driven arena generation (§13 / M13) — deliberately last, and likely post-v1.0

---

## 16. Priority open questions

Ordered by how much they block progress.

**Everything blocking is now a tuning question answerable with data, not a design question.** The batch runner ([BATCH.md](BATCH.md)) is the instrument for all of them.

1. **Raptor speed relative to carriers.** The most consequential single number in the game. (§8.7)
2. **Is a full raptor clear meant to be achievable?** (§8.7)
3. **Nest-to-base distance.** The primary map tuning dial, now also entangled with art dressing. (§9.3)
4. **Delivery zone geometry** — the base camping mitigation. (§5.2)
5. **Match timer length.** (§2.3)
6. **Tall grass concealment** — cosmetic, or wired into line-of-sight as §8.6 specifies? Decide alongside the art pass.
7. **Ties** — secondary tiebreak, or a genuine draw? (§2.4)
8. **Egg handling** — multi-carry, voluntary drop/handoff, pickup channel. (§4.5)

**Resolved:**

- ~~Which research question is primary?~~ → **all four, Q1 primary**, order Q1 → Q3 → Q4 → Q2 (§14.2).
- ~~Where does the project end?~~ → **v1.0, research complete** (§15).
- ~~Team coordination architecture.~~ → **the radio *is* the mechanism**; no blackboard (§11.5).
- ~~Close the radio loop.~~ → **built**; callouts now reach teammates (§7.2).
- ~~LLM commanders — in or out.~~ → **out entirely**, gameplay and commentary both (§14.7).
- ~~Aim model.~~ → **perfect aim, all body damage**; an aim stat would confound Q1 (§6.5).
- ~~Empty-magazine dead state.~~ → infinite low-damage sidearm (§6.3).
- ~~Raptor lethality — silent movement plus 30 damage.~~ → kept both; the screech is the tell (§7.4).
- ~~Utility scoring functions.~~ → designed, implemented, and *measured* (§11.3).
- ~~Raptor respawn.~~ → no respawn; the nest is clearable (§8.2).
- ~~Pack behaviour.~~ → **built**, without needing a blackboard (§8.5).
- ~~Arena generation.~~ → **built**, conservative and always symmetric (§13.5).
- ~~Art deferral.~~ → now an active parallel workstream (§15).

**Resolved:**

- ~~Which research question is primary?~~ → **all four, Q1 (personalities) primary**, order Q1 → Q3 → Q4 → Q2 (§14.2).
- ~~Where does the project end?~~ → **v1.0, research complete** (§15).
- ~~Team coordination architecture.~~ → **the radio *is* the coordination mechanism**; no separate blackboard (§11.5).
- ~~LLM commanders — in or out.~~ → **out entirely**, gameplay and commentary both (§14.7).
- ~~Aim model — perfect aim or an aim-skill parameter?~~ → **perfect aim, all body damage**; an aim stat would confound Q1 (§6.5, §14.2).
- ~~Reinstate the knife, or accept the empty-magazine dead state?~~ → infinite low-damage sidearm (§6.3).
- ~~Raptor lethality — silent movement plus 30 damage.~~ → kept both; the screech is the audible tell (§7.4).
- ~~Utility scoring functions — never designed.~~ → designed and implemented (§11.3); values remain provisional.
- ~~Raptor respawn.~~ → no respawn; the nest is clearable (§8.2).
- ~~Art — deferred until the AI is proven?~~ → **now an active parallel workstream** at the director's decision (§15).

---

## 17. Design principles to preserve

Rules of thumb that emerged during design and should survive future changes:

1. **Legibility over intelligence.** A readable agent beats a smart one.
2. **Kills are never worth points.** The moment they are, this becomes a deathmatch.
3. **Delivery is the only healing.** Adding any other heal source destroys the self-balancing economy.
4. **Three bodies, not two.** The raptors must remain hostile to everyone.
5. **Reward the completed action, not the possession.**
6. **The pattern is fixed; the instance is random.** This is what makes raptors feel intelligent forever.
7. **The observer knows more than the agents.** Protect this gap — it is the source of the best moments.
8. **Symmetric map.** Ambiguous results are worse than boring ones.
9. **Every number is provisional** until observed in a running build.
