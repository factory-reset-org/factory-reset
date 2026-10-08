# Tracker Toy

Owner: S1 · Architecture: Greedy Best-First Search + hierarchical FSM

Source of truth: the externally supplied Tracker Toy specification from `ToyFactoryRevolt_FullPlan_v4.md`. The Full Plan is not checked into this repository. Behaviour, tests and schedule entries below are design requirements, not claims of completed implementation or measured results.

## Role in gameplay

The Tracker is a wind-up scout that hunts by ear. It uses Greedy Best-First Search (GBFS) to move toward sounds and pursue the player, with behaviour organised by the shared hierarchical, data-driven FSM architecture.

Its eager movement toward a target is intentional: it does not need the shortest route to feel like a scout sniffing toward a sound. The player can distract it with noise or kite it until its wind-up energy runs out, then attack during Rewind.

## Current implementation status

The current repository contains the shared `GridGraph`, `BinaryHeap`, `BaseCostModel`, `AStarSearch`, `IPathfinder` and `PathResult`, plus the brain/body contracts `IAgentBrain`, `AgentContext` and `AgentIntent`. The grid provides 0.5 m cells, eight-connected movement without diagonal corner cutting, explicit door identity/state, and nearest-traversable-cell lookup. `BaseCostModel.OctileDistance` supplies the shared heuristic calculation.

Implemented: wind-down energy (`WindUpEnergy`), the Tracker's search (`GreedyBestFirstSearch`, measured against A* below), and the brain itself (`TrackerBrain`): all eight states in a hierarchical FSM built on the new shared `CompositeState`, grid-traced vision, a `NoiseMemory` that scores what it hears, waiting at closed doors, and the "?"/"!" alert level on every intent. `AgentSpawner` now builds a `TrackerBrain` for the Tracker spawn point whenever a grid exists.

Noise propagation (`NoisePropagation`, task G) is implemented and measured on the level grid. S4's runtime hook, `AgentHearing` (#162), listens to `NoiseEvents`, runs `Propagate` once per noise and fills each agent's `SensorSnapshot`. In play, the Tracker therefore hears, investigates and is distracted, as well as patrolling, seeing and chasing.

## Creative hook

### Sound propagation

The Tracker reads the propagated sound level `L(cell)` at its own cell. Hearing is a Dijkstra propagation through the grid rather than a Physics sphere or straight-line distance test: sound travels around corners and through closed doors, losing level with path distance and door crossings, and walls and boxes stop it.

The source levels, attenuation formula, hearing threshold and noise-selection rule are under Maths to defend. Task noises are part of the hook: the Chapter 2 terminal beeps every 0.8 s (a repeating source that pulls the Tracker into Distracted), the relay alarm and core explosions are loud enough to be heard two rooms away, and the pressure plate click gives the player away nearby.

### Wind-down energy

