# Guard Bot

Owner: S2 · Architecture: Tactical A* + cover evaluation

## Role in gameplay

The Guard is a defensive shooter that holds cover, peeks out to shoot, and relocates the moment its cover stops protecting it. Unlike the Tracker (which hunts by sound) or the Saboteur (which disrupts the player indirectly), the Guard's whole identity is playing safe and forcing the player to work to dislodge it.

- **Cover, not chase:** the Guard never runs straight at the player. It only ever moves between positions it has evaluated as safe.
- **Threat:** the player has to break line of sight, flank around a peek angle, or force a relocation to get a clean shot, rather than simply out-running an enemy.
- **Pacing:** the Guard becomes more aggressive as the player's battery runs low, so a fight that starts cautious gets tenser as the player's options shrink.

## Creative hook

**The gameplay problem:** a cover-shooter that always hides behind the same static geometry is predictable, and a shooter that never adapts to the player's state feels the same in every encounter.

**The Guard's solution: it treats the physics world as part of its own decision space, not just an obstacle to route around.**

1. **Player-built barricades become its cover.** Any pushable box the player settles into place is a new cover candidate the instant it stops moving. A player who builds a wall to slow the Guard down may unintentionally hand it a fortified position instead.
2. **It adapts to the player's weapon state.** The Guard reads the player's battery fraction off the blackboard and changes its ideal engagement distance and peek rhythm accordingly, so a player who is winning the ammo war finds the Guard turtling up, while a player who is running low finds it pressing the advantage.

| Player battery / state | Guard tactic |
| --- | --- |
| > 50% | `d_ideal = 10 m`, hold cover, peek only after the player stops firing for 1.5 s |
| 25 to 50% | `d_ideal = 7 m`, normal peek rhythm |
| < 25% or reloading | `d_ideal = 4 m`, advance to closer cover, shorter peek interval |

**Counter-play:** the cover scoring always prefers positions the Guard can reach cheaply, so a player who keeps pushing boxes to block the Guard's cheap routes, or who manages their battery conservatively so the Guard never presses in, can keep it passive and predictable.

**Team play:** the Guard reserves its chosen cover cell on the shared blackboard so no other agent picks the same one, and releases it immediately on relocation or stun.

## Architecture

The Guard is a finite-state machine built on the shared FSM framework: each state is an object with `Enter`, `Tick` and `Exit`, and transitions are data (condition + priority + target), not a `switch` statement.

Cover candidate generation runs every 1 s, or immediately when the player's cell changes by more than 2 m, or when a box settles nearby:

1. Collect traversable cells within 15 m that are adjacent to a static obstacle or a settled pushable box.
2. Protection test: Linecast from the player's eye to the candidate at 0.5 m and 1.2 m height. Full cover if both are blocked, half cover if only the low one is, otherwise discard the candidate.
3. Peek test: at least one neighbour cell from which the player is visible.

Selection ranks all candidates by score using octile distance as a cheap estimate of path cost, runs tactical A* on the top 3, recomputes the score with the true path cost, picks the best, and reserves that cell on the blackboard.

### States

| State | What the Guard does |
| --- | --- |
| **TakeCover** | Walking to the reserved cover cell via tactical A*. Fires opportunistically if the player is visible along the way. |
| **InCover** | Settled at cover, facing the player's last known position, waiting out a short hold timer before peeking. |
| **PeekAndShoot** | Moved to the cover's peek cell, fires hitscan shots with a 0.3 s wind-up telegraph while the peek timer runs. |
| **Relocate** | Current cover is exposed or flanked. Immediately re-runs cover scoring and moves to the next best candidate. |
| **Advance** | No valid cover exists, or the player's battery is below 25%. Presses toward the player's last known position. |
| **Retreat** | No valid cover and advancing is not safe either. Falls back to the reachable cell that maximises distance from the player with zero exposure. |
| **Stunned** | Hit points reached 0. Falls apart for the stun duration and releases its cover reservation. |

### Transitions

Higher priority wins when several conditions are true on the same tick.

