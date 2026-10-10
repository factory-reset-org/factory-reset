# AI Performance Log (IS evidence)

Measured with ProfilerMarkers (`AI.Tracker.GBFS`, `AI.Guard.AStar`, ...) on the lab machine.

## Search comparisons

| Date | Agent | Algorithm | Start → Goal | Nodes expanded | Path length | ms |
| --- | --- | --- | --- | --- | --- | --- |
| 2026-10-04 | Tracker | GBFS | 20 random pairs on the level grid (total) | 1,270 | 468.2 m | 2.62 |
| 2026-10-04 | Tracker | A* (baseline, same pairs) | 20 random pairs on the level grid (total) | 5,426 | 412.2 m | 12.27 |
| 2026-10-09 | Tracker | GBFS | 20 random pairs on the prototype-room grid (total) | 1,659 | 511.6 m | 4.10 |
| 2026-10-09 | Tracker | A* (baseline, same pairs) | 20 random pairs on the prototype-room grid (total) | 7,080 | 439.8 m | 17.98 |

### GBFS vs A* (Tracker, S1)

Level grid with the prototype rooms (`Env.unity`, 83 x 83 cells of 0.5 m, 4,963 walkable, 4,955 traversable with the Control Room doors closed), 2026-10-09. 20 start/goal pairs drawn from traversable cells with `System.Random(2026)`, keeping only pairs A* can reach; both searches use `BaseCostModel` and the same reused instances, after one warm-up search each. Editor timing, not the lab machine. Cells are in grid coordinates.

| # | Start | Goal | A* nodes | GBFS nodes | A* length (m) | GBFS length (m) |
| --- | --- | --- | --- | --- | --- | --- |
| 1 | 8,13 | 33,21 | 53 | 26 | 14.2 | 14.2 |
| 2 | 74,73 | 19,7 | 935 | 187 | 48.9 | 62.0 |
| 3 | 26,52 | 8,69 | 81 | 25 | 14.3 | 14.3 |
| 4 | 58,48 | 37,27 | 504 | 164 | 21.8 | 26.1 |
| 5 | 76,72 | 33,15 | 556 | 140 | 41.0 | 54.1 |
| 6 | 33,30 | 38,37 | 17 | 8 | 4.5 | 4.5 |
| 7 | 49,10 | 38,23 | 41 | 15 | 9.1 | 9.1 |
| 8 | 61,37 | 63,9 | 81 | 29 | 14.4 | 14.4 |
| 9 | 48,38 | 80,62 | 321 | 45 | 24.5 | 24.5 |
| 10 | 50,29 | 80,63 | 414 | 44 | 25.8 | 25.8 |
| 11 | 49,19 | 62,47 | 101 | 29 | 16.7 | 16.7 |
| 12 | 56,64 | 77,2 | 703 | 143 | 38.3 | 51.0 |
| 13 | 58,11 | 71,9 | 21 | 14 | 6.9 | 6.9 |
| 14 | 3,20 | 54,8 | 394 | 104 | 28.3 | 35.8 |
| 15 | 50,22 | 71,24 | 41 | 25 | 12.3 | 12.3 |
| 16 | 61,21 | 19,40 | 256 | 103 | 25.8 | 33.4 |
| 17 | 48,48 | 43,15 | 274 | 102 | 24.6 | 28.6 |
| 18 | 12,7 | 63,80 | 2260 | 435 | 56.4 | 65.9 |
| 19 | 69,62 | 65,66 | 10 | 6 | 3.1 | 3.1 |
| 20 | 13,4 | 22,18 | 17 | 15 | 8.9 | 8.9 |
| **Total** | | | **7,080** | **1,659** | **439.8** | **511.6** |

