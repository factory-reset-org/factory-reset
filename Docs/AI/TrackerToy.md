# Tracker Toy

Owner: S1 · Architecture: Greedy Best-First Search + hierarchical FSM

Source of truth: the externally supplied Tracker Toy specification from `ToyFactoryRevolt_FullPlan_v4.md`. The Full Plan is not checked into this repository. Behaviour, tests and schedule entries below are design requirements, not claims of completed implementation or measured results.

## Role in gameplay

The Tracker is a wind-up scout that hunts by ear. It uses Greedy Best-First Search (GBFS) to move toward sounds and pursue the player, with behaviour organised by the shared hierarchical, data-driven FSM architecture.

Its eager movement toward a target is intentional: it does not need the shortest route to feel like a scout sniffing toward a sound. The player can distract it with noise or kite it until its wind-up energy runs out, then attack during Rewind.

## Current implementation status

The current repository contains the shared `GridGraph`, `BinaryHeap`, `BaseCostModel`, `AStarSearch`, `IPathfinder` and `PathResult`, plus the brain/body contracts `IAgentBrain`, `AgentContext` and `AgentIntent`. The grid provides 0.5 m cells, eight-connected movement without diagonal corner cutting, explicit door identity/state, and nearest-traversable-cell lookup. `BaseCostModel.OctileDistance` supplies the shared heuristic calculation.

Implemented: wind-down energy (`WindUpEnergy`), the Tracker's search (`GreedyBestFirstSearch`, measured against A* below), and the brain itself (`TrackerBrain`): all seven states in a hierarchical FSM built on the new shared `CompositeState`, grid-traced vision, and a `NoiseMemory` that scores what it hears. `AgentSpawner` now builds a `TrackerBrain` for the Tracker spawn point whenever a grid exists.

Not yet implemented: noise propagation (task G). The brain reads hearing from `SensorSnapshot`'s new noise fields, which S4's runtime fills once `NoisePropagation` exists; until then `default(SensorSnapshot)` means "heard nothing", so in play the Tracker patrols, sees and chases but does not investigate.

## Creative hook

### Sound propagation

Tracker will read the propagated sound level `L(cell)` at its own cell. Hearing will use a Dijkstra propagation through the grid rather than a Physics sphere or straight-line distance test. Sound can travel around corners and through closed doors, losing level with path distance and door crossings.

The five source levels, attenuation formula, hearing threshold and noise-selection rule are specified under Maths to defend. Door closure must remain distinguishable from ordinary blockers so the future noise system can apply attenuation without treating the door as acoustically impassable.

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
| **Investigate** | GBFS to the best one-off noise; on arrival stand and sweep the head round for **2.4 s**, then mark the noise handled. A better noise heard on the way retargets it. | 3.2 |
| **Distracted** | Circle a repeating source at **1.5 m**, stepping to the next of 8 points every **1.2 s**, looking at it. On leaving, the source is marked handled so it is not then investigated. | 3.6 |
| **Chase** | GBFS to the player (or LKP if not currently seen), replanning every **0.5 s**; looks at the target. | 4.6 |
| **Search** | GBFS round three rings about LKP (1.5, 3 and 4.5 m, 8 points each), starting in the direction the player was moving. Gives up after **8 s**. | 3.3 |
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
| Hunting | 20 | Chase → Search | not seen for 0.7 s |

Why these priorities: a stun outranks everything because the body has physically fallen apart; Rewind outranks sight because a spent spring cannot chase (that is the counter-play window); inside Calm a repeating lure outranks a one-off noise because that is the point of throwing a toy. Rewind does not look for the player, so a Tracker that is winding up stays vulnerable for the full 3 s.

**Stun rules.** `OnStunned` sets a pending flag, records whether the Tracker was hunting, and drops the route; LKP is kept. On the first tick after the reboot the brain calls `WindUpEnergy.Resume` (so the stun costs no energy), the Stunned row fires, and the pass-through picks Rewind, Hunting or Calm and plans a fresh route on that same tick.

**Senses.** Vision is S2's `VisionQuery` cone (12 m, 60° half-angle), widened to all round within 2.5 m, with line of sight traced on the grid by `GridLineCheck`. The trace stops 0.8 m short of the player: cells within the 0.55 m agent clearance of a wall are unwalkable, so a player standing against a wall would otherwise always count as hidden. Hearing goes through `NoiseMemory` (see Maths).