Implemented in `AI/Agents/Tracker/WindUpEnergy.cs` (pure C#, game time only).

| Value | Number | Why |
| --- | --- | --- |
| Full spring | 100 | A round capacity; `Energy01 = Energy / 100` drives the key-spin animation. |
| Drain while chasing | 10/s | A full spring lasts **10 s** of pursuit: long enough to be a threat, short enough that a player who kites it sees it wind down within one room. |
| Drain otherwise | 2/s | A full spring lasts **50 s** of patrol or investigation, so it rarely winds down while calm and the hook stays tied to chasing. |
| Rewind | 3 s, linear 0 to 100 | The vulnerable window: the toy stands still and takes double hits, long enough for the player to turn and land a few shots. |

At zero energy the spring clamps to 0 (never negative) and `IsRewinding` becomes true. While rewinding there is no drain, whatever the state, and energy refills linearly. Rewind progress is stored as elapsed time rather than summed energy, so the spring is full after exactly 3 s of game time; at full it clamps to 100 and `IsRewinding` becomes false. Leftover time in the tick that completes the rewind is not drained.

This gives the player a counter-play loop: keep the Tracker chasing until it winds down, then attack during Rewind.

**Time rules.** `Tick(ctx.Time, chasing)` measures the gap since the previous tick. The first tick after construction only records the time, and a tick whose time did not move forward changes nothing. Cutscenes and pause need nothing special because game time stops. A stun does need handling: the runtime does not tick the brain while the body is fallen apart, but game time keeps running, so the first tick after the reboot would see a 7 s gap and drain 14 (70 if it had been chasing). The brain calls `Resume(ctx.Time)` on that tick to restart the clock without changing energy.

**Tests** (`Tests/EditMode/WindUpEnergyTests.cs`): `StartsFullAndNotRewinding`, `FirstTickOnlyRecordsTheTime`, `FiveSecondsDrainsByState` (chasing 100 to 50, otherwise 100 to 90), `DrainingToZeroStartsRewindingAndNeverGoesNegative`, `RewindIgnoresChasingAndIsFullAfterExactlyThreeSeconds`, `Energy01StaysBetweenZeroAndOne`, `ResumeAfterStunGapDoesNotDrain`, `TimeGoingBackwardsDoesNotChangeEnergy`, `RepeatedTicksAllocateZeroBytes`.

## Architecture

### Shared FSM and AI contracts

States are objects with `Enter`, `Tick` and `Exit`. Transitions are data: a `Transition<T>` holds a **from state** (null for "any state"), a **target state**, a **condition delegate** and a numeric **priority**; `StateMachine<T>` checks them highest priority first and takes the first that holds. It is not a switch-statement FSM.

`TrackerBrain` follows the `IAgentBrain` contract: it reads `AgentContext`, returns an `AgentIntent` for Runtime to execute, and handles `OnGraphChanged` and `OnStunned`. It is pure C# with no Physics or NavMesh calls, as required by [CONTRIBUTING.md](../../CONTRIBUTING.md); every timer uses `ctx.Time` (game time), so pause and cutscenes freeze it.

### Hierarchy

`CompositeState<T>` (`AI/Core/FSM/CompositeState.cs`, shared) is a state that owns a child `StateMachine<T>`. While the parent is current, each tick runs the child machine, so child transitions only compete with each other, and any parent transition beats every child. Re-entering a parent restarts it at its initial child; leaving it exits the running child first.

```text
Top machine
├── Calm            (composite, starts in Patrol)
│   ├── Patrol
│   ├── Investigate
│   └── Distracted
├── Hunting         (composite, starts in Chase)
│   ├── Chase
│   ├── WaitAtDoor
│   └── Search
├── Rewind          (two instances: from Calm, from Hunting, so each knows where to return)
└── Stunned         (pass-through on the first tick after the reboot)
```

The hierarchy is what keeps the table short: "sees the player" is one Calm → Hunting row instead of one per calm state, and Rewind and Stunned interrupt whichever child is running.

### States

LKP means **last known position** of the player. Speeds are in m/s.

| State | Behaviour | Speed |
| --- | --- | ---: |
| **Patrol** | GBFS to each patrol point in turn; an unreachable point is skipped. | 1.9 |
| **Investigate** | GBFS to the best one-off noise; on arrival stand and sweep the head round for **2.4 s**, then mark the noise handled. A better noise heard on the way retargets it. A noise heard through a closed door is checked from the near side of that door. | 3.2 |
| **Distracted** | Circle a repeating source at **1.5 m**, stepping to the next of 8 points every **1.2 s**, looking at it. On leaving, the source is marked handled so it is not then investigated. | 3.6 |
| **Chase** | GBFS to the player (or LKP if not currently seen), replanning every **0.5 s**; looks at the target. If a closed door is in the way, it runs to the near side of the door. | 4.6 |
| **WaitAtDoor** | The player escaped through a door and shut it. Runs to the near side of the door, then stands and stares at it for **2.5 s**: the toy has no hands to open it. | 4.6 |
| **Search** | GBFS round three rings about LKP (1.5, 3 and 4.5 m, 8 points each), starting in the direction the player was moving. After WaitAtDoor the rings are centred on the door's near side instead, the side it can reach. Gives up after **8 s**. | 3.3 |
| **Rewind** | Empty path (stop), `Action = Rewind` for **3 s**; `DamageMultiplier` is 2. | 0 |
| **Stunned** | The controller holds the body for the whole stun and does not tick the brain. On the first tick after the reboot this state clears the stun and hands over on the same tick. | 0 |

### Transition table

`DescribeTransitions()` prints this table from the live transition objects, for the debug overlay and the viva.

| Machine | Priority | From → To | When |
| --- | ---: | --- | --- |
| Top | 100 | any → Stunned | a stun is pending (first tick after the reboot) |
| Top | 92 | Stunned → Rewind (from Hunting) | still rewinding, was hunting |
| Top | 91 | Stunned → Rewind (from Calm) | still rewinding |
| Top | 90 | Stunned → Hunting | was hunting, LKP known, player alive |
| Top | 89 | Stunned → Calm | otherwise |
| Top | 80 | Calm → Rewind, Hunting → Rewind | energy reached 0 |
| Top | 70 | Rewind → Calm / Hunting | energy full again (back to the parent it left) |
| Top | 50 | Calm → Hunting | sees the player |
| Top | 40 | Hunting → Calm | Search timed out, or the player is gone or dead |
| Calm | 30 | any → Distracted | the best noise is a repeating source, and not already Distracted |
| Calm | 25 | Distracted → Patrol | the source has been silent for 1.5 s |
| Calm | 20 | Patrol → Investigate | an unhandled one-off noise is remembered |
| Calm | 15 | Investigate → Patrol | arrived and looked around for 2.4 s |
| Hunting | 30 | Search → Chase | sees the player |
| Hunting | 30 | WaitAtDoor → Chase | sees the player |
| Hunting | 28 | WaitAtDoor → Chase | the door opened |
| Hunting | 25 | Chase → WaitAtDoor | a closed door stands between it and the player |
| Hunting | 22 | WaitAtDoor → Search | waited 2.5 s at the shut door |
| Hunting | 20 | Chase → Search | not seen for 0.7 s |

Why these priorities: a stun outranks everything because the body has physically fallen apart; Rewind outranks sight because a spent spring cannot chase (that is the counter-play window); inside Calm a repeating lure outranks a one-off noise because that is the point of throwing a toy. Rewind does not look for the player, so a Tracker that is winding up stays vulnerable for the full 3 s. Inside Hunting, a shut door outranks losing sight. Once the door shuts, the player is out of sight within 0.7 s anyway, so without that priority the Tracker would search rings it cannot reach, all on the far side of the door.

**Stun rules.** `OnStunned` sets a pending flag, records whether the Tracker was hunting, and drops the route; LKP is kept. On the first tick after the reboot the brain calls `WindUpEnergy.Resume` (so the stun costs no energy), the Stunned row fires, and the pass-through picks Rewind, Hunting or Calm and plans a fresh route on that same tick.

**Senses.** Vision is S2's `VisionQuery` cone (12 m, 60° half-angle), widened to all round within 2.5 m, with line of sight traced on the grid by `GridLineCheck`. The trace stops 0.8 m short of the player: cells within the 0.55 m agent clearance of a wall are unwalkable, so a player standing against a wall would otherwise always count as hidden. Hearing goes through `NoiseMemory` (see Maths).

**Closed doors.** The toy cannot open doors, and a closed door is not a legal cell in the grid's movement rules, so the Tracker never plans through one. When the normal GBFS finds no route, Chase and Investigate run a second GBFS with `throughClosedDoors: true`. Closed doors count as open in that search, while walls and boxes still block. If that route exists, its first closed door is the one in the way, and the cells before it are a real, walkable route to the door's near side. The brain walks those cells and remembers the door cell. Patrol and Search do not do this: an unreachable patrol point or ring point is simply skipped, as before.

**Alert icon.** Each state sets `AgentIntent.Alert` (S4's #178):
- `Alert` ("!") in Chase and WaitAtDoor
- `Suspicious` ("?") in Investigate, Distracted and Search
- `None` in Patrol, Rewind and Stunned

The icon no longer depends on the state names.

**Graph changes.** `OnGraphChanged` replans only when a changed cell lies on the current route, or is the closed door the route stops at (it may have opened). Other changes cost nothing. While waiting, WaitAtDoor also checks the door every tick and goes back to Chase as soon as it opens.

### Debug overlay (F3)

`Runtime/Debug/TrackerOverlayLayer.cs` is a layer on S4's debug overlay. It registers itself on scene load, so S4's overlay needs no edits. S4's base layer already labels the Tracker's leaf state and "?"/"!" and draws the route its body is walking. The Tracker layer adds:

| Drawn | Shows |
| --- | --- |
| Label under its feet | Top state (Calm, Hunting, Rewind, Stunned) and spring energy, with "rewinding, hits x2" during Rewind |
| Orange cell path | The GBFS route the brain planned (`RouteCells`) |
| Noise cells with labels | Every remembered noise and its decayed score. The one it would follow is linked to the Tracker; repeating sources are pink, handled ones grey |
| Red cell "last seen" | The player's last known position while hunting |
| Red double cell "shut door" | The door it is waiting at (`BlockingDoorCell`) |
| Three yellow circles | The Search rings (1.5, 3 and 4.5 m) round `SearchCentre` |
| Heatmap dots | How the latest noise in the level spread: cold (just audible) to hot (at the source), for 4 s |

The heatmap is the hearing model made visible. The layer listens to `NoiseEvents` and runs its own `NoisePropagation` once per new noise, or again if the grid changes; it does not run every frame. It draws every other cell (1 m apart), so walls, corners and the −35 at a closed door show as gaps and colour steps.

Reading the brain never changes it: `NoiseMemory.CopyTo` lists noises without forgetting decayed ones, which the brain's own queries do. Tests: `TrackerOverlayLayerTests` (PlayMode, 3), `CopyToListsEveryRememberedSourceWithItsFlags`, `CopyToLeavesOutDecayedNoisesAndChangesNothing` and `TheOverlayCanReadTheRouteNoisesLastSightingAndSearchCentre` (EditMode).

## Why this architecture over the alternatives

GBFS fits the Tracker's eager "sniffing toward a sound" behaviour. It is expected to expand fewer nodes than A* in open rooms, but it can take longer or slightly wandering routes around obstacles. That trade-off is acceptable for this scout and can make its pursuit less predictable. Fewer expansions are an expectation to test, not a measured result or a guarantee for every map.

| Alternative | Why not |
| --- | --- |
| A* for all Tracker routes | A* seeks an optimal path under its cost model. The Tracker deliberately accepts non-optimal routes in exchange for heuristic-only greedy selection; A* remains the comparison baseline. |
| Straight-line/radius-based hearing | It would omit the planned path-distance attenuation around corners and the separate penalty for each closed door crossed. |

## Maths to defend

### GBFS and the octile heuristic

GBFS selects its next frontier node using only its heuristic:

```text
f(n) = h(n)

dx = abs(n.x - goal.x)
dy = abs(n.y - goal.y)
h(n) = D * (dx + dy) + (D2 - 2 * D) * min(dx, dy)

D  = 1
D2 = sqrt(2)
```

This is octile distance on the eight-connected grid. Cell Y is the grid coordinate corresponding to world Z. Costs are in grid units: a cardinal step costs 1 and a diagonal step costs sqrt(2). Multiply a base grid path length by the **0.5 m** cell size to obtain metres. Reuse `BaseCostModel.OctileDistance`, `StraightCost` and `DiagonalCost` rather than maintaining another formula.

GBFS does not include accumulated cost `g(n)` in its selection priority. It must maintain a **closed set** so an expanded node is not expanded again. With that closed set and exploration of the available frontier, GBFS is **complete on the project's finite graph**: it finds a path if one exists, or exhausts the frontier and returns `Found = false`. It is **not optimal**; the octile heuristic does not turn heuristic-only selection into shortest-path search.

**Implementation** (`AI/Agents/Tracker/GreedyBestFirstSearch.cs`, an `IPathfinder`):
- The open list is the shared `BinaryHeap`, keyed by `h(n)` only (`BaseCostModel.OctileDistance`).
- **Closed set:** a cell is stamped when it is first pushed and never pushed again. Because `h(n)` of a cell never changes, its first priority is final, so there is nothing for `DecreaseKey` to improve (unlike A*, where a cheaper `g` can lower a cell's priority). Every cell is therefore expanded at most once, which bounds the search by the number of cells: this is the completeness argument.
- Neighbours come from `GridGraph.GetNeighboursNonAlloc`, so the no-corner-cutting rule and closed doors apply exactly as for A*.
- The `ICostModel` argument is required by `IPathfinder` but not used for ordering; ignoring `g(n)` is what makes the search greedy.
- Arrays are reused between searches with a stamp (the same trick as `AStarSearch` and `DijkstraField`), so a search allocates nothing but the returned path list. That list is sized exactly: a found path costs 2 allocations, a failed search none. Wrapped in the `AI.Tracker.GBFS` ProfilerMarker.

### Propagated sound level

`NoisePropagation` (`AI/Core/Perception`, pure C#) computes:

```text
L(cell) = L0 - alpha * pathDistance(source, cell)
             - sum(doorPenalty for closed doors crossed)

alpha            = 4 per metre
closedDoorPenalty = 35
hearingThreshold  = 10
```

`pathDistance` is distance along the grid route in **metres**, not straight-line distance. Stop expanding when **`L(cell) <= hearingThreshold`**. Tracker hears an eligible noise by reading its propagated value at Tracker's own cell, above the threshold.

The loudness table is in `Interfaces/NoiseLoudness.cs` so every emitter uses the same numbers. Audible radius in the open is `(L0 - 10) / 4` metres.

| Noise source (`NoiseLoudness`) | L0 | Heard up to |
| --- | ---: | ---: |
| Blaster shot | 100 | 22.5 m |
| Relay alarm (Ch3) | 90 | 20 m |
| Power core explosion (Ch4) | 90 | 20 m |
| Thrown wind-up toy landing (every 1 s for 5 s) | 70 | 15 m |
| Door slam | 60 | 12.5 m |
| Terminal beep (Ch2, every 0.8 s) | 60 | 12.5 m |
| Box pushed / impact | 50 | 10 m |
| Pressure plate click (Ch1) | 40 | 7.5 m |
| Running footsteps | 25 | 3.75 m |

**Implementation.** A bounded Dijkstra over the *sound* graph (`GetNeighboursNonAlloc(..., allowClosedDoors: true)`, so closed doors connect but walls and box blockers do not). The path cost is the level lost: 2 per orthogonal 0.5 m step, 2.83 per diagonal, plus 35 when a step enters a closed door from outside it. Charging on entry means a door two cells deep still costs 35 once, and a door slam made *on* the door is heard on both sides without the penalty. Dijkstra pops cells in increasing loss, so each cell gets the loudest level any route can bring, which is the shortest sound path. A cell whose level would be at or below 10 is never queued, so the work is bounded by the audible area. A source inside a wall or box spreads from the nearest open cell within 2 cells. Arrays are reused with a stamp (as in `DijkstraField`): no allocation per noise. `Level(cell)` returns 0 where the noise is not heard. Profiler marker `AI.NoisePropagation.Propagate`.

**Hearing contract for S4.** For each `NoiseEvents.OnNoise`: `Propagate(grid.WorldToCell(e.Position), e.Loudness)`, then for each agent with `Level(agentCell) > 10`, merge `new SensorSnapshot(e.Position, level, e.SourceId, e.Time)` into its pending snapshot with `SensorSnapshot.Loudest`, so the loudest noise since the last tick wins.

**On the level** (done-when check): a blaster shot 2 m in front of closed door 4 is heard at **47** 2.5 m behind it, which is `100 - 4 x 4.5 - 35` along the 4.5 m grid path. With the door open it is 82, exactly 35 more. Timings are in [AIPerformanceLog.md](../AIPerformanceLog.md).

### Selecting among heard noises

```text
score = L(cell) * exp(-lambda * age)
lambda = 0.3/s
```

`L(cell)` is the propagated level at Tracker's cell and `age` is in seconds. The highest score selects the preferred noise, favouring recent loud noises over older ones. Equal scores prefer the noise with the **smaller path distance**.

For an otherwise equivalent noise aged 5 seconds, the multiplier is `exp(-0.3 * 5) ≈ 0.223`, about **22%** of its fresh score. This is a consequence of the formula, not an experimental measurement.

**Implementation** (`AI/Agents/Tracker/NoiseMemory.cs`): one entry per source, so the hack terminal's beep every 0.8 s refreshes a single entry instead of piling up. An entry is forgotten once its score falls to the hearing threshold (10), e.g. a level-20 noise after 2.3 s. A source heard again within **1.5 s** of its previous noise is *repeating* (what sends the Tracker to Distracted) until it has been silent for 1.5 s. Investigated sources are marked handled and ignored until they make a new noise. Fixed capacity of 8 and no allocation after construction. Example: a 30-level beep 0.2 s old (score 28.3) beats an 80-level shot 5 s old (score 17.9).

**Deviation:** the plan breaks ties by **path** distance; `NoiseMemory` uses flat (straight-line) distance. The hearing hook gives the brain each noise's level at its cell but not the path distance to the source, so breaking ties by path would mean running one search per tied noise every tick. Exact ties are rare (same level, same age), so the cheaper rule was kept.

## Edge cases

| Case | Handling now | Status |
| --- | --- | --- |
| Noise source is in an unreachable/blocked cell | Every goal is snapped to the nearest traversable cell within 6 cells (3 m). If no route exists, Investigate looks around from where it stands, then marks the noise handled. | Implemented; snapping covered indirectly by the Investigate tests |
| LKP becomes blocked by a pushed box | Chase and Search goals are snapped the same way; Search skips ring points it cannot reach (at most 3 searches per tick). | `ABoxPushedOntoTheLastSightingIsSearchedRoundNotWalkedInto` |
| A closed door makes the goal unreachable | Runs to the near side of the door (found by a GBFS through closed doors), waits there 2.5 s staring at it, then searches its own side. If the door opens it chases again; a noise heard through the door is investigated from the door. A wall with no door still just stops it. | `AClosedDoorSendsTheChaseToTheNearSideOfTheDoor`, `ItWaitsAtTheShutDoorThenSearchesItsOwnSide`, `TheDoorOpeningResumesTheChase`, `ANoiseThroughAClosedDoorIsCheckedFromTheDoor`, `ANoiseBehindAWallWithNoDoorIsLookedForFromWhereItStands` |
| Two noises have equal scores | Prefer the closer one (flat distance, see the deviation above). | `EqualScoresPreferTheCloserNoise` |
| Tracker is stunned during Chase | LKP is kept; the first tick after the reboot resumes the hunt towards it. | `AStunWhileHuntingResumesTheHunt` |

`GridGraph.TryFindNearestTraversable` is an existing spatial helper, not a reachability test. A nearest traversable cell can still be in a disconnected region. Likewise, a closed door's cell is not a legal movement destination. The Tracker therefore waits on the approach cell, the last cell before the door on the through-door route, and never enters the door cell. The plan says "wait/bump": the Tracker waits but does not bump, because a bump would need a new body action and animation from S4 for little gameplay gain.

## Tests

### Required GBFS and sound tests — planned

| Required test/evidence | What it must establish | Status |
| --- | --- | --- |
| GBFS finds a path when one exists | A reachable start/goal pair returns `Found = true` with a legal grid route. | Passing: `FindsAStraightPathOnAnOpenGrid`, `RoutesAroundAWallWithoutCuttingCorners`, `FindsAPathWheneverAStarDoesOnFiftyRandomGrids` |
| Enclosed/unreachable region | GBFS returns `Found = false` when the goal cannot be reached. | Passing: `WalledOffGoalIsNotFound`, `ClosedDoorBlocksMovement`, `BlockedStartOrGoalIsNotFoundWithoutExpanding` |
| Through closed doors | With `throughClosedDoors`, a closed door counts as open but walls still block. | Passing: `ThroughClosedDoorsTheRouteCrossesTheDoor`, `ThroughClosedDoorsWallsStillBlock`, `ThroughClosedDoorsAGoalOnTheDoorIsAllowed` |
| Closed-set behaviour | GBFS never expands the same node twice. | Passing: `ExpandsEachReachableCellExactlyOnceWhenTheGoalIsUnreachable` (expanded count equals the reachable region's size exactly) |
| Fewer expansions than A*; no allocation | GBFS expands fewer nodes than A* on open grids; a reused failed search allocates nothing, a found one only its path (2 allocations). Counted with Unity's GC.Alloc recorder. | Passing: `ExpandsFewerNodesThanAStarOnOpenGrids`, `ReusedSearchAllocatesNothing`, `AFoundPathCostsOnlyItsListAndArray` |
| Closed-door attenuation | Propagated noise through a closed door matches `L0 - 4 * pathDistance - sum(35 per closed door crossed)`. | Passing: `AClosedDoorCostsThirtyFive`, `ADoorSeveralCellsDeepCostsThirtyFiveOnce`, `EachClosedDoorCrossedCostsThirtyFive`; checked on the level at door 4 |
| Tracker edge cases | The five cases above follow the planned handling. | Done: see Edge cases |
| GBFS versus A* | Record nodes expanded and path length for both algorithms over the same 20 start/goal pairs. | Done: see Measured results |

### Brain tests (EditMode, all passing)

`Tests/EditMode/TrackerBrainTests.cs` (31), on a 20 m x 10 m open grid driven tick by tick with game time:
- **Patrol:** `FirstTickPatrolsWithAGbfsRoute`, `PatrolKeepsItsRouteUntilArrivalThenGoesToTheNextPoint`.
- **Hearing:** `ALoudNoiseStartsAnInvestigation`, `InvestigationLooksAroundThenReturnsToPatrolAndDoesNotRepeat`, `ARepeatingSourceDistractsUntilItGoesQuiet`.
- **Vision and hunting:** `SeeingThePlayerStartsTheChase`, `APlayerBehindAWallIsNotSeen`, `APlayerBehindTheTrackerIsSeenOnlyUpClose`, `ChaseReplansEveryHalfSecond`, `LosingSightSwitchesToSearchAfterPointSevenSeconds`, `SearchGivesUpAfterEightSeconds`, `SeeingThePlayerAgainDuringSearchResumesTheChase`, `AMissingOrDeadPlayerEndsTheHunt`.
- **Energy:** `RunningOutOfEnergyRewindsInPlaceThenCarriesOn` (empty path, `Action = Rewind`, double damage, back after 3 s), `ChasingDrainsEnergyFiveTimesFaster`, `ARewindFromTheHuntReturnsToTheHunt`.
- **Stuns and priority:** `AStunDoesNotDrainEnergyAndAFreshRouteGoesOutOnTheFirstTickAfter`, `AStunWhileHuntingResumesTheHunt`, `AStunDuringARewindGoesBackToRewinding`, `TheStunInterruptHasTheHighestPriority` (every machine's rows are in descending priority and the stun is the top machine's first row).
- **Graph changes:** `AGraphChangeOffTheRouteKeepsIt`, `AGraphChangeOnTheRouteReplansAroundIt`, `ABoxPushedOntoTheLastSightingIsSearchedRoundNotWalkedInto`.
- **Closed doors** (a wall with a three-cell door that shuts mid-chase): `AClosedDoorSendsTheChaseToTheNearSideOfTheDoor`, `ItWaitsAtTheShutDoorThenSearchesItsOwnSide`, `TheDoorOpeningResumesTheChase`, `ANoiseThroughAClosedDoorIsCheckedFromTheDoor`, `ANoiseBehindAWallWithNoDoorIsLookedForFromWhereItStands`.
- **Alert icon:** `TheAlertLevelFollowsTheState` (None in Patrol, Suspicious in Investigate and Search, Alert in Chase).
- `ConstructorRejectsMissingInputs`.

`Tests/EditMode/NoiseMemoryTests.cs` (11): decay formula, a repeating beep beating an older louder shot, the 1.5 s repeat window, the closer-noise tie-break, handled noises, forgetting at the threshold, ignoring out-of-order noises, and replacing the weakest entry when full.

`Tests/EditMode/NoisePropagationTests.cs` (25): full level at the source; 4 per metre along the grid, not the straight line; a closed door costs 35, an open one nothing, a deep door 35 once, two doors 70; a door slam is heard on both sides; walls and boxes block; sound goes round corners with exactly the A* route's loss; fades out at the threshold (footsteps 11 at 3.5 m, gone at 4 m) and exactly-10 is not heard; only audible cells are expanded; a source in a wall snaps out; off-grid sources are heard nowhere; no allocation on reuse; and `SensorSnapshot.Loudest`.

`Tests/EditMode/CompositeStateTests.cs` (6): the child is entered and ticked inside the parent, child transitions run inside it, leaving exits the running child first, re-entry restarts at the initial child, and a child that never ran is never exited.

Full suites after the evidence branch: EditMode 730/730, PlayMode 138/138.

### Planned S1 delivery schedule

These are the Full Plan's schedule entries, not a completion checklist. See Current implementation status for what exists in this checkout.

| Week | Planned work |
| --- | --- |
| 11 | GridGraph from NavMesh; grid debug overlay; FSM framework. |
| 12 | GBFS; Tracker Patrol / Investigate / Chase; noise propagation. |
| 13 | Search and Rewind states; wind-down energy; edge cases. |
| 14 | Tracker edge-case tests; GBFS versus A* comparison table. |

## Measured results

### Brain cost

Full table in [AIPerformanceLog.md](../AIPerformanceLog.md) (Tracker brain tick). The brain was run for 60 s of scripted game time on the level grid and went through every state:

| | Per tick | Notes |
| --- | --- | --- |
| Median | 0.8 µs | |
| p99 | 18.7 µs | |
| Seven Trackers at once | 95.5 µs per frame at p99 | |
| Allocations | Only on the 1.3% of ticks that hand the body a new route, about 4 each | Every other tick allocates nothing |

### Evidence scenes

Both scenes have the F3 overlay on from the start, a runtime NavMesh, and no player.

| Scene | Set-up | What it shows | Screenshots |
| --- | --- | --- | --- |
| `Scenes/Test/Test_DoorClosedNoise.unity` | Two rooms and a closed door. A relay alarm (90) sounds 3 m past the door every 7 s. The Tracker patrols 5 m from the door. **O** toggles the door. | Closed: the heatmap is warm on the alarm's side and drops to cold past the door (the −35). The Tracker hears about 23, walks to the door, and looks around there ("?"). Open: the heatmap stays warm through the doorway, and the Tracker walks through to the alarm. | `Docs/Evidence/Tracker/door_closed_noise.png`, `door_open_noise.png` |
| `Scenes/Test/Test_TerminalNoise.unity` | One room with two cover blocks. A colour-terminal stand-in beeps (60) every 0.8 s, 8 times (the 6.4 s hold), then is quiet for 10 s. | The Tracker turns Distracted ("?") and circles the terminal while it beeps, then goes back to Patrol 1.5 s after the last beep. The heatmap has gaps behind the cover. | `Docs/Evidence/Tracker/terminal_distracted.png` (taken with the terminal held on) |

The noise sources are `ScriptedNoiseSource` and the door is `DebugDoorToggle` (`Runtime/Debug`), stand-ins until S2's terminal and relays exist. The screenshots are camera renders, so the overlay's on-screen labels (OnGUI) are not in them; in play they show above each cell.


### GBFS vs A* on the level grid

Same 20 start/goal pairs on the greybox level grid (83 x 83 cells, 5,318 walkable), `BaseCostModel`, editor timing. Full per-pair table in [AIPerformanceLog.md](../AIPerformanceLog.md).

| Total over 20 pairs | A* | GBFS | Difference |
| --- | --- | --- | --- |
| Nodes expanded | 5,426 | 1,270 | 77% fewer |
| Search time | 12.27 ms | 2.62 ms | 4.7x faster |
| Path length | 412.2 m | 468.2 m | 13.6% longer |

GBFS matched A*'s path length on 10 pairs and was longer on 10. The longest detours are routes that must pass round a central wall: greedy heads straight for the goal, meets the wall and follows it to a doorway. That is the trade the design accepts: a chaser that replans every 0.5 s needs cheap searches more than shortest routes, and the slight detours read as sniffing.

| Evidence | Status |
| --- | --- |
| Results for the same 20 GBFS/A* pairs | Done (above; lab-machine timing still to record) |
| Tracker test execution results | Done: 40 brain, noise-memory and composite-state tests passing (see Tests) |
| Tracker profiling and four-agent stress measurements | Pending / TODO |