- GBFS expanded **77% fewer nodes** (1,659 vs 7,080) and ran **4.4x faster** (4.10 ms vs 17.98 ms in total).
- Its paths were **16.3% longer** in total: equal to A*'s on 12 of 20 pairs, longer on the other 8. The longest detours (pairs 2, 5 and 12, about 13 m each) are cross-level routes that must go round the central walls and through the shelf maze: greedy heads straight for the goal, meets a wall or a shelf run, then follows it.
- Worst case for A*, pair 18 (2,260 nodes, from the Assembly Floor to the far corner of Storage through two doorways and the maze): GBFS needed 435. For a chaser replanning every 0.5 s, that is the trade the Tracker is designed to make.
- **Before the prototype rooms** (2026-10-04, 5,318 walkable cells, a different set of pairs from the same seed): 77% fewer nodes, 4.7x faster, 13.6% longer. The conclusion is unchanged.

### Noise propagation (Tracker hearing, S1)

`NoisePropagation.Propagate` on the level grid with the prototype rooms (83 x 83 cells, 4,963 walkable), one noise from the middle of the Assembly Floor (10.5, 10.5), doors 3 and 4 closed. Average of 50 runs after one warm-up, Unity editor (Mono), 2026-10-09.

| Source | L0 | Audible radius | Cells reached | Time per noise |
| --- | ---: | ---: | ---: | ---: |
| Blaster shot | 100 | 22.5 m | 2,109 | 5.64 ms |
| Relay alarm / core explosion | 90 | 20 m | 1,826 | 5.07 ms |
| Wind-up toy landing | 70 | 15 m | 1,504 | 3.86 ms |
| Door slam / terminal beep | 60 | 12.5 m | 1,387 | 3.62 ms |
| Box impact | 50 | 10 m | 1,058 | 2.84 ms |
| Pressure plate click | 40 | 7.5 m | 589 | 2.10 ms |
| Running footsteps | 25 | 3.75 m | 160 | 0.41 ms |

- The cost scales with the audible area, not the level size: the threshold bound stops the spread, so footsteps touch 160 cells while a shot touches 2,109 of the 4,963 walkable cells. Quiet noises (footsteps every few frames) stay cheap; loud ones are rare events.
- A few more cells than on the old layout (a shot reached 1,980): the presses moved from the middle of the Assembly Floor to its west side, so the area round the source is more open.
- **No allocations** over 100 blaster-shot propagations (stamped arrays and a reused heap), checked with the GC.Alloc recorder (see the note under Tracker brain tick).
- Closed-door check at door 4, on the first cell inside the Control Room: 21 behind the closed door, 56 with it open (difference exactly 35).
- Lab-machine and player-build timings still to record; the editor's per-cell cost (about 2.7 µs) matches A*'s on the same grid (about 2.5 µs per expanded node).

### Tracker brain tick (S1, and S1's share of the stress test)

**Setup:**
- `TrackerBrain.Tick` driven on the level grid with the prototype rooms: 83 x 83 cells, 4,963 walkable, doors 3 and 4 closed as in Chapter 1.
- 60 s of game time at 30 ticks/s. A simple body walks each route at the brain's speed. The Trackers patrol S4's Tracker waypoints round the Assembly Floor ((4, 14), (17, 14), (17, 5), (4, 5)); with 7, each starts at a different waypoint.
- Script:
  - patrol on the Assembly Floor
  - from 10 s, the colour terminal beeps from the Painting Room (60, every 0.8 s for 6.4 s)
  - at 30 s, a blaster shot (100) on the Assembly Floor
  - from 40 s to 46 s, the player is in sight in the middle of the Assembly Floor, then gone
- Noise reaches a brain only where `NoisePropagation` says the Tracker's cell hears it, as at runtime.
- States reached: Patrol, Investigate, Chase, Rewind and Search. **Not Distracted:** on this layout the terminal (31, 10.5) is about 14 m from the nearest point of the patrol, just past the beep's 12.5 m audible radius, so the beeps never reach the patrol. In play the Tracker hears the terminal only if it is already near the Painting door; Distracted is covered by `TrackerBrainTests` and the `Test_TerminalNoise` scene.
- Only `Tick` is timed. Noise propagation runs in S4's runtime once per noise, so it is excluded (its cost is in the table above).
- Unity editor (Mono), 2026-10-09, Intel Core Ultra 7 155H. Allocations are counted with the "GC.Alloc" profiler recorder (see the note below).