**Graph changes.** `OnGraphChanged` replans only when a changed cell lies on the current route; other changes cost nothing.

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
- Arrays are reused between searches with a stamp (the same trick as `AStarSearch` and `DijkstraField`), so a search allocates nothing but the returned path list. Wrapped in the `AI.Tracker.GBFS` ProfilerMarker.

### Propagated sound level

The planned Dijkstra noise propagation uses:

```text
L(cell) = L0 - alpha * pathDistance(source, cell)
             - sum(doorPenalty for closed doors crossed)

alpha            = 4 per metre
closedDoorPenalty = 35
hearingThreshold  = 10
```

`pathDistance` is distance along the grid route in **metres**, not straight-line distance. Stop expanding when **`L(cell) <= hearingThreshold`**. Tracker hears an eligible noise by reading its propagated value at Tracker's own cell, above the threshold.

| Noise source | Initial level L0 |
| --- | ---: |
| Blaster shot | 100 |
| Thrown wind-up toy landing | 70 |
| Box pushed / impact | 50 |
| Door slam | 60 |
| Running footsteps | 25 |

Closed doors transmit sound with the specified penalty even though they block movement. GridGraph already exposes `DoorId` and `IsDoorClosed`, and its sound-aware neighbour mode allows closed doors. Applying the 35-point penalty is planned NoisePropagation work, not an implemented GridGraph calculation.

### Selecting among heard noises

```text
score = L(cell) * exp(-lambda * age)
lambda = 0.3/s
```

`L(cell)` is the propagated level at Tracker's cell and `age` is in seconds. The highest score selects the preferred noise, favouring recent loud noises over older ones. Equal scores prefer the noise with the **smaller path distance**.

For an otherwise equivalent noise aged 5 seconds, the multiplier is `exp(-0.3 * 5) ≈ 0.223`, about **22%** of its fresh score. This is a consequence of the formula, not an experimental measurement.

**Implementation** (`AI/Agents/Tracker/NoiseMemory.cs`): one entry per source, so the hack terminal's beep every 0.8 s refreshes a single entry instead of piling up. An entry is forgotten once its score falls to the hearing threshold (10), e.g. a level-20 noise after 2.3 s. A source heard again within **1.5 s** of its previous noise is *repeating* (what sends the Tracker to Distracted) until it has been silent for 1.5 s. Investigated sources are marked handled and ignored until they make a new noise. Fixed capacity of 8 and no allocation after construction. Example: a 30-level beep 0.2 s old (score 28.3) beats an 80-level shot 5 s old (score 17.9).

**Deviation:** the plan breaks ties by **path** distance; `NoiseMemory` uses flat (straight-line) distance, because the brain does not have a path distance to each source without running a search per noise. Exact ties are rare (same level, same age), so this was kept simple; it can switch to the propagation's distance once task G stores it.

## Edge cases

| Case | Handling now | Status |
| --- | --- | --- |
| Noise source is in an unreachable/blocked cell | Every goal is snapped to the nearest traversable cell within 6 cells (3 m). If no route exists, Investigate looks around from where it stands, then marks the noise handled. | Implemented; snapping covered indirectly by the Investigate tests |
| LKP becomes blocked by a pushed box | Chase and Search goals are snapped the same way; Search skips ring points it cannot reach (at most 3 searches per tick). | Implemented; no dedicated test yet |
| A closed door makes the goal unreachable | GBFS reports not found, the Tracker stops; Chase retries every 0.5 s and drops to Search after 0.7 s without sight. | **Partly:** the plan's wait/bump at the door is not implemented |
| Two noises have equal scores | Prefer the closer one (flat distance, see the deviation above). | `EqualScoresPreferTheCloserNoise` |
| Tracker is stunned during Chase | LKP is kept; the first tick after the reboot resumes the hunt towards it. | `AStunWhileHuntingResumesTheHunt` |

`GridGraph.TryFindNearestTraversable` is an existing spatial helper, not a reachability test. A nearest traversable cell can still be in a disconnected region. Likewise, a closed door's cell is not a legal movement destination in the current graph: the plan's door target denotes the obstruction to wait/bump against. The exact approach-cell selection and wait duration are not specified in the supplied excerpt and remain integration details to confirm; the design does not require bypassing the grid's movement rules.

## Tests

### Required GBFS and sound tests — planned

