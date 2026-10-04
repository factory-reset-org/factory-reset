# AI Performance Log (IS evidence)

Measured with ProfilerMarkers (`AI.Tracker.GBFS`, `AI.Guard.AStar`, ...) on the lab machine.

## Search comparisons

| Date | Agent | Algorithm | Start → Goal | Nodes expanded | Path length | ms |
| --- | --- | --- | --- | --- | --- | --- |
| 2026-10-04 | Tracker | GBFS | 20 random pairs on the level grid (total) | 1,270 | 468.2 m | 2.62 |
| 2026-10-04 | Tracker | A* (baseline, same pairs) | 20 random pairs on the level grid (total) | 5,426 | 412.2 m | 12.27 |

### GBFS vs A* (Tracker, S1)

Greybox level grid (`Env.unity`, 83 x 83 cells of 0.5 m, 5,318 walkable, Control Room doors closed). 20 start/goal pairs drawn from walkable cells with `System.Random(2026)`, keeping only pairs A* can reach; both searches use `BaseCostModel` and the same reused instances, after one warm-up search each. Editor timing, not the lab machine. Cells are in grid coordinates.

| # | Start | Goal | A* nodes | GBFS nodes | A* length (m) | GBFS length (m) |
| --- | --- | --- | --- | --- | --- | --- |
| 1 | 76,6 | 55,70 | 1326 | 146 | 41.2 | 48.1 |
| 2 | 22,8 | 24,27 | 53 | 20 | 9.9 | 9.9 |
| 3 | 2,19 | 71,21 | 204 | 73 | 35.7 | 36.0 |
| 4 | 73,9 | 60,24 | 35 | 19 | 11.1 | 11.1 |
| 5 | 18,65 | 16,52 | 21 | 14 | 6.9 | 6.9 |
| 6 | 67,16 | 77,35 | 61 | 20 | 11.6 | 11.6 |
| 7 | 50,70 | 59,23 | 453 | 130 | 30.1 | 35.1 |
| 8 | 76,67 | 56,12 | 399 | 120 | 31.6 | 45.5 |
| 9 | 73,33 | 26,24 | 352 | 51 | 26.2 | 26.5 |
| 10 | 25,36 | 53,2 | 205 | 36 | 23.1 | 23.1 |
| 11 | 59,45 | 77,76 | 159 | 37 | 20.7 | 20.7 |
| 12 | 38,33 | 48,28 | 114 | 34 | 11.7 | 11.7 |
| 13 | 24,13 | 52,5 | 359 | 105 | 19.6 | 26.7 |
| 14 | 77,26 | 65,24 | 20 | 13 | 6.4 | 6.4 |
| 15 | 28,30 | 53,37 | 316 | 105 | 18.7 | 25.7 |
| 16 | 80,18 | 36,19 | 82 | 45 | 22.2 | 22.2 |
| 17 | 20,16 | 62,10 | 297 | 82 | 23.5 | 28.3 |
| 18 | 65,11 | 20,6 | 484 | 104 | 26.8 | 32.4 |
| 19 | 63,10 | 30,6 | 463 | 96 | 22.2 | 27.4 |
| 20 | 59,32 | 78,16 | 23 | 20 | 12.8 | 12.8 |
| **Total** | | | **5,426** | **1,270** | **412.2** | **468.2** |

- GBFS expanded **77% fewer nodes** (1,270 vs 5,426) and ran **4.7x faster** (2.62 ms vs 12.27 ms in total).
- Its paths were **13.6% longer** in total: equal to A*'s on 10 of 20 pairs, longer on the other 10. The longest detours (pairs 8, 13, 15) are routes that must go round the central walls through a doorway: greedy heads straight for the goal, meets the wall, then follows it.
- Worst case for A*, pair 1 (1,326 nodes, a cross-level route through two doorways): GBFS needed 146. For a chaser replanning every 0.5 s, that is the trade the Tracker is designed to make.

### Noise propagation (Tracker hearing, S1)

`NoisePropagation.Propagate` on the greybox level grid (83 x 83 cells), one noise from the middle of the Assembly Floor, doors 3 and 4 closed. Average of 50 runs after one warm-up, Unity editor (Mono), 2026-10-04.

| Source | L0 | Audible radius | Cells reached | Time per noise |
| --- | ---: | ---: | ---: | ---: |
| Blaster shot | 100 | 22.5 m | 1,980 | 5.37 ms |
| Relay alarm / core explosion | 90 | 20 m | 1,717 | 4.82 ms |
| Wind-up toy landing | 70 | 15 m | 1,441 | 3.97 ms |
| Door slam / terminal beep | 60 | 12.5 m | 1,353 | 3.71 ms |
| Box impact | 50 | 10 m | 1,023 | 2.85 ms |
| Pressure plate click | 40 | 7.5 m | 525 | 1.36 ms |
| Running footsteps | 25 | 3.75 m | 128 | 0.33 ms |

- The cost scales with the audible area, not the level size: the threshold bound stops the spread, so footsteps touch 128 cells while a shot touches 1,980 of the 5,318 walkable cells. Quiet noises (footsteps every few frames) stay cheap; loud ones are rare events.
- **0 bytes** allocated over 100 blaster-shot propagations (stamped arrays and a reused heap).
- Closed-door check at door 4: 47 behind the closed door, 82 with it open (difference exactly 35).
- Lab-machine and player-build timings still to record; the editor's per-cell cost (about 2.7 µs) matches A*'s on the same grid (about 2.3 µs per expanded node).

## Stress test (`Test_FourAgentsStress`)

| Date | Build | Avg FPS | Worst frame (ms) | AI ms / frame | GC Alloc in searches |
| --- | --- | --- | --- | --- | --- |
