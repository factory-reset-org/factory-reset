# Saboteur Bot

Owner: S3 · Architecture: heuristic-based utility AI with logical action rules

## Role and requirement

The Saboteur disrupts the player's progress by choosing between closing a useful door, arming a trap, taking a battery, attacking, escaping, and patrolling. Its distinctive choice is a door that measurably lengthens the player's route to an objective. Alternate routes, clearing traps, and reaching targets first give the player counter-play.

This is S3's **heuristic-based and logical agent**. Response curves and route comparisons are heuristics: they estimate how useful each feasible action is. Explicit eligibility, safety, invalidation, commitment, and cooldown rules are its logical reasoning: they determine which actions may be considered and when a plan must change. A score never authorises an impossible action.

## Action model

The full assigned action set is retained. Each door, trap, and battery generates a separate action-target candidate; the best eligible pair competes with the other actions. Idle/Patrol is always available with score `0.1`.

| Action | Logical eligibility | Heuristic preference | Intent |
| --- | --- | --- | --- |
| CloseDoor(d) | Door exists and is open; player is outside its doorway; Saboteur can reach and operate it; a valid objective and route comparison exist; cooldown ready. | Larger player detour and shorter Saboteur travel. | Travel to door, then `CloseDoor` with target ID. |
| ArmTrap(t) | Trap exists, is disarmed, reachable, operable, and ready; a valid player route predicts passage within 10 s. | Earlier likely passage and shorter travel. | Travel to trap, then `ArmTrap` with target ID. |
| StealBattery(b) | Battery exists and is collectible; Saboteur can reach it before player. | Lower player ammunition, proximity to player route, and larger arrival advantage. | Travel to battery, then `StealBattery` with target ID. |
| AttackPlayer | Player is alive, visible, within 8 m, and attack ready. | Closer range and higher own health. | Face player and request `Shoot`. |
| Flee | Player is alive and threatening; a safe reachable escape cell is known. | Lower own health and player aiming at Saboteur. | Follow escape path. |
| Idle/Patrol | Always eligible while active. | Constant `0.1`. | Follow agreed patrol path, or hold position if none. |

Missing facts make the affected candidate ineligible. A target that becomes invalid cannot remain selected because of a high score or momentum. An unreachable player does not prevent independent sabotage when the necessary objective and route facts are still valid.

### Decision cycle and phases

Utility selection runs at 4 Hz. On every `IAgentBrain.Tick`, check hard invalidation and stun first. At a selection tick, build candidates, reject ineligible ones, calculate scores, apply stability rules, and return the selected `AgentIntent`. Between selection ticks, keep the latest valid intent. Runtime may call `Tick` more often; target and path invalidation must still be checked each call.

| Phase | Behaviour |
| --- | --- |
| Assess | Select the highest eligible action-target pair; enter Travel, Execute, or Hold. |
| Travel | Follow a route to a door, trap, battery, or escape cell; revalidate target and route; Execute only when in interaction range. |
| Execute | Request interaction or attack; start cooldown only after runtime confirms success; reassess on failure. |
| Hold | Patrol or remain in place; reassess next selection tick. |
| Stunned | Suppress actions and discard pending interaction; reassess with fresh facts when recovered. |

These are Saboteur behaviour phases, not a new shared FSM framework. Attack and Flee may start directly from Assess. A missing or dead player makes player-dependent actions ineligible; Hold remains valid.

### Logical rules and stability

Apply rules in this order:

1. Stun, invalid target, or blocked route cancels the current plan immediately, including during commitment. A door closed by someone else, an armed trap, or a collected battery is invalid.
2. Reject candidates that fail eligibility. Player doorway occupancy vetoes CloseDoor regardless of score. Reject a hypothetical door closure that makes the active objective unreachable, avoiding an unintended player lockout.
3. Score eligible candidates. Add `0.15` momentum to the **current action-target pair's** ranking score, capped at `1`. Momentum never restores eligibility.
4. Keep a valid selected pair for at least `1.5 s`. A different eligible pair may interrupt early only when its **base** score is greater than `0.9`. After commitment, rank with momentum.
5. On equal ranking scores, keep the current eligible pair; otherwise use the action order in the table, then ascending target ID.
6. Start cooldown after confirmed success, not intent emission. A closed door has a `10 s` cooldown. Other cooldowns need agreement with target owners.