| From | To | Condition | Priority |
| --- | --- | --- | --- |
| Any | Stunned | Hit points reach 0 | 100 |
| Stunned | TakeCover | Stun timer ends | 90 |
| TakeCover | InCover | Reaches the reserved cover cell | 80 |
| InCover | PeekAndShoot | Hold timer elapses (1.3 s, or later once the player stops firing, per the battery table) | 70 |
| InCover, PeekAndShoot | Relocate | Current cover becomes exposed (player gains line of sight to it) | 60 |
| PeekAndShoot | InCover | Peek timer elapses | 50 |
| Relocate | TakeCover | A new cover cell is chosen | 40 |
| TakeCover, InCover, PeekAndShoot, Relocate | Advance | No valid cover exists, or the player's battery drops below 25% / they are reloading | 30 |
| Advance, Relocate | Retreat | No valid cover and no safe advance position | 20 |

```text
                    reaches cover                 hold timer
   TakeCover ─────────────────────▶ InCover ─────────────────────▶ PeekAndShoot
      ▲   ▲                            │  ▲                              │
      │   │ new cover chosen           │  │ peek timer elapses           │ cover exposed
      │   └──────────── Relocate ◀─────┘  └──────────────────────────────┘
      │                    │  ▲
      │  no valid cover /  │  │ no valid cover / battery < 25%
      │  low battery       ▼  │
      └───────────────── Advance ──────────────────▶ Retreat
                                  no safe advance position
```

## Why this architecture over the alternatives

The gameplay need is a shooter that plays it safe without being static: it must genuinely evaluate danger, and the player must be able to out-manoeuvre it by controlling sightlines and the physics world. Each alternative below fails one of those two requirements.

| Alternative | What it would do | Why not |
| --- | --- | --- |
| Unity's built-in `NavMeshAgent` | Handle pathfinding and avoidance automatically | Its cost model is opaque, it cannot be biased away from cells the player can see, which is the entire point of the tactical cost model. The project also standardises on one custom grid and search stack shared by all four agents, not a per-agent black box. |
| Greedy Best-First Search (like the Tracker) | Route greedily toward the target using only the heuristic | Not guaranteed optimal. A greedy route could cut through an exposed cell that looks close, directly undermining the point of a cost model built to avoid exposure. |
| Plain Dijkstra (no heuristic) | Expand uniformly in all directions until the goal is found | Correct, but expands far more nodes than A* with no goal bias, wasteful against the shared 2 ms/frame search budget split across four agents. |
| Fixed, hand-placed cover waypoints | Pre-authored cover spots the designer places in the level | Cannot react to boxes the player pushes into new positions, which would remove the "player-built cover" creative hook entirely. |
| Behaviour tree | Hand-authored priority tree of checks and actions | Organises behaviour fine, but does not solve the actual hard problem, scoring which of many candidate cells is safest. The cover-scoring formula would still be needed inside it, so the tree adds structure without adding the missing capability. |
| Utility AI (like the Saboteur) | Score every possible action on one 0 to 1 scale | Suited to many competing, qualitatively different actions. The Guard has one core decision loop (find safe cover, peek, shoot, relocate), which a small number of states with data-driven transitions expresses more simply and can be printed as a table for the viva. |

**What the chosen design gives instead:**

- **Tactical awareness:** routes and cover choices explicitly avoid cells visible to the player, not just walls.
- **Explainability:** every weight (0.40 / 0.25 / 0.20 / 0.15) and `lambda = 3` has a stated reason, and cover candidates with their scores can be shown live in the debug overlay.
- **Reuse:** the same `AStarSearch` and `IPathfinder` / `ICostModel` contracts the rest of the project shares, only the cost model differs.
- **Adaptivity:** the same architecture reacts to the player's battery state and to boxes the player pushes into place, without adding new states.

## Maths to defend

### Cover score

Weights sum to 1 so `S(c)` is comparable across frames:

```text
S(c) = 0.40*P(c) + 0.25*R(c) + 0.20*(1 - pathCost(c)/maxCost) + 0.15*F(c)
R(c) = clamp01(1 - |dist(c, player) - d_ideal| / d_ideal)
```

Protection (`P`) is weighted highest because a Guard standing in the open fails its entire role. Range (`R`) is next because a Guard that is too close or too far cannot function. Travel cost matters but should not override safety. Peek ability (`F`) is a tiebreaker.

### Tactical A* cost model

`lambda = 3` in a ScriptableObject:

```text
stepCost(n -> m) = base(n, m) * (1 + lambda * exposure(m))
```

`exposure(m)` is 1 if the player can currently see cell `m` (cached, invalidated when the player changes cell), else 0.

### Why lambda = 3

`lambda` sets how strongly exposure penalises a step. A step through an exposed cell costs `(1 + lambda)` times a hidden one:

| lambda | Exposed step cost multiplier | Effect |
| --- | --- | --- |
| 0.5 | 1.5x | Barely a deterrent; the Guard would walk through the open almost as readily as through cover. |
| 3 (chosen) | 4x | The Guard accepts a route up to 4x longer to stay hidden, but still crosses open ground if the hidden alternative is much longer. |
| 10 | 11x | The Guard would take wildly long detours to avoid even brief exposure, reading as erratic rather than cautious. |

**Worked example:** a direct route crosses 2 exposed cells and 2 hidden cells; a hidden alternative is 3 cells longer but fully hidden.

```text
direct route: 2*1 (hidden) + 2*4 (exposed) = 10
hidden route: 7*1 (hidden)                 = 7
```

The hidden route wins despite being longer, because 7 is less than 10.

### Admissibility and consistency

Octile distance assumes every step costs its base cost. The exposure penalty only ever increases a step's cost (`1 + lambda*exposure >= 1`), never decreases it, so the heuristic never overestimates the true cost, it stays admissible. Consistency also holds because the base costs already satisfy the triangle inequality and real costs are always `>= base cost`. Together this means A* still expands each node at most once and remains optimal with respect to the tactical costs, not just plain distance.

### Why these battery thresholds

- **`d_ideal = 10 m` above 50% battery:** far enough to stay outside the blaster's most reliable range while still able to peek-shoot, so a well-supplied player faces a patient, hard-to-pressure Guard.
- **`d_ideal = 7 m` between 25 and 50%:** the Guard starts closing the gap as the fight becomes more even.
- **`d_ideal = 4 m` below 25% or while reloading:** an aggressive push while the player is at their most vulnerable, so running low on ammo has a real, felt cost.

## Edge cases

| Case | Handling | Test |
| --- | --- | --- |
| No valid cover found | Retreat to the reachable cell that maximises distance from the player with zero exposure; if none exists, engage from the current position | `Guard_NoCoverAvailable_FallsBackToRetreat` |
| Player flanks the chosen cover | Immediate re-evaluation, not waiting for the hold timer | `Guard_ExposedMidCover_TransitionsToRelocate` |
| Chosen cover already reserved by another agent | Take the next-best candidate | `Cover_ReservedByOtherAgent_SkipsToNextBest` |
| Target cover becomes blocked by a box | Take the next-best candidate; the box is added as a new cover candidate | `Cover_TargetBlockedByBox_ReselectsAndAddsBoxAsCandidate` |
| Player unreachable | Hold current cover, keep peeking | `Guard_PlayerUnreachable_HoldsCoverWithoutFreezing` |
| Stunned | Releases its cover reservation so another agent can use it | `Guard_OnStunned_ReleasesCoverReservation` |

## Tests

EditMode tests run without a scene, which also proves the brain is decoupled from Unity objects. Each one uses a small hand-made grid or a fake `IVisibility`.

**AStarSearch**

| Test | What it proves |
| --- | --- |
| `AStar_KnownGrid_ReturnsOptimalPath` | Matches a hand-worked answer on a small grid |
| `AStar_UnreachableGoal_ReturnsNotFound` | `Found = false` rather than an infinite search |
| `AStar_CostMatchesDijkstra_OnRandomGrids` | Optimality holds generally, not just on one example |

**TacticalCostModel**

| Test | What it proves |
| --- | --- |
| `TacticalCost_HiddenRouteWithinLambdaBound_IsPreferred` | The exposure penalty actually changes route choice |
| `TacticalCost_NeverBelowBaseOctileStep_StaysAdmissible` | The admissibility argument holds in code, not just on paper |

**Cover scoring**

| Test | What it proves |
| --- | --- |
| `Cover_FullCoverRanksAboveHalfCover_AtEqualRange` | Protection weighting behaves as designed |
| `Cover_BoxSettles_BecomesNewCandidate` | The player-built-cover creative hook actually works |

**Brain and states**

| Test | What it proves |
| --- | --- |
| `Guard_Transitions_PickHighestPriorityValidOne` | The FSM is data-driven, not if/else |
| `Guard_BatteryBelow25_ReducesIdealDistanceAndAdvances` | The battery-adaptive creative hook actually works |

Edge-case tests are listed in the table above.

## Measured results
<!-- Numbers from AIPerformanceLog.md: lambda tuning notes, A* vs Dijkstra verification, replans-per-box-push counts. -->