| Required test/evidence | What it must establish | Status |
| --- | --- | --- |
| GBFS finds a path when one exists | A reachable start/goal pair returns `Found = true` with a legal grid route. | Passing: `FindsAStraightPathOnAnOpenGrid`, `RoutesAroundAWallWithoutCuttingCorners`, `FindsAPathWheneverAStarDoesOnFiftyRandomGrids` |
| Enclosed/unreachable region | GBFS returns `Found = false` when the goal cannot be reached. | Passing: `WalledOffGoalIsNotFound`, `ClosedDoorBlocksMovement`, `BlockedStartOrGoalIsNotFoundWithoutExpanding` |
| Closed-set behaviour | GBFS never expands the same node twice. | Passing: `ExpandsEachReachableCellExactlyOnceWhenTheGoalIsUnreachable` (expanded count equals the reachable region's size exactly) |
| Fewer expansions than A*; no allocation | GBFS expands fewer nodes than A* on open grids and allocates nothing on reuse. | Passing: `ExpandsFewerNodesThanAStarOnOpenGrids`, `ReusedSearchAllocatesNothing` |
| Closed-door attenuation | Propagated noise through a closed door matches `L0 - 4 * pathDistance - sum(35 per closed door crossed)`. | Pending implementation and execution |
| Tracker edge cases | The five cases above follow the planned handling. | Partly: see Edge cases |
| GBFS versus A* | Record nodes expanded and path length for both algorithms over the same 20 start/goal pairs. | Done: see Measured results |

### Brain tests (EditMode, all passing)

`Tests/EditMode/TrackerBrainTests.cs` (23), on a 20 m x 10 m open grid driven tick by tick with game time:
- **Patrol:** `FirstTickPatrolsWithAGbfsRoute`, `PatrolKeepsItsRouteUntilArrivalThenGoesToTheNextPoint`.
- **Hearing:** `ALoudNoiseStartsAnInvestigation`, `InvestigationLooksAroundThenReturnsToPatrolAndDoesNotRepeat`, `ARepeatingSourceDistractsUntilItGoesQuiet`.
- **Vision and hunting:** `SeeingThePlayerStartsTheChase`, `APlayerBehindAWallIsNotSeen`, `APlayerBehindTheTrackerIsSeenOnlyUpClose`, `ChaseReplansEveryHalfSecond`, `LosingSightSwitchesToSearchAfterPointSevenSeconds`, `SearchGivesUpAfterEightSeconds`, `SeeingThePlayerAgainDuringSearchResumesTheChase`, `AMissingOrDeadPlayerEndsTheHunt`.
- **Energy:** `RunningOutOfEnergyRewindsInPlaceThenCarriesOn` (empty path, `Action = Rewind`, double damage, back after 3 s), `ChasingDrainsEnergyFiveTimesFaster`, `ARewindFromTheHuntReturnsToTheHunt`.
- **Stuns and priority:** `AStunDoesNotDrainEnergyAndAFreshRouteGoesOutOnTheFirstTickAfter`, `AStunWhileHuntingResumesTheHunt`, `AStunDuringARewindGoesBackToRewinding`, `TheStunInterruptHasTheHighestPriority` (every machine's rows are in descending priority and the stun is the top machine's first row).
- **Graph changes:** `AGraphChangeOffTheRouteKeepsIt`, `AGraphChangeOnTheRouteReplansAroundIt`.
- `ConstructorRejectsMissingInputs`.

`Tests/EditMode/NoiseMemoryTests.cs` (11): decay formula, a repeating beep beating an older louder shot, the 1.5 s repeat window, the closer-noise tie-break, handled noises, forgetting at the threshold, ignoring out-of-order noises, and replacing the weakest entry when full.

`Tests/EditMode/CompositeStateTests.cs` (6): the child is entered and ticked inside the parent, child transitions run inside it, leaving exits the running child first, re-entry restarts at the initial child, and a child that never ran is never exited.

Full suites after this change: EditMode 538/538, PlayMode 56/56.

### Planned S1 delivery schedule

These are the Full Plan's schedule entries, not a completion checklist. See Current implementation status for what exists in this checkout.

| Week | Planned work |
| --- | --- |
| 11 | GridGraph from NavMesh; grid debug overlay; FSM framework. |
| 12 | GBFS; Tracker Patrol / Investigate / Chase; noise propagation. |
| 13 | Search and Rewind states; wind-down energy; edge cases. |
| 14 | Tracker edge-case tests; GBFS versus A* comparison table. |

## Measured results

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