Emergency scoring does not override eligibility. Flee interrupts commitment early only if eligible and its base score exceeds `0.9`.

## Heuristics and response curves

Every consideration maps an observed value into `[0, 1]`, where higher means more desirable. Clamp valid numerical inputs at their defined boundary; reject missing or non-finite inputs. Distances used for action range are in metres and arrival times are in seconds; route costs use the shared search cost model's units.

| Input | Normalisation or curve | Actions |
| --- | --- | --- |
| Saboteur path distance `d` | `1 - clamp01(d / Dmax)`; `Dmax` is maximum useful travel distance. | Door, trap, battery |
| Player ammo fraction `a` | Ammo deficit `x = 1 - clamp01(a)`; rising logistic centred at `x = 0.7` (30% ammo remaining). | Battery |
| Own health fraction `h` | `clamp01(h)` for Attack; `1 - clamp01(h)` for Flee. | Attack, Flee |
| Attack distance `d` | Require `d <= 8 m`; within range use `1 - d/8`. | Attack |
| Predicted trap arrival `t` | Require `0 <= t <= 10 s`; use `1 - t/10`. | Trap |
| Door detour gain `g` | `clamp01(max(0, g))`; a 100% longer route saturates. | Door |
| Battery arrival advantage `v` | Require `v > 0`; `clamp01(v / Vmax)`, where `v = playerArrival - saboteurArrival`. | Battery |
| Battery distance from player route `r` | `1 - clamp01(r / Rmax)`. | Battery |
| Player aiming at Saboteur | `1` when reported, otherwise `0`; missing aiming data makes Flee unavailable until a threat substitute is agreed. | Flee |

`Dmax`, `Vmax`, `Rmax`, attack readiness, escape selection, and trap prediction confidence are integration/tuning decisions. Fix them with relevant system owners before implementation, so the same snapshot always yields the same score.

Available curve definitions are linear `clamp01(m*x+b)`, power `x^k` for `k > 0`, logistic `1/(1+exp(-k*(x-x0)))` for `k > 0`, and inverse `1-x`. Inputs are clamped to `[0,1]`. The plan calls the power curve quadratic falloff when `k = 2`. Applying a positive logistic directly to *remaining* ammo would favour a well-supplied player; using ammo **deficit** fixes that directional error. Curve parameters should be editable configuration once assembly placement is agreed.

For an eligible action with `n >= 1` considerations `c_i`:

```text
raw = product(c_i)
modFactor = 1 - 1/n
baseScore = raw + (1 - raw) * modFactor * raw
rankingScore = min(1, baseScore + (isCurrentPair ? 0.15 : 0))
```

An action definition with no considerations is invalid; Idle/Patrol is the explicit constant-score exception. A zero consideration vetoes the candidate. Compensation reduces the structural disadvantage of actions with more inputs but does not make them mathematically equivalent. Four values of `0.9` yield `raw = 0.6561` and `baseScore ≈ 0.8253`. Show eligibility, each consideration, base/ranking scores, target, and rejection reason in the Saboteur debug view.

### Door detour heuristic

Use the Captain's predicted objective if it is current and active; otherwise use the nearest active switch. If neither exists, CloseDoor is ineligible. For each eligible open door, compare the shortest player-to-objective path in the current world with a **hypothetical** view where only that door is closed:

```text
gain(d) = (closedCost(d) - openCost) / openCost
doorConsideration(d) = clamp01(max(0, gain(d)))
```

