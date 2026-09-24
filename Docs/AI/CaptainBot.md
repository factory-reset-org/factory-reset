# Captain Bot

Owner: S4 · Architecture: Goal prediction + Dijkstra fields + A* intercept

## Role in gameplay

The Captain is the boss of the Control Room area. It does not chase the player. It watches where the player is going, predicts which objective they are heading for, and gets to a point on their route first.

- **Candidate goals:** the active control switches (up to 3), the Control Room door, and battery pickups when the player's ammo is below 30%.
- **Threat:** the player meets the Captain in front of them, at a chokepoint, rather than being followed from behind.
- **Pacing:** the Captain only commits to an ambush when it is confident. Otherwise it keeps its distance and observes, so it never feels like it is cheating.

## Creative hook

**The gameplay problem:** a boss that chases is predictable. The player always knows where it is (behind them) and can beat it by simply outrunning it or looping around cover.

**The Captain's solution: it predicts where you are going, not where you are.** Every other enemy reacts to the player's position. The Captain reasons about the player's *intention*:

1. It compares the route the player has taken over the last 5 seconds with the shortest route to each goal. A player who stays on the shortest route to Switch 2 is probably going to Switch 2.
2. It works out which chokepoints on that predicted route it can reach at least 1 second before the player.
3. It walks there and waits, facing the way the player will come.

**Why it is surprising to play against:** the player expects the enemy behind them and instead finds it waiting in front of them. Its tactics also change with its confidence: it shadows the player while unsure, and commits to an ambush once the prediction is strong.

**Counter-play:** the prediction is based on shortest routes, so a player who takes an unexpected route lowers the Captain's confidence and can slip past it. The Captain is beatable by a player who realises it is reading their movement.

**Team play:** the Captain publishes its predicted goal to the shared blackboard at 2 Hz. The Saboteur uses it to measure which door closure forces the longest detour (falling back to the nearest active switch if no prediction exists). Nice-to-have: the Tracker investigates the predicted goal when it has nothing to hear.

## Architecture

The Captain is a finite-state machine built on the shared FSM framework: each state is an object with `Enter`, `Tick` and `Exit`, and transitions are data (condition + priority + target), not a `switch` statement. Every tick the machine takes the highest-priority transition whose condition is true.

**Confidence** below means the probability of the most likely goal, `P(g*)`, from goal inference (see Maths to defend). It is recomputed at 2 Hz.

### States

| State | What the Captain does |
| --- | --- |
| **Observe** | Prediction is too uncertain to commit. Keeps its distance from the player and stays out of sight while the prediction updates. |
| **Intercept** | Confident about `g*`. Picks the first chokepoint on the player's predicted route it can reach at least 1 s before them, and walks there with A*. If no cell qualifies, it heads to `g*` itself to defend it. |
| **Ambush** | At the intercept cell. Stands still, facing the direction the player will arrive from. |
| **Engage** | Player is in view within 10 m. Faces the player and fires hitscan shots, each with a 0.3 s wind-up telegraph. |
| **Reassess** | Something invalidated the plan. Discards the current intercept cell, re-runs goal inference immediately, then hands over to Observe or Intercept. Lasts one decision tick. |
| **Stunned** | Hit points reached 0. Falls apart for the stun duration, releases its reserved cell, then goes to Reassess. |

### Transitions

Higher priority wins when several conditions are true on the same tick.

| From | To | Condition | Priority |
| --- | --- | --- | --- |
| Any | Stunned | Hit points reach 0 | 100 |
| Stunned | Reassess | Stun timer ends | 90 |
| Observe, Intercept, Ambush | Engage | Player visible within 10 m | 80 |
| Engage | Reassess | Player no longer visible | 70 |
| Intercept, Ambush | Reassess | `g*` changes, confidence drops below 0.5, the player reaches `g*`, or the intercept cell becomes blocked or no longer satisfies the 1 s margin | 60 |
| Intercept | Ambush | Captain reaches the intercept cell | 50 |
| Reassess | Intercept | Confidence ≥ 0.5 | 40 |
| Reassess | Observe | Confidence < 0.5 | 30 |
| Observe | Intercept | Confidence ≥ 0.5 | 20 |

```text
            confidence ≥ 0.5              reached cell
  Observe ───────────────────▶ Intercept ─────────────▶ Ambush
     ▲                            │  ▲                    │
     │ confidence < 0.5           │  │ confidence ≥ 0.5   │ goal changed /
     │                            ▼  │                    │ confidence dropped
     └──────────────────────── Reassess ◀─────────────────┘
                                  ▲
         player lost from view    │        stun ends
  Engage ─────────────────────────┴──────────────────── Stunned
     ▲
     └── any of Observe / Intercept / Ambush: player visible within 10 m
```

## Why this architecture over the alternatives

| Alternative | Why not |
| --- | --- |

## Maths to defend

### Goal inference

For each candidate goal `g`, a Dijkstra field gives `C(x → g)`: the shortest path cost from any cell `x` to `g`, looked up in O(1). Costs are in grid units (1 per orthogonal step, √2 per diagonal) and converted to metres by multiplying by the 0.5 m cell size.

Every 0.5 s (2 Hz) the Captain scores each goal:

```text
D(g)  = C(s → x) + C(x → g) − C(s → g)          detour cost, in metres
w(g)  = P(g) · exp(−β · D(g))                    unnormalised likelihood
P(g | observed) = w(g) / Σ w(g')                 normalise so the goals sum to 1
```

- `s` is the player's cell 5 s ago and `x` is the player's current cell.
- `D(g)` measures how far the player's actual movement strays from the shortest route to `g`. It is **0 when the player is on an optimal route to `g`** and grows as they move away from it.
- `D(g) ≥ 0` always, because the field costs are true shortest paths: going via `x` can never be cheaper than the direct route (triangle inequality).
- **Confidence** is the largest posterior, `P(g*)`, where `g*` is the most likely goal.