| Trackers | Ticks | Tick mean | Median | p99 | Max | Whole frame, all Trackers: mean / p99 / max | Ticks that allocate |
| ---: | ---: | ---: | ---: | ---: | ---: | --- | --- |
| 1 | 1,800 | 1.1 µs | 0.5 µs | 8.2 µs | 95 µs | 1.1 / 8.2 / 95 µs | 20 (1.1%), 86 allocations |
| 7 | 12,600 | 0.9 µs | 0.5 µs | 4.8 µs | 103 µs | 6.3 / 59.3 µs / 0.31 ms | 134 (1.1%), 582 allocations |

- **Allocations happen only when the brain hands the body a new route.** Every allocating tick returned a path (20 of 20 for one Tracker, 134 of 134 for seven). Each one allocates the route cells and the world-space path, about 4 small allocations. Every other tick allocates nothing.
- **Seven Trackers at once** (the plan's stress test has 7 agents) cost under 0.06 ms per frame at p99. That is about 0.4% of a 16.7 ms frame.
- **Compared with the run before the prototype rooms** (2026-10-08, 5,310 walkable cells): the same picture. One Tracker then had a mean of 2.7 µs and a single 1.66 ms first-run (JIT) outlier; this run came after the code had already run in the editor, so it has no JIT outlier.
- **Limits:** these are editor timings for the brain alone, not a player build. The full stress test (`Test_FourAgentsStress`, below) is S4's, with all agent types and the bodies.

**Measuring allocations: `GC.GetAllocatedBytesForCurrentThread()` does not work in Unity.**
- Under Unity's Mono it always returns 0: allocating a 100 KB array shows a change of 0. So any "allocates nothing" test built on it can never fail.
- S1's tests now count allocations with Unity's "GC.Alloc" profiler recorder, through `Tests/EditMode/GcAllocations.Count`. That covers GBFS, noise propagation, grid, FSM, cost model, wind-up energy and the blackboard. They all still pass, so those claims are now verified.
- One claim was made exact rather than "zero": a successful GBFS search costs exactly 2 allocations, its path list and the list's array. `BuildPath` now sizes the list up front, so it never regrows.
- S3's and S4's tests that still use the old counter are listed in the PR for their owners.

## Stress test (`Test_FourAgentsStress`)

`Tests/PlayMode/FourAgentsStressTests` (category Evidence, explicit: run it on its own). It loads `Bootstrap`, jumps to Chapter 4 with the debug chapter jump (the Captain awake, every door open), then walks the player round a loop through the Control Room and the Storage doorway at 4 m/s for 30 s after a 3 s warm-up, so all seven agents (1 Tracker, 1 Guard, 4 Saboteurs, the Captain) keep seeing, hearing and chasing. The player's health is refilled every frame so the run is not cut short. AI time is the sum of the `AI.Brain.Tick.<type>` markers (around each brain's `Tick` in `AgentController`, so searches are inside them). Allocation is Unity's "GC Allocated In Frame", which counts everything in the frame, the test's own per-frame work included, not only the AI.

| Date | Build | Avg FPS | Frame ms (avg / p99 / worst) | AI ms / frame (avg / p99 / worst) | GC allocated in frame, everything (avg / p99) |
| --- | --- | --- | --- | --- | --- |
| 2026-10-09 | Editor (Mono), 30 s, 5,887 frames | 196.2 | 5.10 / 13.55 / 44.58 | 0.179 / **5.191** / 14.04 | 10.3 KB / 12.7 KB |

**By brain** (same run):

| Brain | Avg ms / frame | p99 | Worst | Frames over 1 ms |
| --- | --- | --- | --- | --- |
| Tracker (S1) | 0.011 | 0.020 | 0.082 | 0 |
| Guard (S2) | 0.071 | 3.052 | 11.790 | 83 |
| Saboteur x4 (S3, placeholder brain) | 0.002 | 0.004 | 0.047 | 0 |
| Captain (S4) | 0.095 | 4.967 | 14.007 | 67 |

