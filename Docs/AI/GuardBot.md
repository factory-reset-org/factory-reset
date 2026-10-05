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
2. **It adapts to the player's weapon state.** The Guard reads the player's battery off the blackboard (the blaster's charge, `AmmoFraction` on the player snapshot) and changes its ideal engagement distance and peek rhythm accordingly, so a player who is winning the ammo war finds the Guard turtling up, while a player who is running low finds it pressing the advantage.

| Player battery / state | Guard tactic |
| --- | --- |
| Overcharge active | `d_ideal = 12 m`, full cover only (`P = 1.0`), no peeking until it ends. Reads `OverchargeTimeLeft` from the blackboard player |
| > 50% | `d_ideal = 10 m`, hold cover, peek only after the player stops firing for 1.5 s |
| 25 to 50% | `d_ideal = 7 m`, normal peek rhythm |
| < 25% or reloading | `d_ideal = 4 m`, advance to closer cover, shorter peek interval |

**Counter-play:** the cover scoring always prefers positions the Guard can reach cheaply, so a player who keeps pushing boxes to block the Guard's cheap routes, or who manages their battery conservatively so the Guard never presses in, can keep it passive and predictable.

**Team play:** the Guard reserves its chosen cover cell on the shared blackboard so no other agent picks the same one, and releases it immediately on relocation or stun.

## Architecture

The Guard is a finite-state machine built on the shared FSM framework: each state is an object with `Enter`, `Tick` and `Exit`, and transitions are data (condition + priority + target), not a `switch` statement.

Cover candidate generation runs every 1 s, or immediately when the player's cell changes by more than 2 m, or when a box settles nearby:

1. Collect traversable cells within 15 m that are adjacent to a static obstacle or a settled pushable box.
2. Protection test: a line-of-sight query from the player's eye to the candidate at 0.5 m and 1.2 m height, answered through `ICoverVisibility`, which Runtime implements with a physics linecast. Full cover if both are blocked, half cover if only the low one is, otherwise discard the candidate.
3. Peek test: at least one neighbour cell from which the player is visible.

Selection ranks all candidates by score using octile distance as a cheap estimate of path cost, runs tactical A* on the top 3, recomputes the score with the true path cost, picks the best, and reserves that cell on the blackboard (`WorldBlackboard.Reservations`).

Two rules keep the behaviour stable:

- **Hysteresis:** the Guard keeps its current cover unless that cover stopped being valid (exposed, blocked, or filtered out by the battery tier) or a new candidate scores more than 0.1 higher. Without this it would hop between two near-equal spots every second.
- **Engagement range:** the Guard is alerted when a living player comes within 20 m and stays alerted until they are more than 30 m away, so it follows the player out of its room but does not cross the whole factory at the start of the game. Outside that range it patrols. These two distances are tuning values chosen for the greybox level.

### Runtime contract

These rules come from the shared brain and body contract in `Docs/DesignDoc.md`. The brain reads the world through `AgentContext` and returns an `AgentIntent`, and it never touches a GameObject.

- **Stun:** the controller stops the body and does not tick the brain until it reboots. `OnStunned(duration)` is information only, so the Guard has no stun timer of its own. On the first tick after reboot it plans a fresh route. It releases its cover reservation at the moment of the stun.
- **Arrival:** a waypoint counts as reached when the distance to the last waypoint is under 0.5 m. Never compare `ctx.Cell` with the last cell, because the body stops within 0.3 m of a waypoint, which is more than half a cell.
- **Time:** use `ctx.Time`, which is game time and stops during cutscenes and pause. Never use `UnityEngine.Time`.
- **Destruction:** `OnDestroyed()` releases the cover reservation, so a dead Guard never blocks another agent.
- **Spawning:** the Guard's case in `AgentSpawner.CreateBrain(point, in BrainSetup setup)` receives the grid, the shared A* pathfinder, its identity id, the blackboard and the patrol points through `setup`. Only that case is edited, and it is edited by S2.
- **Shooting:** the Guard outputs `AgentAction.Shoot` with `LookTarget` set. The controller applies the 0.3 s telegraph, the hitscan and the damage through `PlayerState.Current.TakeDamage`.
- **Player:** read the player from `setup.Blackboard.Player`. If `IsKnown` is false or `IsAlive` is false, patrol or hold position. Never throw.
- **Replanning:** replan only when `OnGraphChanged` touches the current path or the reserved cover cell.
- **Journey:** the home region is the Painting Room during Chapter 2. The Guard follows the player out of the room when alerted. The controller does not tick it during cutscenes or pause.

### States

| State | What the Guard does |
| --- | --- |
| **Patrol** | No living player within range. Walks its patrol points, or holds position if it has none. Holds no cover reservation. |
| **TakeCover** | Walking to the reserved cover cell via tactical A*. Fires opportunistically if the player is visible along the way. |
| **InCover** | Settled at cover, facing the player, waiting out the hold timer before peeking. |
| **PeekAndShoot** | Steps to the cover's peek cell and requests hitscan shots while the peek timer runs; the controller adds the 0.3 s wind-up telegraph. Does not peek while overcharge is active. |
| **Relocate** | The cover choice changed (exposed, blocked, lost, or beaten by a better one). Lasts one tick: the table then picks TakeCover, Advance or Retreat from the fresh choice. |
| **Advance** | The player's battery is below 25% or they are reloading. Pushes to the closer cover chosen with `d_ideal = 4 m`, or straight at the player down to that range if no cover exists. |
| **Retreat** | No valid cover. Falls back to the reachable hidden cell farthest from the player; if every nearby cell is exposed it fights from where it stands. |
| **Stunned** | Hit points reached 0. The body falls apart, and the controller stops ticking the brain until it reboots (the Guard reassembles after 8 s, plan section 9.5). The cover reservation was released at the moment of the stun. The state itself lasts one tick after the reboot. |

### Transitions

Higher priority wins when several conditions are true on the same tick. This is the table in `GuardBrain`'s constructor, and `GuardBrain.DescribeTransitions()` prints it at runtime.

| Priority | From | To | Condition |
| --- | --- | --- | --- |
| 100 | Any | Stunned | Stunned (seen on the first tick after the reboot) |
| 95 | Any | Patrol | No player, player dead, or out of range |
| 90 | Stunned | Relocate | Reboot: plan a fresh route |
| 85 | Patrol | Relocate | A living player within alert range |
| 80 | TakeCover, Advance | InCover | Reaches the reserved cover cell (within 0.5 m) |
| 70 | InCover | PeekAndShoot | Hold timer elapses (1.3 s, or 0.7 s when aggressive) and peeking is allowed; above 50% battery that also needs the player to have stopped firing for 1.5 s |
| 65 | PeekAndShoot | InCover | Overcharge becomes active; no peeking until it ends |
| 60 | TakeCover, InCover, PeekAndShoot, Advance, Retreat | Relocate | The cover choice changed: exposed, blocked, lost, found, or beaten by a better one |
| 50 | PeekAndShoot | InCover | Peek timer elapses (2 s) |
| 40 | Relocate | TakeCover | A cover cell is reserved and the player is not low on battery |
| 30 | Relocate | Advance | Player battery below 25% or reloading |
| 20 | Relocate | Retreat | No valid cover |

```text
   Patrol --player in range--> Relocate <--cover changed-- (TakeCover, InCover, PeekAndShoot, Advance, Retreat)
                                  |
            +---------------------+----------------------+
     cover reserved          low battery              no cover
            v                     v                      v
        TakeCover              Advance                Retreat
            |                     |
            +--reaches cover--> InCover <--peek timer / overcharge-- PeekAndShoot
                                  +--------hold timer elapses------------^

   Any --stunned--> Stunned --reboot--> Relocate        Any --player gone--> Patrol
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

`lambda = 3`, passed to the model as a constructor parameter today. The ScriptableObject that exposes it in the Inspector is planned:

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

All of these are in `GuardBrainTests` unless the status says otherwise.

| Case | Handling | Test | Status |
| --- | --- | --- | --- |
| No valid cover found | Retreat to the reachable hidden cell farthest from the player; if none exists, engage from the current position | `Guard_NoCoverAvailable_FallsBackToRetreat` | Built |
| Player flanks the chosen cover | Immediate re-evaluation, not waiting for the hold timer | `Guard_ExposedMidCover_TransitionsToRelocate` | Built |
| Chosen cover already reserved by another agent | Take the next-best candidate | `Cover_ReservedByOtherAgent_SkipsToNextBest` | Built |
| Target cover becomes blocked by a box | Take the next-best candidate | `Cover_TargetBlockedByBox_Reselects` | Built |
| A box settles on the route | Replan only because the route crosses the changed cell; a change elsewhere keeps the path | `Guard_GraphChangeOnRoute_Replans`, `Guard_GraphChangeOffRoute_KeepsItsPath` | Built |
| Stunned | Releases its cover reservation so another agent can use it; the brain is not ticked until reboot | `Guard_OnStunned_ReleasesCoverReservation` | Built |
| Stun ends (reboot) | Plans a fresh route on the first tick after reboot; no stun timer of its own | `Guard_AfterReboot_PlansFreshRoute` | Built |
| Destroyed for good | Releases the cover reservation in `OnDestroyed` | `Guard_OnDestroyed_ReleasesCoverReservation` | Built |
| Waypoint arrival | Counts as reached within 0.5 m of the last waypoint, never by comparing cells | `Guard_ArrivalUsesDistanceNotCell` | Built |
| Overcharge starts mid-peek | Returns to cover at once | `Guard_OverchargeStartsMidPeek_ReturnsToCover` | Built |
| Overcharge ends mid-wait | Re-evaluates the cover with the battery row now in effect | `Guard_OverchargeEndsMidWait_ReevaluatesWithBatteryRow` | Built |
| Overcharge with only half cover nearby | Full cover only, so it has no cover and retreats | `Guard_OverchargeWithOnlyHalfCover_HasNoCover` | Built |
| No player, or player dead | Patrols or holds position, never throws | `Guard_NoPlayer_PatrolsOrHolds`, `Guard_DeadPlayer_PatrolsOrHolds` | Built |
| Player leaves or dies mid-fight | Returns to Patrol and releases its cover | `Guard_PlayerLost_ReturnsToPatrolAndReleasesCover` | Built |
| Player unreachable | Hold current cover, keep peeking | `Guard_PlayerUnreachable_HoldsCoverWithoutFreezing` | Planned |
| Cutscene or pause mid-route | Brain is not ticked; game time (`ctx.Time`) resumes from the same value. This is the controller's behaviour, so it needs a Runtime test | `Guard_CutsceneFreezesGameTime` | Planned |

## Tests

EditMode tests run without a scene, which also proves the brain is decoupled from Unity objects. Each one uses a small hand-made grid or a fake `ICoverVisibility`.

Status says whether a test exists in the repo today (**Built**) or is still to be written (**Planned**). Test names are the real ones in the test files.

**AStarSearch** (`AStarSearchTests`, built)

| Test | Status | What it proves |
| --- | --- | --- |
| `StraightLineOnOpenGridIsOptimal`, `RoutesAroundAWallWithoutCuttingCorners` | Built | Matches hand-worked optimal paths |
| `WalledOffGoalIsNotFound` | Built | `Found = false` rather than an infinite search |
| `CostMatchesReferenceDijkstraOnFiftyRandomGrids` | Built | Optimality holds generally, not just on one example |

**TacticalCostModel** (`TacticalCostModelTests`)

| Test | Status | What it proves |
| --- | --- | --- |
| `HiddenStepCostsTheSameAsTheBaseModel`, `ExposedStepCostsBaseTimesOnePlusLambda`, `DefaultLambdaIsThree`, `CustomLambdaIsRespected` | Built | The cost formula matches `base · (1 + lambda · exposure)` |
| `ExposureIsQueriedWithTheDestinationCell`, `NullExposureDelegateThrows` | Built | Exposure is read for the step's destination, and a missing exposure source is rejected |
| `TacticalCost_HiddenRouteWithinLambdaBound_IsPreferred` | Planned | The exposure penalty actually changes route choice |
| `TacticalCost_NeverBelowBaseOctileStep_StaysAdmissible` | Planned | The admissibility argument holds in code, not just on paper |

**Cover scoring** (`CoverEvaluatorTests`, built unless marked)

| Test | Status | What it proves |
| --- | --- | --- |
| `CellBehindAnObstacleIsFullCoverWhenBothHeightsAreBlocked`, `CellIsHalfCoverWhenOnlyTheLowHeightIsBlocked`, `CellIsDiscardedWhenOnlyTheChestHeightIsBlocked` | Built | Protection tiers follow the plan's rule |
| `OpenGridWithNoObstacleHasNoCandidates`, `ObstacleBeyondFifteenMetresProducesNoCandidates` | Built | Candidates need an obstacle next to them and must be within 15 m |
| `CanPeekWhenANeighbourSeesTheChest`, `CannotPeekWhenEveryNeighbourIsHiddenAtChestHeight` | Built | Peek ability follows chest visibility from neighbouring cells |
| `ScoreIsOneForFullCoverAtDesiredRangeWithNoTravelCostAndPeekable`, `FullCoverScoresHigherThanHalfCoverAtEqualRange`, `HigherPathCostLowersTheScore` | Built | The weights from the cover formula behave as designed |
| `NullGridIsRejected`, `NonPositiveDesiredRangeIsRejected` | Built | Bad inputs are rejected |
| `Cover_BoxSettles_BecomesNewCandidate` | Planned | The player-built-cover creative hook actually works |

**Brain and states** (`GuardBrainTests`, built)

| Test | Status | What it proves |
| --- | --- | --- |
| `Guard_Transitions_PickHighestPriorityValidOne`, `Guard_TransitionTable_IsDataAndPrintable` | Built | The FSM is data-driven, not if/else: the highest-priority valid rule wins, and the table can be printed |
| `Guard_PlayerInRange_TakesCoverAndReservesIt`, `Guard_PlayerBeyondAlertRange_StaysOnPatrol` | Built | Engagement range and the cover reservation |
| `Guard_HoldTimerElapses_PeeksAndShoots`, `Guard_PeekTimerElapses_ReturnsToCover` | Built | The peek-and-fire cycle, with shooting as an intent |
| `Guard_BatteryBelow25_ReducesIdealDistanceAndAdvances`, `Guard_PlayerReloading_Advances`, `Guard_BatteryTiers_SetTheIdealDistance`, `Guard_OverchargeActive_UsesTwelveMetresAndDoesNotPeek` | Built | The battery-adaptive creative hook actually works |

**Cell reservations** (`CellReservationsTests`, built): reserving, refusing a held cell, swapping cells, and release.

Edge-case tests are listed in the table above.

## Measured results
<!-- Numbers from AIPerformanceLog.md: lambda tuning notes, A* vs Dijkstra verification, replans-per-box-push counts. -->