### Why β = 0.5 per metre

β sets how strongly a detour counts against a goal. A detour of `D` metres multiplies that goal's likelihood by `e^(−0.5·D)`:

| Detour D | Likelihood multiplier |
| --- | --- |
| 1 m (stepping around a crate) | 0.61 |
| 2 m | 0.37 |
| 4 m | 0.14 |
| 6 m | 0.05 |

- **Too high** (e.g. β = 2): a 1 m sidestep around an obstacle would cut a goal's likelihood to 0.14, so the Captain would flip its prediction on every small dodge.
- **Too low** (e.g. β = 0.1): even a 6 m detour only cuts a goal's likelihood to 0.55, so the Captain would almost never become confident.
- **β = 0.5** ignores small corrections but responds decisively to real route choices. With two goals and equal priors, `P(A) > 0.8` needs `e^(−β·ΔD) < 0.25`, i.e. the player has to stray about **2.8 m** further from B's shortest route than from A's (`ln 4 / 0.5 ≈ 2.77`).

**Worked example:** three active switches with equal priors. The player is on the shortest route to A (`D = 0`), 4 m off the route to B, and 6 m off the route to C.

```text
w(A) = 1.000   w(B) = e^−2 = 0.135   w(C) = e^−3 = 0.050
P(A) = 1.000 / 1.185 = 0.84   P(B) = 0.11   P(C) = 0.04
```

Confidence is 0.84 ≥ 0.5, so the Captain moves from Observe to Intercept.

### Why a 5 s window

- **Shorter:** a single dodge or a moment of strafing would dominate the prediction.
- **Longer:** the Captain would be slow to notice a genuine change of plan, and an old part of the route would drag the prediction towards a goal the player has abandoned.
- **Standing still:** if `s = x`, every `D(g) = 0`, so the posterior equals the prior. The prediction does not change and the Captain does not replan.

### Priors

- Active switches share the prior equally. Restored switches are removed from the candidate set.
- The Control Room door's prior rises as switches are restored, because the player can only win by reaching it after the switches.
- Batteries join the candidate set only while the player's ammo is below 30%.
- A goal with no reachable path (infinite field cost) is left out of the candidate set.

### Numerical safety

Before taking the exponent, subtract the smallest `D(g)` from every goal's detour. This does not change the normalised result, but it keeps `exp` away from underflow when every goal has a large detour.

### Intercept point selection

Once confidence ≥ 0.5, the Captain picks where to wait.

1. **Predict the player's route.** Starting at the player's cell `x`, repeatedly step to the neighbour with the lowest `C(· → g*)`. Because the field holds true shortest-path costs, this walks the player's optimal route to `g*`. Call the cells on it `r_1, r_2, …, g*`.
2. **Player arrival time** at each route cell:
   ```text
   t_player(i) = [C(x → g*) − C(r_i → g*)] / v_player
   ```
   The bracket is the distance the player still has to walk to reach `r_i`. It comes from two O(1) field lookups, so no extra search is needed.
3. **Captain arrival time** at each route cell, from a Dijkstra field rooted at the Captain:
   ```text
   t_captain(i) = C(captain → r_i) / v_captain
   ```
4. **Choose the first chokepoint that satisfies**
   ```text
   t_captain(i) + 1.0 s ≤ t_player(i)
   ```
   A chokepoint is a doorway cell or a cell with at most 4 walkable neighbours (precomputed on the grid), so the player cannot simply walk around the Captain. If no chokepoint qualifies, take the first route cell that does. If no cell qualifies at all, the player is too close to `g*`: the Captain heads to `g*` and defends it.
5. **Walk there** with A* through the path scheduler.

**Why the first qualifying chokepoint:** it is the earliest point where the Captain can be waiting, so it meets the player furthest from their goal and gives the player the least time to notice and reroute.

**Why a 1.0 s margin:** the Captain needs time to stop, turn to face the approach and settle before the player arrives. The margin also absorbs small prediction errors in the player's speed. Without it, the Captain would often arrive at the same moment as the player, which looks like chasing rather than ambushing.

**Why `v_player` is the player's sprint speed:** it is the worst case. If the Captain can beat a sprinting player to a cell, it can also beat a walking one, so the inequality never over-promises.

### Why Dijkstra fields and A* together

| Question the Captain asks | Search needed | Tool |
| --- | --- | --- |
| How far is every cell from each goal? (goal inference, predicted route) | One-to-all | Dijkstra field per goal |
| How soon can I reach every cell on the predicted route? | One-to-all | Dijkstra field from the Captain |
| What path do I actually walk to the chosen cell? | One-to-one | A* |

- **Dijkstra (uniform-cost search)** expands cells in order of cost from its source and gives the exact cost to every reachable cell. One run answers the arrival-time question for the whole route at once. Running A* separately for every route cell would repeat most of the same work.
- **A*** is the efficient choice when there is a single destination. The octile heuristic is admissible and consistent on the 8-connected grid, so A* returns an optimal path while expanding far fewer cells than Dijkstra. It also goes through the same `IPathfinder` and path scheduler as the other agents, so the Captain's movement follows the shared frame budget and replanning rules.

**Cost:** each field is a bounded Dijkstra run, O(V log V) with the binary heap. Fields are only recomputed when `OnGraphChanged` reports a changed cell inside them, not every tick. Choosing the intercept cell is then O(L) for a route of L cells, because every step is a field lookup.

## Edge cases

| Case | Handling | Test |
| --- | --- | --- |

## Tests
<!-- EditMode tests and what each proves -->

## Measured results
<!-- Numbers from AIPerformanceLog.md -->