| Search marker | Avg ms / frame | Worst | Frames over 1 ms |
| --- | --- | --- | --- |
| `AI.Tracker.GBFS` | 0.000 | 0.062 | 0 |
| `AI.AStarSearch.FindPath` | 0.012 | 5.791 | 19 |
| `AI.DijkstraField.Compute` | 0.089 | 13.911 | 67 |
| `AI.NoisePropagation.Propagate` | 0.000 | 0.000 | 0 |

**What it shows:**
- **On average the AI is cheap:** 0.18 ms a frame for all seven agents, about 1% of a 16.7 ms frame.
- **But it spikes past the 2 ms budget on about 1% of frames** (p99 5.2 ms). The test logged a warning for this rather than failing; see the re-run after the Captain fix below. Two brains cause it:
  - **Captain:** 67 frames over 1 ms in 30 s, about two a second, which is its 2 Hz decision. Every spike is `DijkstraField.Compute`. With the player still, the cached goal fields are never rebuilt (5 computations, then none); the spikes come from the two fields each decision rebuilds while the player moves, the player's field (goal inference) and the Captain's own (intercept). One full-level field costs 4.33 ms median (6.95 ms worst, 4,878 cells expanded) in the editor; a bounded one 2.0 ms. Two in one decision exceed the budget. This is fixed and re-measured in `OptimisationLog.md`.
  - **Guard:** 83 frames over 1 ms. A* accounts for 19 of them (worst 5.8 ms); the rest is other work in the Guard's tick, probably cover scoring. Reported to S2.
- **Limits:** editor timings with Mono; a player build is faster. The worst frame (44.6 ms) includes editor work outside the AI.

### After the Captain fix (2026-10-09)

Three changes, each written up in `OptimisationLog.md`:
1. a shared neighbour table (`GridAdjacency`) that every field reads;
2. a cost-only A* (`OneToOneCost`) for the two single-pair questions that used to build a whole field;
3. goal fields repaired in place after a grid change (`DijkstraField.Refresh`), one per frame.

Before and after were run back to back in one editor session: `develop` first, then this branch. That session ran the editor slower than the first run above (about 129 FPS, not 196), so compare the rows with each other, not with the table above.

| Captain (S4), `Test_FourAgentsStress` | Avg ms / frame | p99 | Worst | Frames over 1 ms |
| --- | --- | --- | --- | --- |
| Before (`develop`, 3,843 frames) | 0.174 | 7.167 | 15.069 | 67 |
| After (this branch, 3,875 frames) | 0.014 | 0.139 | 1.136 | 1 |

`AI.DijkstraField.Compute` worst per frame: 14.98 ms before, 0.09 ms after. The new `AI.OneToOneCost.Compute` worst is 0.48 ms.

**Pushed box (`Test_PushedBoxStress`).** This is a new evidence test in the same fixture: the same Chapter 4 run for 20 s, with a box-sized blocker moved one cell every 0.5 s through `GridManager.SetBlocker`, the call `PushableBox` makes. That is 40 moves and 41 grid changes, and every move makes all four of the Captain's goal fields stale.

| Captain (S4), `Test_PushedBoxStress` | Avg ms / frame | p99 | Worst | Frames over 1 ms |
| --- | --- | --- | --- | --- |
| Before (`develop`, 2,476 frames) | 0.575 | 32.014 | 39.077 | 45 |
| After (this branch, 2,670 frames) | 0.016 | 0.201 | 0.920 | 0 |

After the change, all of the field work is repairs: 157 frames held one (40 moves × 4 goal fields, give or take a decision), with a worst of 0.51 ms. Before it, each move recomputed every goal field, plus the player's field and the Captain's own, all in one decision.

**What it shows now:**
- **The Captain** is at p99 0.14 ms in normal play and 0.20 ms with a box being pushed. Both tests now check its p99 against 1 ms, half the AI budget, and fail above it.
- **The Guard** is what remains over the 2 ms AI budget: p99 4.93 ms, worst 23.6 ms, 78 frames over 1 ms. `AStarSearch` accounts for 41 of those frames (worst 19.2 ms). The total budget stays a warning until S2's fix; the test cannot fix another agent's brain. `AStarSearch` could read `GridAdjacency` the way `DijkstraField` now does, which took the field from 4.8 to 0.7 ms; this has been offered to S2.
- **Limits:** editor timings. One Captain frame of 1.14 ms in the stress run is not a field: the field marker never went above 0.09 ms in the whole run. It is probably the Captain's own A* route (`MoveTo`) or editor noise.

