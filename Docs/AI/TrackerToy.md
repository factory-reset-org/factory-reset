# Tracker Toy

Owner: S1 · Architecture: Greedy Best-First Search + hierarchical FSM

Source of truth: the externally supplied Tracker Toy specification from `ToyFactoryRevolt_FullPlan_v4.md`. The Full Plan is not checked into this repository. Behaviour, tests and schedule entries below are design requirements, not claims of completed implementation or measured results.

## Role in gameplay

The Tracker is a wind-up scout that hunts by ear. It uses Greedy Best-First Search (GBFS) to move toward sounds and pursue the player, with behaviour organised by the shared hierarchical, data-driven FSM architecture.

Its eager movement toward a target is intentional: it does not need the shortest route to feel like a scout sniffing toward a sound. The player can distract it with noise or kite it until its wind-up energy runs out, then attack during Rewind.

## Current implementation status

The current repository contains the shared `GridGraph`, `BinaryHeap`, `BaseCostModel`, `AStarSearch`, `IPathfinder` and `PathResult`, plus the brain/body contracts `IAgentBrain`, `AgentContext` and `AgentIntent`. The grid provides 0.5 m cells, eight-connected movement without diagonal corner cutting, explicit door identity/state, and nearest-traversable-cell lookup. `BaseCostModel.OctileDistance` supplies the shared heuristic calculation.

Implemented: wind-down energy (`WindUpEnergy`), the shared FSM framework, and the Tracker's search (`GreedyBestFirstSearch`, measured against A* below). Not yet implemented: the seven Tracker states and noise propagation. `WorldBlackboard` and `SensorSnapshot` are still stubs for the shared world/perception data. GridGraph exists as pure AI Core code; this does not mean the planned GridManager/NavMesh sampling integration or grid debug overlay is complete.

The sections below describe the planned Tracker design. Tracker test results and measured comparisons remain pending.

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

The planned hierarchical FSM uses state objects with `Enter`, `Tick` and `Exit`. Transitions are data containing a **condition delegate**, **priority** and **target state**. It is not a switch-statement FSM. The supplied Tracker specification does not define a parent-state layout or numeric transition priorities.

The planned brain will follow the existing `IAgentBrain` contract: read `AgentContext`, return an `AgentIntent` for Runtime to execute, respond to `OnGraphChanged`, and handle `OnStunned`. AI decision logic remains scene-independent; Physics and NavMesh queries belong in Runtime, as required by [CONTRIBUTING.md](../../CONTRIBUTING.md).

`IPathfinder.FindPath` works with `Vector2Int` cells and returns a `PathResult`, including `Found`, `Cells`, `NodesExpanded`, `ElapsedMs` and `GraphVersion`. Planned GBFS should use the existing shared grid/search infrastructure. The graph's global version and affected-cell notifications support detecting routes that may need replanning.

### Planned states and exit conditions

LKP means **last known position** of the player.

| State | Planned behaviour | Exit condition / next action from the Full Plan |
| --- | --- | --- |
| **Patrol** | Use GBFS between patrol waypoints. | Hear noise above the hearing threshold, or see the player. |
| **Investigate** | Use GBFS to the loudest recent noise cell, selected using the noise score below. | Arrive, hear a louder noise, or see the player. |
| **Chase** | Use GBFS to the player's cell; repath every **0.5 s**. | Lose sight → **Search**; energy reaches zero → **Rewind**. |
| **Search** | Sample cells on expanding rings around LKP and use GBFS to each. | **8 s** timeout → **Patrol**; see the player → **Chase**. |
| **Distracted** | Use GBFS to a thrown toy and circle it. | The toy stops making noise. |
| **Rewind** | Stop and wind the key for **3 s**; take double hits while vulnerable. | Energy full → return to the previous state. |
| **Stunned** | Remain fallen apart. | The stun timer ends. |

Noise investigation and visible-player pursuit correspond to Investigate and Chase respectively. When Investigate receives a louder noise, noise selection supplies the new target. A stun during Chase must preserve LKP for recovery; zero energy requires Rewind as described above.

The excerpt does not specify the destination after Investigate arrives, after the Distracted toy becomes silent, or after the Stunned timer ends. It also does not supply the exact entry/arbitration rule for Distracted or numeric priorities when conditions compete. These transition details remain **TODO: confirm against the Full Plan**, rather than assigning new behaviour in this document.

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

## Edge cases

These are planned behaviours from the Full Plan; the test descriptions are requirements, not passing-test claims.

| Case | Handling | Test |
| --- | --- | --- |
| Noise source is in an unreachable/blocked cell | Search/ring outward to the nearest traversable cell. | A blocked source produces a traversable investigation target; pathfinding still reports failure if that target cannot be reached. |
| LKP becomes blocked by a pushed box | Start Search from the nearest traversable cell. | Search uses a traversable replacement for blocked LKP. |
| A closed door makes the goal unreachable | Treat the door cell as the target, wait/bump it, then Search. | Door obstruction leads to the planned wait/bump and Search behaviour. |
| Two noises have equal scores | Prefer the one closer by path distance. | Equal-score selection uses path distance as its tie-break. |
| Tracker is stunned during Chase | Preserve LKP in memory for recovery. | Stun and recovery do not erase the recorded LKP. |

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
| Tracker edge cases | The five cases above follow the planned handling. | Pending implementation and execution |
| GBFS versus A* | Record nodes expanded and path length for both algorithms over the same 20 start/goal pairs. | Done: see Measured results |

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
| Tracker test execution results | Pending / TODO |
| Tracker profiling and four-agent stress measurements | Pending / TODO |
