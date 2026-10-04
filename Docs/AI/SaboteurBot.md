# Saboteur Squad

Owner: S3 · Architecture: heuristic-based utility AI with logical action rules, run as four coordinated instances of one brain

## Role and requirement

The Saboteur Squad is four instances (A-D) of one brain. Each instance disrupts the player's progress by choosing between closing a useful door, arming a trap, taking a battery, attacking, escaping, and patrolling. A thin squad layer (target claims, attack saturation, a shared door-detour cache) stops the four from piling onto one target; it adds no second decision-maker. Saboteurs are destroyed permanently and never reboot. Its distinctive choice is a door that measurably lengthens the player's route to an objective. Alternate routes, clearing traps, and reaching targets first give the player counter-play.

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
4. Keep a valid selected pair for at least `1.5 s`. Idle/Patrol is a fallback, not a plan, so it is never held by commitment. A different eligible pair may interrupt early only when its **base** score is greater than `0.9`. After commitment, rank with momentum.
5. On equal ranking scores, keep the current eligible pair; otherwise use the action order in the table, then ascending target ID.
6. Start cooldown after confirmed success, not intent emission. A closed door has a `10 s` cooldown. Other cooldowns need agreement with target owners.

Emergency scoring does not override eligibility. Flee interrupts commitment early only if eligible and its base score exceeds `0.9`.

## Squad layer

### Instances and identity

| Instance | Colour | Notes |
| --- | --- | --- |
| A | purple | Carries the chapter keycard; drops it when destroyed |
| B | teal | |
| C | orange | |
| D | pink | |