### After the Guard fix (2026-10-09)

The Guard was the brain left over the AI budget above. Four changes, in the order they were made:
1. **Sight memory** (`CachedCoverVisibility`): each cell's line of sight is tested once and remembered until the player changes cell, the grid changes, or the Guard's own cover is flanked. One cover decision used to ask about the same cell up to ten times (its own protection, its neighbours' peek checks, every A* step), and each ask is a physics raycast.
2. **Best-first cover search** (`CoverEvaluator.FindBest`): every possible cell first gets an upper bound, its score if it turned out to be full, peekable cover. Cells are sight-tested in that order and the search stops once the worst cell kept scores at least the next bound. It returns the same top three as scoring every candidate (tested against the exhaustive ranking at three ranges). The cells next to obstacles, the only ones that can be cover, are worked out once per grid version instead of on every decision.
3. **Bounded, cost-only path costing**: ranking needs the cost of a route, not the route, and only up to `MaxPathCost` (60), past which the travel term of the score is zero. `OneToOneCost` is used with that cost bound and a new effort limit of 400 cells (`GuardBrain.CostSearchCells`). A cover that cannot be costed within the limit counts as far. Whether it can be reached at all is read from `GridRegions`, the grid's connected areas, rebuilt only when the grid changes.
4. **`AStarSearch` reads `GridAdjacency`**, the shared neighbour table, as offered above. Same paths.

Changes 1, 2 and 4 do not change which cover is chosen. Change 3 does in one case: a cover that needs more than 400 cells of search to cost is scored as far even if its true cost was under 60.

**How it was found.** The first two attempts were guesses and the second gained almost nothing. `Tests/PlayMode/GuardTickEvidenceTests` (category Evidence, explicit) was then written: the same scenario as `Test_FourAgentsStress`, with every slow Guard frame broken down by `ProfilerMarker`s inside `GuardBrain` (`AI.Guard.FindBest`, `AI.Guard.PathCost`, `AI.Guard.MoveTo`, ...), the sight checks made, and the Guard's state. It showed that with the sight memory in place the slow frames ran no sight checks at all: 46% of their time was the loop over about 3,700 cells looking for obstacles, and 45% was path costing that spread over most of the level, because out of the player's sight every step is cheap and the cost bound alone never stopped it.

| Guard (S2), `Test_FourAgentsStress` | Avg ms / frame | p99 | Worst | Frames over 1 ms | AI total p99 | Budget warning |
| --- | --- | --- | --- | --- | --- | --- |
| Before (the 2026-10-09 run above) | not recorded | 4.93 | 23.6 | 78 | over 2 | yes |
| Changes 1, 2, 4 (3,822 frames) | 0.093 | 2.434 | 12.915 | 82 | 2.523 | yes |
| Plus the cost bound, no effort limit (3,357 frames) | 0.089 | 2.412 | 10.623 | 82 | 2.535 | yes |
| All four (3,051 frames) | 0.068 | 1.690 | 7.931 | 52 | **1.755** | no |

The last three rows are from one editor session; the first is the earlier session logged above, so compare it only roughly. A second run of the final build stalled the editor for 39 s in one frame (1,424 frames measured instead of about 3,000) and is not used.

| `Test_GuardTickBreakdown`, slow frames only | Before the obstacle-cell list and the effort limit | After |
| --- | --- | --- |
| Guard frames over 1 ms | 88 | 50 |
| `AI.Guard.FindBest`, average in a slow frame | 1.329 ms | 0.600 ms |
| `AI.Guard.PathCost`, worst | 6.325 ms | 2.559 ms |
| `AI.Guard.PathCost`, average in a slow frame | 1.299 ms | 1.254 ms |
| Sight checks in the slow frames | 2,621 | 0 |

**What it shows now:**
- **The AI total is under its 2 ms budget** at p99 (1.76 ms) in the one clean run of the final build, and the warning is not logged. One clean run is thin evidence; it should be re-run before the warning is turned into a hard check.
- **The Guard is at p99 1.69 ms**, from 4.93 ms. It is still the largest share of the AI budget.
- **What is left:** path costing is still about 1.25 ms in a typical slow frame (three or four capped searches in one tick), and the few worst frames (5 of 50) are now the tactical route search itself, `MoveTo`, at 2.6 to 9.2 ms when the Guard is 18 to 27 m from the player. Neither is addressed here. The next step would be to spread one decision over several frames, which is the path request scheduler that is still not built.
- **Limits:** editor timings with Mono, one session.


### Path request scheduler (2026-10-09, measured 2026-10-10)

`BrainTickScheduler` now gives brain decisions a per-frame budget of 2 ms: once a frame's brains have used it, the remaining agents tick in the next frame, at most 2 frames late (see the DesignDoc's path follower section). It cannot split one decision, so the Guard's single 2.6 to 9.2 ms route searches still take their frame; what it removes is two heavy decisions landing in the same frame, which is what pushes the AI total over its budget.

