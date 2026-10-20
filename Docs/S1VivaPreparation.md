# S1 Viva Preparation

These answers describe the implementation and evidence currently on `develop`. Exact performance figures come from [AIPerformanceLog.md](AIPerformanceLog.md); art evidence comes from [OptimisationLog.md](OptimisationLog.md).

## Tracker AI

**What is the Tracker's role?**  
A wind-up scout that hunts by ear. It patrols, investigates noise, chases visible players, searches a last-known position, can be distracted by a repeating lure, rewinds when its spring empties, and can be stunned.

**Why is it model-based?**  
The current percept is not enough to drive it. The brain retains its FSM state, last-known player position, remembered noises, current route, blocking door and wind-up energy.

**What is the Tracker's PEAS description?**  
Performance is finding and pressuring the player correctly at low cost. Its environment is the changing four-room factory. Its actuators are movement/look intents, pounce, alert and rewind/stun intent. Its sensors are grid sight, propagated hearing, graph changes, player observations, stun state and time.

## GBFS and A*

**How does GBFS choose a node?**  
It uses `f(n) = h(n)` only. The heuristic is octile distance on the eight-connected grid, with straight cost 1 and diagonal cost `sqrt(2)`.

**Is GBFS complete and optimal here?**  
With a closed set it is complete on the finite grid: it either reaches the goal or exhausts the frontier, and never expands a cell twice. It is not optimal because it ignores accumulated path cost `g(n)`.

**Why use it instead of A* for Tracker?**  
It suits eager pursuit and searches cheaply. Over the same 20 recorded pairs, GBFS expanded 1,659 nodes versus 7,080 for A*, and took 4.10 ms versus 17.98 ms. Its paths totalled 511.6 m versus 439.8 m, 16.3% longer. These are editor results for that pair set, not a universal guarantee.

## Sound propagation and memory

**Why is hearing grid-based?**  
Straight-line distance cannot represent walls, routes around corners or door attenuation. Bounded Dijkstra computes loss along the acoustic grid: `L = L0 - 4 * pathDistance - sum(closed-door penalties)`, with a penalty of 35 per closed door crossed and hearing threshold 10.

**How can a closed door block movement but pass sound?**  
`GridNode` stores door identity/state separately from ordinary blockers. Movement neighbour queries reject closed-door cells; acoustic queries allow them and `NoisePropagation` adds the penalty.

**How does the Tracker choose among noises?**  
`NoiseMemory` scores each source with `level * exp(-0.3 * age)`, so recent loud noises win. It stores one entry per source, identifies repeating lure sources, marks investigated sources handled, decays old entries and uses a fixed capacity of eight.

## FSM and energy

**Why use a hierarchical, data-driven FSM?**  
Transitions are explicit data with stable priority order, while composite Calm and Hunting states group related leaf states. High-priority stun and energy transitions can interrupt lower-level behaviour without duplicating them in every state.

**What are the important lifecycle rules?**  
The shared machine lazily enters its initial state, selects at most one highest-priority transition per tick, performs `Exit -> update Current -> Enter`, and then ticks the current state. Equal priorities retain declaration order.

**How does wind-up energy affect play?**  
Energy drains at 10/s in Chase and 2/s otherwise. At zero the Tracker rewinds for 3 seconds, is stationary and takes double hit cost, then resumes its previous behaviour with restored energy.

## GridGraph

**What coordinate contract does the grid use?**  
It is a flat XZ grid with 0.5 m cells and an explicit origin. World-to-cell uses that origin; cell-to-world returns the cell centre.

**How is legal movement represented?**  
The graph is eight-connected and forbids diagonal corner cutting. Traversability requires a walkable cell, zero ordinary blockers and no closed associated door.

**How do dynamic changes stay consistent?**  
Multi-cell edits use one batch, producing one version increment and one `GridChange` with complete changed/affected cell sets. Search code can detect stale results from the version, and non-allocating neighbour/index APIs support repeated searches.

## Chapter progression

**What is the chapter sequence?**  
Assembly Floor, Painting Room, Storage Vault and Control Room. Chapters 1–3 complete their tasks, unseal their control switch, wait for restoration and the configured transition/cutscene, then activate the next chapter. Chapter 4 ends through its console and has no fake fourth switch.

**How are objectives shared without coupling agents to Journey?**  
`ChapterManager` publishes world-space `ObjectiveTargetInfo` values through `ObjectiveEvents`. Runtime converts them to grid-space `ObjectiveTarget` entries in `WorldBlackboard`; Captain and Saboteur consume the blackboard rather than referencing Journey.

**How are early pickups handled?**  
The flow can record permitted pickup completion before its chapter activates. Later non-pickup props remain inactive until their chapter is active, so authored progression is preserved.

## Optimisation and evidence

**What Tracker performance evidence exists?**  
The 9 October isolated run measured one Tracker at 0.5 µs median and 8.2 µs p99 per tick; seven Trackers together were 0.0593 ms per frame at p99. In the 16 October seven-agent re-run, Tracker p99 was 0.032 and 0.026 ms across two runs. These are Unity Editor/Mono measurements with the limitations recorded in the performance log.

**What did the photographic texture audit prove?**  
The four-room pass contains 24 source maps in eight 1,024 x 1,024 sets. Their import metadata enables mipmaps, disables CPU readability, uses a 1,024 default cap and automatic compression. Runtime GPU memory and before/after frame cost were not measured, so the evidence records configuration and scope rather than claiming a performance improvement.

**How should you defend an optimisation claim?**  
Name the controlled before/after case, metric, machine/build context and limitations. If only asset settings or counts are known, state those facts and mark runtime memory or timing as not measured.