Each instance owns its own brain object, with a numeric agent id (the claim owner id) and a letter. The brain does not know its colour: the tint is a model concern (S3 model; the tint component's location is to be agreed with S4). The letter and agent id must be given to the brain at spawn. `SpawnPoint` currently stores only `AgentType` and `IAgentState` has no instance id, so this is a **needed handoff from S4** (also needed for the HUD squad dots). Spawn rooms for A-D are **to be confirmed** with S1/S4.

### Target claims

The squad coordinates only through S2's `TargetClaims` on the blackboard. No claim state lives in the Saboteur folder.

- **Claim on commit.** An instance claims a door, trap or battery when it commits to CloseDoor, ArmTrap or StealBattery, using its final (compensated) score. A claim made while only evaluating a candidate would block the others without any intent behind it.
- **Arbitration.** `TargetClaims.TryClaim` succeeds when the target is unclaimed, already held by the caller, or held at a lower score; an exact tie goes to the lower agent id. The design uses that rule unchanged.
- **Displacement.** Because a higher score can take a claim, a holder can lose it between ticks. Every tick the holder checks `ClaimedBy(target) == own id`, and a lost claim cancels the plan the same way an invalid target does.
- **"Not claimed" consideration.** For CloseDoor, ArmTrap and StealBattery, a target held by another instance gives a consideration of 0, which vetoes the candidate for this instance. A target held by the instance itself, or unclaimed, gives 1.
- **Release.** `Release(agentId)` on action end, invalidation (door closed first, battery collected, trap armed), stun and destruction. A claim must never outlive the plan that made it.

### Attack saturation

An attack-count factor multiplies the AttackPlayer score by how many other live instances are already in AttackPlayer: `1.0` for 0 or 1 other attackers, `0.45` for 2 or more. Worked example: a raw attack score of `0.62` becomes `0.62 x 0.45 = 0.279`; with four considerations the compensated score is about `0.43`. That sits above Idle (`0.1`), so a third Saboteur still attacks when nothing else is useful, but below typical door scores of `0.5-0.7`, so it prefers sabotage when a useful door is free. The `0.45` factor is the plan's starting value and will be tuned in play with the reason recorded here.

### Tick stagger

Selection runs at 4 Hz (250 ms). Instances are offset by 62.5 ms (A = 0, B = 62.5, C = 125, D = 187.5 ms) from their first tick and keep that phase, so one frame never holds four selections. Timers use `AgentContext.Time` only; today that is `Time.time`, so cutscene and pause freezing depends on the controller supplying game time (**needed from S4**).

### DetourCache

Detour gain is player-centric, so four instances would repeat the same searches. `DetourCache` computes it once per door every 0.5 s for the whole squad and all four brains read the result. With six doors that is about 12 A* runs per pass (one open-route and one closed-route search per door), not four times that. The cache stops updating when all four Saboteurs are destroyed, and its searches are wrapped in the `AI.Saboteur.Detour` profiler marker. That the A* count is the same for one instance and for four is a design expectation until it is measured and recorded in `Docs/AIPerformanceLog.md`.

### Hypothetical door closure (proposal, awaiting S2 review)

The live grid is never changed to test a closure. Proposal: a Saboteur-owned `ICostModel` that wraps the base cost model and returns `float.PositiveInfinity` for steps into the door's cells. `ICostModel` only requires a step cost at least equal to the base cost, so infinity satisfies the contract. Because `PathResult` has no total, the route cost is the sum of step costs under the same model, and an infinite total is a lockout (the objective becomes unreachable), so that door is rejected. Open question for S2: how `AStarSearch` behaves when a step cost is infinite. I found no explicit infinity handling in it, so this must be confirmed and tested before relying on it.

### Permanent destruction

`IAgentBrain.OnDestroyed()` is called by `AgentController.Scrap` before `AgentEvents.OnDestroyed`, and there is no reboot. The brain:

- calls `TargetClaims.Release(agentId)` and releases any reservation;
- drops a carried battery;
- for Saboteur A, works out where the keycard lands and reports it through `IDropsItems.GetDrops` (see below), never on a blocked cell or inside a box;
- stops emitting intents.

The squad stops counting a destroyed instance for saturation, and `DetourCache` stops when none remain. Despawning or fading the body belongs to S4, and the keycard pickup prop to S2; the brain only reports the drop.

**Keycard drop (`IDropsItems`).** A brain cannot spawn objects, so `SaboteurBrain` implements the AI.Core contract `IDropsItems { int GetDrops(List<ItemDrop> buffer); }`. `ItemDrop` holds a `Kind` (`Keycard`, `Battery`), an `ItemId` and the `Cell`. The runtime calls `GetDrops` after `OnDestroyed` and creates the pickup; before destruction, and for Saboteurs B-D, it returns 0. The caller owns the buffer: drops are appended, existing entries are kept, nothing is allocated, and repeated calls return the same drop.

- **Where it lands.** `OnDestroyed` fixes the cell once. It starts from the cell of the last tick (the controller passes the cell under the body, which can be a blocked cell or one inside a box) and takes the nearest traversable cell within `DropSearchRadius` (8 cells, 4 m) using `GridGraph.TryFindNearestTraversable`.
- **Nothing walkable nearby.** The search widens to the whole grid, so the keycard is never dropped on a blocked cell and never lost. If the grid has no traversable cell at all, the start cell clamped onto the grid is reported anyway, because a missing keycard would be worse than an unreachable one.
- **Destroyed before its first tick.** The brain has no cell yet, so it starts from its first patrol cell, or from the middle of the grid when it has no patrol.
- **Later grid changes.** The cell is fixed at destruction; a box that lands there afterwards is not tracked.
- **`ItemId`.** Reported as given to the constructor (default 0). What the id means for the keycard (one fixed id, or the chapter task's id) is still to be agreed with S4 and S2.
- **Battery.** The brain carries no battery until StealBattery exists, so `ItemDropKind.Battery` is only in the contract for now.

### Flee and the keycard carrier

Flee stays ineligible until the runtime reports whether the player is aiming at the Saboteur. The keycard carrier (A) gets an extra Flee preference, because losing it early would stall the chapter. The size of that bonus is a tuning value to be decided in play and recorded here.

### Starting tuning

Plan Appendix A values: Saboteur health 4, speed 4.3 m/s, melee damage 10. Any change and its reason is recorded in this document.

## Alternatives considered

| Option | Why not |
| --- | --- |
| Finite state machine | A fixed priority list picks the same action when a door gives no detour or a battery is easier to take; it cannot weigh different kinds of disruption against each other. |
| Behaviour tree | Selectors order options but do not rank them with continuous scores, so it would need hand-tuned thresholds for the same comparison. |
| Four independent utility brains with no squad layer | Each instance would pick the same best door or trap and the squad would act as one. Claims fix that without a central planner. |
| A central planner assigning targets | Replaces the agents' own decisions and makes one instance's score meaningless. Claims keep every decision local and explainable. |

Four instances are still one agent type: one brain class, one set of curves, one design, with the squad layer as shared services (claims, saturation, cache) rather than extra decision logic.

## Edge cases

| Case | Handling |
| --- | --- |
| Door closed by the player first, or player in the doorway | Candidate rejected; claim released. |
| Battery collected or trap armed in transit | Plan cancelled immediately; claim released. |
| All scores below Idle | Idle/Patrol (0.1) wins. |
| No objective or no prediction | The objective chain falls through; with no objective, CloseDoor is ineligible. |
| Claim holder destroyed mid-action | `OnDestroyed` releases the claim; the others see it free on their next tick. |
| Keycard drop cell blocked | Nearest traversable cell is used, never the blocked one. |
| All four destroyed | Squad counters and `DetourCache` stop. |
| Cutscene or pause mid-action | No ticks; timers resume from the same values because they use `AgentContext.Time`. |
| Missing, dead or unreachable player | Player-dependent actions ineligible; sabotage and Idle stay valid. |

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

The Saboteur's `ResponseCurve` evaluates these four formulas without scene or world-state dependencies. Its input is a finite, dimensionless observation; finite values outside `[0, 1]` are clamped before evaluation. Linear results are also clamped to `[0, 1]`; the other formulas stay within that range by definition. Non-finite inputs or parameters are rejected. Power exponent and logistic steepness must be positive, and the logistic midpoint must lie in `[0, 1]`. A linear identity (`m = 1`, `b = 0`) preserves a normalised input; `k = 2` gives quadratic falloff; the logistic example uses `k = 10`, `x0 = 0.7` on ammo deficit. These are examples for curve verification, not final per-action tuning values.

`SaboteurCurveSettings` selects one formula and stores its parameters in a Saboteur-owned ScriptableObject asset. Four example assets in `Scripts/AI/Agents/Saboteur/Curves/` cover identity linear (`m = 1`, `b = 0`), quadratic power (`k = 2`), ammo-deficit logistic (`k = 10`, `x0 = 0.7`), and inverse. The ammo asset expects **deficit**, not remaining ammo, as its input. These values demonstrate the curve configuration and are not final action tuning. Keeping the settings beside Saboteur code avoids adding a shared assembly dependency; future brain selection can read the agreed settings after its inputs are available.

For an eligible action with `n >= 1` considerations `c_i`:

```text
raw = product(c_i)
modFactor = 1 - 1/n
baseScore = raw + (1 - raw) * modFactor * raw
rankingScore = min(1, baseScore + (isCurrentPair ? 0.15 : 0))
```

An action definition with no considerations is invalid; Idle/Patrol is the explicit constant-score exception. A zero consideration vetoes the candidate. Compensation reduces the structural disadvantage of actions with more inputs but does not make them mathematically equivalent. Four values of `0.9` yield `raw = 0.6561` and `baseScore ≈ 0.8253`. Show eligibility, each consideration, base/ranking scores, target, and rejection reason in the Saboteur debug view.

### Door detour heuristic

Resolve the objective with the chain: the Captain's `PredictedGoal` if it is current and confident, otherwise the nearest `ObjectiveTargets` entry, otherwise the nearest active switch. If none exists, CloseDoor is ineligible. `ObjectiveTargets` (S1) and `PredictedGoal` (S4) are not on the blackboard yet, so the chain is tested with fixed inputs. For each eligible open door, compare the shortest player-to-objective path in the current world with a **hypothetical** view where only that door is closed:

```text
gain(d) = (closedCost(d) - openCost) / openCost
doorConsideration(d) = clamp01(max(0, gain(d)))
```

The open cost must be finite and positive. Reject stale results, invalid costs, and closures that leave no player route. A zero or negative gain yields zero detour value. Open cost `10` and closed cost `16` in the same search cost units give gain `0.6`; a second door giving `12` has gain `0.2`, so the first ranks higher with equal other inputs.

Reuse the shared A* and base cost model. Do not mutate the live grid while scoring. The current `PathResult` contains cells but no total-cost field, so sum the returned steps with the same cost model. A hypothetical graph view and search scheduling need agreement with S1/S2. Cache the open-route result for the same player cell, objective, and graph version. The comparison is made once per door every 0.5 s for the whole squad by the shared `DetourCache` (see Squad layer), through S2's scheduler once it exists. Invalidate a comparison when its target, objective, or relevant graph cells change. The plan assumes at most six doors and thus up to 12 A* runs per complete pass; defer or cap work if the actual level exceeds the shared search budget. Never use a result from a different graph version.

## Integration boundaries

- Put pure scoring and selection code in `Assets/_Project/Scripts/AI/Agents/Saboteur/` under `ToyFactory.AI.Agents`. The Agents assembly references AI.Core only. Do not duplicate A* or add reverse references.
- Consume `AgentContext`, `SensorSnapshot`, and read-only `WorldBlackboard` facts. The latter two are currently stubs. Player health/ammo/aiming, target states, objectives, Captain prediction, action completion, and navigation validity are **needed handoffs**, not existing fields.
- Return `AgentIntent` with path, look target, action, target ID, and concise debug state. The current action enum supports `CloseDoor`, `ArmTrap`, `StealBattery`, and `Shoot`; movement-only Flee and Idle/Patrol use `None`. The current `AgentController` applies paths but does not execute actions or populate real cell/sensor facts. S2/S4-owned runtime work must supply execution and completion feedback.
- Runtime adapters supply physics, sight, doorway occupancy, aiming, interaction range, and hypothetical door state. The brain neither touches scene objects nor calls door or trap methods directly.
- On `OnGraphChanged`, replan movement only when changed cells affect its current route; invalidate a door comparison when changed cells affect a compared route or doorway. On `OnStunned`, cancel pending action and enter Stunned.

### Handoffs this design depends on

| Need | Owner | Status |
| --- | --- | --- |
| `TargetClaims` reachable through `WorldBlackboard` | S2 | Class and tests exist; not on the blackboard yet. Tests construct it directly. |
| Player state (position, cell, ammo, overcharge, alive, aiming), door/trap/battery state | S2 | Not on the blackboard. Fake snapshots in tests. |
| Pathfinder, grid and instance identity (letter, claim id, keycard carrier) given at spawn; an id on `IAgentState` | S4 (with S1 `GridManager`) | Not available. Constructor injection in tests. |
| Action execution and success/failure feedback | S4 controller, S2 targets | `AgentController` applies paths only. |
| `ISabotageable` on traps and batteries | S2 | `Door` only. |
| `ObjectiveTargets`; `PredictedGoal` with confidence | S1; S4 | Not published. Fixed test objective. |
| Game time in `AgentContext` | S4 / S2 | `Time.time` today. |
| Infinite-cost step behaviour in `AStarSearch` | S2 | To be confirmed (see hypothetical closure). |

### Implementation status

| Part | Status | Code and tests |
| --- | --- | --- |
| Response curves and curve settings | Implemented | `ResponseCurve`, `SaboteurCurveSettings`; `UtilityCurveTests`, `SaboteurCurveSettingsTests` |
| Considerations, compensation, selection, momentum, commitment, door cooldown | Implemented | `Consideration`, `UtilityAction`, `ActionSelector`; `UtilityScoringTests`, `ActionSelectorTests` |
| Brain skeleton: identity, 4 Hz selection, Idle/Patrol, stun, graph changes, destruction | Implemented | `SaboteurIdentity`, `SaboteurBrain`; `SaboteurBrainTests` |
| Decision trace for the debug panel: each candidate's raw and base score, each consideration's input and score, ranking, and why a candidate lost | Implemented; filled by the brain and `ActionSelector`, no allocation per decision | `UtilityDecisionTrace`, `SaboteurBrain.LastDecision`; `UtilityDecisionTraceTests` |
| CloseDoor, ArmTrap, StealBattery, AttackPlayer, Flee | Not started | Need the blackboard facts in the handoff table |
| Squad layer: claim on commit, "not claimed" veto, displacement check, release, attack saturation, tick stagger | Implemented in the brain: claims on commit, the veto and saturation applied to every candidate, stagger by letter, release on stun and destruction. The candidates themselves come from an `ICandidateSource`, which no action implements yet | `SquadCoordinator`, `ICandidateSource`, `SaboteurBrain`; `SquadClaimTests`, `SaboteurSquadTests` |
| Keycard drop through `IDropsItems` | Implemented; the battery drop waits for StealBattery | `IDropsItems`, `SaboteurBrain.GetDrops`; `SaboteurDropTests` |
| `DetourCache` | Not started | Build on the brain skeleton |

The skeleton's behaviour:

- **Selection.** At most every 0.25 s of `AgentContext.Time`, the brain rebuilds its candidates and asks `ActionSelector` for a choice. Today the only candidate is Idle/Patrol at 0.1, so the loop is in place before the other actions exist.
- **Patrol.** Patrol points are given in world space and converted to cells once. Points outside the grid are dropped; a blocked point snaps to the nearest free cell within 2 cells. A leg counts as finished when the Saboteur is within 0.5 m of its last waypoint on the ground plane, because the body stops up to 0.3 m short and may still be in the previous cell; `AgentContext.Cell` is only used as the start of the next search. Routes come from the injected `IPathfinder` with `BaseCostModel`, and are sent once per leg (`Path = null` keeps the current route). An unreachable point is skipped. If no point is reachable the Saboteur holds, and A* is retried only on the next selection pass or grid change, never every tick. With no patrol points it holds in place.
- **Debug data.** Every selection pass overwrites `SaboteurBrain.LastDecision`: the candidates in the order they were scored, their raw and compensated base scores, each consideration's name, input and score, the ranking score (base plus momentum), whether the commitment window was active, and for each candidate that did not win one reason: `Vetoed`, `OnCooldown`, `CommitmentHeld` or `Outranked`. The brain and selector write the numbers they already computed, so the panel only displays them. Candidates that are ineligible before scoring (for example a target claimed by another Saboteur) are not candidates, so they are not in the trace; the action that builds candidates will need to record them when it exists. Storage is allocated once per brain; a test checks that 1,000 passes allocate 0 bytes.
- **Grid changes.** `OnGraphChanged` replans only when a changed cell lies on the current route.
- **Stun.** The controller owns stun timing: it stops the body and does not tick the brain until the reboot. `OnStunned` therefore only cancels the current selection, releases this instance's claims and asks for a fresh route; the brain keeps no stun timer of its own, so a stun lasts exactly as long as the controller says.
- **Identity.** The brain rejects `default(SaboteurIdentity)`, which would otherwise read as Saboteur A, the keycard carrier.
- **Destruction.** `OnDestroyed` releases this instance's claims only (other instances keep theirs) and the brain only ever returns a stop afterwards. There is no reboot.

### Squad coordinator

`SquadCoordinator` takes the one shared `TargetClaims` and each instance registers with its `SaboteurIdentity`. It keeps no claim ownership of its own; it only remembers each member's committed action so it can count attackers and check the holder.

- `NotClaimedFactor(agentId, key)` is the "not claimed" consideration: 0 for a CloseDoor, ArmTrap or StealBattery target held by another instance, otherwise 1.
- `TryCommit(agentId, key, finalScore)` releases the instance's previous claim when the plan changes, then claims the target through `TryClaim` with the final score. Recommitting to the same pair refreshes the score.
- `CheckOutscored(agentId)`, called before each selection, returns true once when another instance has taken the claim, and drops the member's plan.
- `EndPlan(agentId)` covers action end, invalidation and stun; `OnDestroyed(agentId)` releases every claim and stops the instance counting as an attacker.
- `AttackSaturation(agentId)` returns `1.0` for 0-1 other live attackers and `0.45` for 2 or more.
- `DecisionOffset(letter)` returns 0, 62.5, 125 and 187.5 ms for A-D.

- `For(claims)` returns the one coordinator for a `TargetClaims` instance (a weak table keyed by the claims), so every brain built over the same claims shares claims and the attacker count without the spawner passing a coordinator around.

**In the brain.** `SaboteurBrain` registers its identity with `SquadCoordinator.For(claims)` as the last step of its constructor, so two brains in one squad must have different agent ids and a brain that fails validation never joins. Each decision pass then:

1. calls `CheckOutscored` on every tick, and drops its plan if another instance took the target;
2. asks its `ICandidateSource` for the eligible sabotage and combat candidates (Idle is always added by the brain). A source reports an instance's own scores and never looks at the squad;
3. multiplies each candidate by `NotClaimedFactor` (a target another instance holds becomes 0, so it is vetoed) and, for AttackPlayer, by `AttackSaturation`. The decision trace keeps the source's score as the raw score and the adjusted score as the base score, so a veto is a base score of 0 with the rejection reason `Vetoed`;
4. selects through `ActionSelector`, then calls `TryCommit` with the selected pair's base score. If the claim is lost (a same-frame race, which the veto makes rare) the plan is dropped and the next decision sees the new holder; if nothing is selected, `EndPlan` runs.

Stun calls `EndPlan` and destruction calls `OnDestroyed`, so a stunned or destroyed instance frees its targets at once.

**Stagger.** The first decision of an instance is `DecisionOffset(letter)` after its own first tick, and every later decision stays on that phase of the 250 ms grid (the next slot after the current time), so the four instances neither share a frame nor drift back together. A needs no offset, so it decides on its first tick. This assumes the squad's brains start ticking in the same frame, as the spawner does.

**Limits.** No action implements `ICandidateSource` yet, so in the level the candidate list is still Idle only and no claim is ever made; the squad behaviour is exercised in `SaboteurSquadTests` with fake sources. A claim is released when the plan changes, on stun, on destruction and when the claim is lost, but action end and invalidation (door closed first, battery collected, trap armed) need the runtime's completion feedback, which does not exist yet.

### Spawn hookup

`AgentSpawner.CreateBrain(point, identity)` now receives an `AgentIdentity` (spawn-order id and squad index), and its Saboteur case is S3's to replace. It still returns `MockPathProvider` until the shared grid and claims exist. The Saboteur case will be:

```csharp
new SaboteurBrain(
    new SaboteurIdentity(identity.Id, (SaboteurLetter)identity.SquadIndex),
    grid,                     // the shared GridGraph (S4's grid-cell branch, using S1's GridManager)
    pathfinder,               // one shared IPathfinder over that grid
    claims,                   // the one TargetClaims shared by all four (S2, via the blackboard)
    point.GetPatrolPositions());
```

`SaboteurIdentity` rejects a squad index outside A-D, so a Saboteur spawn point without a squad slot fails loudly instead of becoming a second Saboteur A. The four squad slots are set on the spawn points in `Agents.unity`, which is S4's scene. Until the grid and claims exist the Saboteurs keep the mock brain, and in-scene behaviour is untested.

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
| Two instances want one door | Only one holds the claim; a tie goes to the lower id; the other instance's candidate is vetoed. |
| A holder is outscored | Its next tick sees `ClaimedBy` differ and cancels the plan. |
| Third instance in AttackPlayer | Its score is multiplied by `0.45`. |
| Instance destroyed holding claims | All its claims are released; a carried battery is dropped. |
| Saboteur A destroyed next to a box | Keycard lands on the nearest traversable cell. |
| One versus four instances | `DetourCache` A* count is the same. |

During integration, check that intents move the Saboteur, execute on real targets, respect doorway occupancy, and display the computed scores. Record actual decision time, search count/time, replan count, and action choices in `Docs/AIPerformanceLog.md` with machine and scenario. This design makes no runtime or performance claim.