- **Tested:** `AgentSchedulingTests`. Three brains costing 3 ms each no longer all tick in the same frame, none waits more than 2 frames, and light brains never wait.
- **Measured in the level:** `Test_FourAgentsStress` (budget 2 ms) and `Test_FourAgentsStress_NoScheduler` (the same run with an unlimited budget, so nothing ever waits), run back to back twice in one editor session with Unity focused. Chapter 4, all 7 agents, 30 s each, about 3,000 frames per run.

| Run | Scheduler | AI p99 | AI worst | AI frames over 2 ms | Decisions deferred | Avg FPS |
| --- | --- | --- | --- | --- | --- | --- |
| Pair 1 | On (2 ms) | 1.519 ms | 3.584 ms | 19 | 85 | 99.5 |
| Pair 1 | Off | 2.030 ms | 7.807 ms | 33 | 0 | 100.3 |
| Pair 2 | On (2 ms) | 1.978 ms | 7.522 ms | 24 | 121 | 102.8 |
| Pair 2 | Off | 2.036 ms | 6.727 ms | 32 | 0 | 102.8 |

**What it shows:**
- **About a third fewer frames over the AI budget** (19 and 24 against 33 and 32), and p99 kept just under 2 ms where it is just over without the scheduler.
- **The worst frames are unchanged, as predicted:** they are single Guard route searches (the Guard's worst tick was 2.9 to 7.7 ms) that are longer than the whole budget on their own. Only splitting the Guard's search over several ticks (S2's brain) would cut those.
- **The cost is small:** 85 and 121 decisions in about 3,000 frames waited one frame (under 4%), always within the 2-frame limit, and the frame rate is the same.
- **Variance:** a single earlier run that day with the scheduler on gave p99 2.007 ms and a worst frame of 11.3 ms (the Guard's tick). Run-to-run differences come mostly from when the Guard searches, so compare runs within one session, as here. Editor timings, one machine.

## Captain prediction and intercept in the real level (S4, 2026-10-10)

From `CaptainAccuracyEvidenceTests` (EditMode, category Evidence, about 5 s). The level is a snapshot of the game's grid and objective targets taken in Play mode from Bootstrap (`Tests/EditMode/Data/CaptainLevelSnapshot.txt`: 83 x 83 cells, Chapter 3 and Chapter 4 goal sets), so every run is deterministic. Seeded scripted players walk from random walkable cells (at least 12 m from their goal) straight to it, by a detour through a cell near the middle of the route (35-70% longer), or with a feint (half way towards another goal, then turning). The Captain's `GoalInference` is fed exactly as the brain feeds it: the player's cell at 2 Hz and the cell 5 s ago. Two simpler predictors are measured on the same samples: the goal nearest the player (shortest path), and the highest prior. Accuracy is the share of samples whose most likely goal is the true one. "Confidently" means P >= 0.5, the commit threshold.

### In game: real priors, the player heading where the game lets them

| Chapter | Route | Predictor | 1st quarter | 2nd | 3rd | 4th | All |
| --- | --- | --- | --- | --- | --- | --- | --- |
| 3 | Direct | Captain (Bayesian) | 100% | 100% | 100% | 100% | 100% |
| 3 | Direct | Nearest goal | 32% | 57% | 89% | 100% | 70% |
| 3 | Direct | Prior only | 100% | 100% | 100% | 100% | 100% |
| 3 | Detour | Captain (Bayesian) | 99% | 99% | 96% | 100% | 98% |
| 3 | Detour | Nearest goal | 37% | 65% | 78% | 99% | 70% |
| 3 | Detour | Prior only | 100% | 100% | 100% | 100% | 100% |
| 3 | Feint | Captain (Bayesian) | 86% | 82% | 99% | 100% | 91% |
| 3 | Feint | Nearest goal | 17% | 22% | 58% | 100% | 49% |
| 3 | Feint | Prior only | 100% | 100% | 100% | 100% | 100% |
| 4 | Direct | Captain (Bayesian) | 47% | 55% | 86% | 98% | 71% |
| 4 | Direct | Nearest goal | 8% | 9% | 12% | 63% | 22% |
| 4 | Direct | Prior only | 38% | 37% | 38% | 37% | 38% |
| 4 | Detour | Captain (Bayesian) | 40% | 46% | 46% | 87% | 54% |
| 4 | Detour | Nearest goal | 16% | 20% | 23% | 69% | 31% |
| 4 | Detour | Prior only | 37% | 37% | 36% | 36% | 37% |
| 4 | Feint | Captain (Bayesian) | 21% | 20% | 57% | 98% | 48% |
| 4 | Feint | Nearest goal | 15% | 13% | 15% | 61% | 25% |
| 4 | Feint | Prior only | 28% | 27% | 29% | 27% | 28% |

| Chapter | Route | Confidently right (P >= 0.5) | Median time to it | Median share of the trip left | Confidently wrong | Feint: took the bait | Feint: switched back | Median switch delay |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| 3 | Direct | 100% | 0.5 s | 94% | 0% | - | - | - |
| 3 | Detour | 100% | 0.5 s | 94% | 12% | - | - | - |
| 3 | Feint | 100% | 5.0 s | 47% | 5% | 23% | 100% | 0.4 s |
| 4 | Direct | 90% | 5.0 s | 35% | 0% | - | - | - |
| 4 | Detour | 82% | 5.0 s | 25% | 47% | - | - | - |
| 4 | Feint | 83% | 6.0 s | 27% | 2% | 13% | 98% | 1.1 s |

Chapter 3 is easy by construction: the snapshot has one open task, and its switch and the console are sealed, so the prior alone already names it. Chapter 4 (three cores) is where the predictors differ.

### Before the sealed-goal priors (same runs, old `GoalPriors`)

| Chapter 4, to a core | Accuracy before | After | Confidently wrong before | After | Feint: switched back before | After |
| --- | --- | --- | --- | --- | --- | --- |
| Straight | 30% | 71% | 53% | 0% | - | - |
| Detour | 28% | 54% | 65% | 47% | - | - |
| Feint | 24% | 48% | 25% | 2% | 77% | 98% |

With the console at 0.45 next to three cores at 0.18 each, the Captain was sure of the console before the player had moved, although the console is sealed until the cores are down. Sealed goals now get 0.05 (see CaptainBot.md, Priors). The check `PredictionInGameInTheRealLevel` fails on the old priors.

### Movement only: every goal equally likely beforehand, any goal the true one

| Chapter | Route | Predictor | 1st quarter | 2nd | 3rd | 4th | All |
| --- | --- | --- | --- | --- | --- | --- | --- |
| 3 | Direct | Captain (Bayesian) | 54% | 60% | 65% | 86% | 66% |
| 3 | Direct | Nearest goal | 24% | 37% | 56% | 72% | 47% |
| 3 | Direct | Prior only | 37% | 37% | 36% | 39% | 37% |
| 3 | Detour | Captain (Bayesian) | 52% | 54% | 58% | 83% | 61% |
| 3 | Detour | Nearest goal | 33% | 49% | 65% | 83% | 57% |
| 3 | Detour | Prior only | 42% | 41% | 42% | 41% | 42% |
| 3 | Feint | Captain (Bayesian) | 18% | 16% | 44% | 80% | 39% |
| 3 | Feint | Nearest goal | 21% | 18% | 39% | 69% | 36% |
| 3 | Feint | Prior only | 32% | 33% | 32% | 32% | 32% |
| 4 | Direct | Captain (Bayesian) | 40% | 46% | 72% | 91% | 62% |
| 4 | Direct | Nearest goal | 23% | 23% | 25% | 68% | 34% |
| 4 | Direct | Prior only | 37% | 36% | 37% | 35% | 36% |
| 4 | Detour | Captain (Bayesian) | 33% | 27% | 23% | 69% | 37% |
| 4 | Detour | Nearest goal | 33% | 33% | 32% | 64% | 40% |
| 4 | Detour | Prior only | 24% | 24% | 24% | 23% | 24% |
| 4 | Feint | Captain (Bayesian) | 11% | 11% | 39% | 86% | 36% |
| 4 | Feint | Nearest goal | 27% | 29% | 29% | 64% | 37% |
| 4 | Feint | Prior only | 19% | 18% | 19% | 18% | 19% |

| Chapter | Route | Confidently right (P >= 0.5) | Median time to it | Median share of the trip left | Confidently wrong | Feint: took the bait | Feint: switched back | Median switch delay |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| 3 | Direct | 80% | 5.0 s | 32% | 3% | - | - | - |
| 3 | Detour | 75% | 4.0 s | 50% | 37% | - | - | - |
| 3 | Feint | 87% | 7.5 s | 26% | 15% | 28% | 98% | 2.0 s |
| 4 | Direct | 68% | 5.5 s | 22% | 0% | - | - | - |
| 4 | Detour | 53% | 6.5 s | 17% | 37% | - | - | - |
| 4 | Feint | 48% | 6.5 s | 31% | 0% | 12% | 92% | 1.4 s |

Overall the inference beats the nearest goal; it ties or loses slightly on Chapter 4 detours and feints, where every goal lies behind the same two Control Room doorways, so a route there fits all of them until the player is inside.

### Intercepting against chasing

Chapter 3, the Captain starting at its spawn by the console and the player heading for the open task; 40 seeded trips per row, each starting at least 20 m away and out of its sight.
Contact = the Captain within 10 m of the player with line of sight, before the player reaches their goal.

| Player | Route | Captain | Contact before the goal | Median seconds to spare |
| --- | --- | --- | --- | --- |
| Walking 4 m/s | Direct | Predicts and intercepts | 85% | 3.2 s |
| Walking 4 m/s | Direct | Chases | 50% | 4.6 s |
| Walking 4 m/s | Detour | Predicts and intercepts | 70% | 3.9 s |
| Walking 4 m/s | Detour | Chases | 78% | 3.8 s |
| Sprinting 7 m/s | Direct | Predicts and intercepts | 50% | 1.1 s |
| Sprinting 7 m/s | Direct | Chases | 10% | 5.2 s |
| Sprinting 7 m/s | Detour | Predicts and intercepts | 63% | 1.5 s |
| Sprinting 7 m/s | Detour | Chases | 38% | 2.1 s |

"Chases" is the same body re-planning the shortest route to the player every 0.5 s at the Captain's 4.6 m/s. Overall: 67% contact for the Captain against 44% for chasing. Against a sprinting player (7 m/s) chasing almost never works on a straight route (10%), which is why the Captain predicts and waits. The one loss is a walking player on a detour, who stays close enough to be caught from behind.

**Limits:** scripted players on the level's grid, not people; detours are random rather than tactical; Chapter 3 had one open task when the snapshot was taken. Re-take the snapshot if the level changes.