The open cost must be finite and positive. Reject stale results, invalid costs, and closures that leave no player route. A zero or negative gain yields zero detour value. Open cost `10` and closed cost `16` in the same search cost units give gain `0.6`; a second door giving `12` has gain `0.2`, so the first ranks higher with equal other inputs.

Reuse the shared A* and base cost model. Do not mutate the live grid while scoring. The current `PathResult` contains cells but no total-cost field, so sum the returned steps with the same cost model. A hypothetical graph view and search scheduling need agreement with S1/S2. Cache the open-route result for the same player cell, objective, and graph version. Compare doors at most 2 Hz through the shared scheduler. Invalidate a comparison when its target, objective, or relevant graph cells change. The plan assumes at most six doors and thus up to 12 A* runs per complete pass; defer or cap work if the actual level exceeds the shared search budget. Never use a result from a different graph version.

## Integration boundaries

- Put pure scoring and selection code in `Assets/_Project/Scripts/AI/Agents/Saboteur/` under `ToyFactory.AI.Agents`. The Agents assembly references AI.Core only. Do not duplicate A* or add reverse references.
- Consume `AgentContext`, `SensorSnapshot`, and read-only `WorldBlackboard` facts. The latter two are currently stubs. Player health/ammo/aiming, target states, objectives, Captain prediction, action completion, and navigation validity are **needed handoffs**, not existing fields.
- Return `AgentIntent` with path, look target, action, target ID, and concise debug state. The current action enum supports `CloseDoor`, `ArmTrap`, `StealBattery`, and `Shoot`; movement-only Flee and Idle/Patrol use `None`. The current `AgentController` applies paths but does not execute actions or populate real cell/sensor facts. S2/S4-owned runtime work must supply execution and completion feedback.
- Runtime adapters supply physics, sight, doorway occupancy, aiming, interaction range, and hypothetical door state. The brain neither touches scene objects nor calls door or trap methods directly.
- On `OnGraphChanged`, replan movement only when changed cells affect its current route; invalidate a door comparison when changed cells affect a compared route or doorway. On `OnStunned`, cancel pending action and enter Stunned.

## Architecture rationale

A fixed priority list would select the same action when a door gives no detour or a battery is easier to steal. Behaviour phases organise execution but cannot rank different disruptions. Utility scores make that comparison; logical gates keep it valid and safe. The calculations and rejection reasons can be shown directly in the debug overlay. The Guard and Captain designs are consistency references only; their implementations and shared infrastructure remain with their owners.

## Verification and evidence

EditMode tests should use deterministic snapshots and fake target/search results. They prove brain decisions, not scene integration.

| Scenario | Expected result |
| --- | --- |
| Curves at `0`, `0.5`, `1`; ammo remaining `0.1` versus `0.9` | Bounded outputs; lower ammo gives higher battery-theft desire. |
| Four `0.9` considerations | Compensated base score approximately `0.8253`. |
| Equal scores and multiple target IDs | Stable action order and target-ID tie break are deterministic. |
| Small score change during commitment; new base score above `0.9` | Current valid pair remains for small change; emergency candidate may interrupt. |
| Open route `10`, closed routes `16` and `12` | First door ranks higher with equal other considerations. |
| Player in doorway; door already closed; zero open cost; unreachable objective; stale route | CloseDoor candidate rejected; no live grid mutation. |
| Battery collected or trap armed during travel | Current pair invalidates immediately and fresh decision follows. |
| Missing/dead player, absent objective, unreachable target, stun | No invalid action intent; valid fallback or independent action remains; stun suppresses actions. |
| Unrelated versus route-intersecting grid change | Only affected path/comparison is requested again. |
| Runtime reports action failure versus success | Cooldown starts only after success. |

During integration, check that intents move the Saboteur, execute on real targets, respect doorway occupancy, and display the computed scores. Record actual decision time, search count/time, replan count, and action choices in `Docs/AIPerformanceLog.md` with machine and scenario. This design makes no runtime or performance claim.
