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
