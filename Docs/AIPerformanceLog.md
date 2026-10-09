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
- `TrackerBrain.Tick` driven on the real level grid as it was before the prototype rooms: 83 x 83 cells, 5,310 walkable, doors 3 and 4 closed as in Chapter 1. Not re-run on the prototype rooms: the tick's cost is per tick, not per obstacle, and the route count changes only with the script.
- 60 s of game time at 30 ticks/s. A simple body walks each route at the brain's speed.
- Script:
  - patrol on the Assembly Floor
  - from 10 s, the colour terminal beeps from the Painting Room (60, every 0.8 s for 6.4 s)
  - at 30 s, a blaster shot (100)
  - from 40 s to 46 s, the player is in sight, then gone
- The brains went through every state: Patrol, Investigate, Distracted, Chase, Rewind and Search.
- Only `Tick` is timed. Noise propagation runs in S4's runtime once per noise, so it is excluded (its cost is in the table above).
- Unity editor (Mono), 2026-10-08, Intel Core Ultra 7 155H. Allocations are counted with the "GC.Alloc" profiler recorder (see the note below).

| Trackers | Ticks | Tick mean | Median | p99 | Max | Whole frame, all Trackers: mean / p99 / max | Ticks that allocate |
| ---: | ---: | ---: | ---: | ---: | ---: | --- | --- |
| 1 | 1,800 | 2.7 µs | 0.8 µs | 18.7 µs | 1.66 ms | 2.7 / 18.7 µs / 1.66 ms | 23 (1.3%), 99 allocations |
| 7 | 12,600 | 1.3 µs | 0.9 µs | 7.8 µs | 0.13 ms | 8.9 / 95.5 µs / 0.43 ms | 154 (1.2%), 665 allocations |

- **Allocations happen only when the brain hands the body a new route.** That covers 21 replans plus 2 stops for one Tracker; every allocating tick returned a path. Each one allocates the route cells and the world-space path, about 4 small allocations. Every other tick allocates nothing.
- **Seven Trackers at once** (the plan's stress test has 7 agents) cost under 0.1 ms per frame at p99. That is about 0.6% of a 16.7 ms frame.
- **Outlier:** the one 1.66 ms tick is the first time a new state's code runs in the editor (JIT). The second run, with 7 Trackers, has no tick over 0.13 ms.
- **Limits:** these are editor timings for the brain alone, not a player build. The full stress test (`Test_FourAgentsStress`, below) is S4's, with all agent types and the bodies.

**Measuring allocations: `GC.GetAllocatedBytesForCurrentThread()` does not work in Unity.**
- Under Unity's Mono it always returns 0: allocating a 100 KB array shows a change of 0. So any "allocates nothing" test built on it can never fail.
- S1's tests now count allocations with Unity's "GC.Alloc" profiler recorder, through `Tests/EditMode/GcAllocations.Count`. That covers GBFS, noise propagation, grid, FSM, cost model, wind-up energy and the blackboard. They all still pass, so those claims are now verified.
- One claim was made exact rather than "zero": a successful GBFS search costs exactly 2 allocations, its path list and the list's array. `BuildPath` now sizes the list up front, so it never regrows.
- S3's and S4's tests that still use the old counter are listed in the PR for their owners.

## Stress test (`Test_FourAgentsStress`)

| Date | Build | Avg FPS | Worst frame (ms) | AI ms / frame | GC Alloc in searches |
| --- | --- | --- | --- | --- | --- |
