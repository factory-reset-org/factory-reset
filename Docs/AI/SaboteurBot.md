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
| AttackPlayer | Player is alive, visible, within 8 m, and attack ready. | Closer range and higher own health. | Walk at the player until within 2 m, then face them and request `Shoot` (the claw swipe). |
| Flee | Player is alive and threatening; a safe reachable escape cell is known. | Lower own health and player aiming at Saboteur. | Follow escape path. |
| Idle/Patrol | Always eligible while active. | Constant `0.1`. | Follow agreed patrol path, or hold position if none. |

Missing facts make the affected candidate ineligible. A target that becomes invalid cannot remain selected because of a high score or momentum. An unreachable player does not prevent independent sabotage when the necessary objective and route facts are still valid.

### Decision cycle and phases

Utility selection runs at 4 Hz. On every `IAgentBrain.Tick`, check hard invalidation and stun first. At a selection tick, build candidates, reject ineligible ones, calculate scores, apply stability rules, and return the selected `AgentIntent`. Between selection ticks, keep the latest valid intent. Runtime may call `Tick` more often; target and path invalidation must still be checked each call.

| Phase | Behaviour |
| --- | --- |
| Assess | Select the highest eligible action-target pair; enter Travel, Execute, or Hold. |
| Travel | Follow a route to a door, trap, battery, or escape cell; revalidate target and route; Execute only when in interaction range. |
| Execute | Request interaction or attack; start the cooldown when the attempt ends (rule 6), never when the intent is emitted; reassess on failure. |
| Hold | Patrol or remain in place; reassess next selection tick. |
| Stunned | Suppress actions and discard pending interaction; reassess with fresh facts when recovered. |

These are Saboteur behaviour phases, not a new shared FSM framework. Attack and Flee may start directly from Assess. A missing or dead player makes player-dependent actions ineligible; Hold remains valid.

### Logical rules and stability

Apply rules in this order:

1. Stun, invalid target, or blocked route cancels the current plan immediately, including during commitment. A door closed by someone else, an armed trap, or a collected battery is invalid.
2. Reject candidates that fail eligibility. Player doorway occupancy vetoes CloseDoor regardless of score. A closure that leaves the player no route is allowed: the player can open any door a Saboteur shuts, so it is the most a door can cost them, not a soft lock (see Sabotage before combat).
3. Score eligible candidates. Add `0.15` momentum to the **current action-target pair's** ranking score, capped at `1`. Momentum never restores eligibility.
4. Keep a valid selected pair for at least `1.5 s`. Idle/Patrol is a fallback, not a plan, so it is never held by commitment. A different eligible pair may interrupt early only when its **base** score is greater than `0.9`. After commitment, rank with momentum.
5. On equal ranking scores, keep the current eligible pair; otherwise use the action order in the table, then ascending target ID.
6. Start the cooldown when an attempt ends, never when the intent is emitted: on the runtime's answer, success or failure (`ActionSelector.NotifySuccess` and `NotifyFailure` start the same cooldown), or when the brain gives the attempt up itself (no route, stuck on the way, no answer 3 s after reaching the target). A failure counts so that an unreachable target is not chosen again at every decision. A target that stops being valid on its own (shut, armed or taken by someone else, or the player stepping into the doorway) ends the plan with no cooldown. A door, trap or battery has a `10 s` cooldown; AttackPlayer, Flee and Idle have none (the attack is paced at 1.2 s instead).

Emergency scoring does not override eligibility. Flee interrupts commitment early only if eligible and its base score exceeds `0.9`.

## Squad layer

### Instances and identity

| Instance | Colour | Notes |
| --- | --- | --- |
| A | purple | Carries the chapter keycard; drops it when destroyed |
| B | teal | |
| C | orange | |
| D | pink | |

Each instance owns its own brain object, with a numeric agent id (the claim owner id) and a letter. The brain does not know its colour: the tint is a model concern. `SaboteurTint` (`Runtime/Visuals/`, S3; the location agreed with S4 on 4 October) reads the squad slot from the `IAgentState` above it once, in `Start`, and writes the colour into a `MaterialPropertyBlock`. The spawner gives the brain its letter and agent id from the spawn point's squad slot and its spawn-order id (`AgentIdentity`, also on `IAgentState.Identity`; the HUD squad dots the plan mentioned were not built); see Spawn hookup.

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

Selection runs at 4 Hz (250 ms). Instances are offset by 62.5 ms (A = 0, B = 62.5, C = 125, D = 187.5 ms) from their first tick and keep that phase, so one frame never holds four selections. Timers use `AgentContext.Time` only. The controller fills it from `GameClock.Current.GameTime`, the GameManager's game time, which stops during cutscenes and the pause menu, so the stagger and every brain timer stop with it; only a test scene with no GameManager falls back to Unity's clock.

### DetourCache

Detour gain is player-centric, so four instances would repeat the same searches. `DetourCache` computes it once per door every 0.5 s for the whole squad and all four brains read the result. With six doors that is about 12 A* runs per pass (one open-route and one closed-route search per door), not four times that. The cache stops updating when all four Saboteurs are destroyed, and its searches are wrapped in the `AI.Saboteur.Detour` profiler marker. In code, a refresh does one search for the player's current route and one more only for each door that lies on or right next to that route, so a door off the route costs no search. A refresh runs at most every 0.5 s of game time, or at once when the grid version or the objective list changes. The objective is the Captain's `PredictedGoal` when its confidence is at least 0.5, otherwise the nearest entry of `WorldBlackboard.ObjectiveTargets`. The searches are counted in `DetourCache.Searches`, and `SaboteurDetourTests` checks that four Saboteurs asking at their staggered times cost the same number of searches as one (two on the test map: the route and the one door on it). Its timings are measured in `Docs/AIPerformanceLog.md`: the squad scenario and the refresh on a level-sized grid (15 October) the seven-agent stress run with the real brain (16 October), and its re-run with the brain of 20 October, which splits the Saboteur cost by marker (`AI.Saboteur.Decide`, `.Detour`, `.Route`, `.WalkReach`, `.PatrolPlan`): the slow frames are the detour refresh and the body-aware route search, not the door, trap and battery reachability searches.

### Hypothetical door closure

The live grid is never changed to test a closure. `DoorClosureCostModel` is a Saboteur-owned `ICostModel` that returns the base step cost, except `float.PositiveInfinity` for a step into or out of the door's cells and for a diagonal step that cuts the corner of one (the grid forbids those diagonals once the door is closed). `ICostModel` only requires a step cost at least equal to the base cost, so infinity satisfies the contract. Because `PathResult` has no total, the route cost is the sum of step costs under the same model (`DoorClosureCostModel.PathCost`), and an infinite total is a lockout: the door is the player's only way on, which CloseDoor values at its highest.

**How `AStarSearch` treats an infinite step (tested in `SaboteurDetourTests`).** Nothing in it needs to change. With another route available it expands the finite cells first and returns a finite route around the infinitely priced cell. When the only route crosses it, the search does not fail: it returns that route with `Found = true`, and the route's summed cost is infinite. So a lockout is read from the summed cost, never from `PathResult.Found`. The search terminates and never produces NaN in either case. S2 has not yet confirmed this is the intended behaviour; the result goes to S2 as a note, and nothing else depends on it.

### CloseDoor

`CloseDoorSource` offers `CloseDoor(d)` only to a Saboteur that stands in the same room as the player (`DetourCache.IsInPlayerRoom`), and only for that room's open ways out (`DetourCache.PlayerRoomExits`), so it shuts the player in with itself and never shuts itself out in another room. The room is one flood a refresh for the squad from the player's cell, over the same floor the room patrol uses, ended by doorways; a body in a doorway or on the edge of a prop counts as in the room. Two considerations through `UtilityAction`: the door's value (`CloseDoorSource.DoorValue`: 1 for the way out the player's route takes, 0.6 for another) and the Saboteur's distance to the door (`1 - d / 30 m`; the real walk once a brain has bound its pathfinder). A door is not offered when it is already closed or when the player is on or next to its cells. The squad layer vetoes a door another Saboteur has claimed, and the brain scores what is left in the door band (Sabotage before combat). A door plan under way ends with no cooldown as soon as the two no longer share a room (the player has left, or the Saboteur was pushed through the doorway); the door is offered again when they do.

The brain carries it out in two phases, only when it was given the shared `DetourCache` (it needs the door's cells):

- **Travel.** One A* route to the nearest door cell is sent (`Path`), and sent again if a changed cell lies on it. A route that cannot be found, or ends out of reach, ends the plan as a failure.
- **Execute.** Within 1.5 m of the door the body stops once and each tick the intent is `AgentAction.CloseDoor` with `ActionTargetId` = the door id (`Door.doorId` = `DoorwayMarker.DoorId` = grid `DoorId`). Until 20 October 2026 a last `DetourCache.WouldLockPlayerOut` search ran here and gave up a door that would shut the player in; it no longer does, because shutting them in is the point and they can open the door again. The doorway check still runs on every tick, so a door never comes down on the player.
- **Stuck.** A walk is watched for progress in game time (a pause never counts). If the Saboteur gets no 0.5 m closer to the door for `TravelStuckSeconds` (4 s), its route is sent again from where the body is now; stuck again for as long, and it gives the door up as a failure (cooldown, claim released). This covers a body shoved aside, carried off, teleported or wedged against something the grid does not know about, which would otherwise keep "walking" a stale route and hold the claim for ever. The same watch runs on trap and battery walks. A body standing in a blocked cell (against a wall or a box) plans from the nearest free cell within `StartSnapRadius` (4 cells) instead of failing to plan anything.
- **End.** The controller answers every door request exactly once through `IActionFeedback.OnActionResolved(action, targetId, success)`: success when it closed the door, failure when the door is not registered, the Saboteur stayed out of reach for 2 s, a newer request replaced it, or the Saboteur was knocked out first. Either answer starts the door cooldown (10 s) at once and ends the plan on the next tick, with the claim released. The grid still ends the plan if the door is seen closed, and a door still open 3 s after the Saboteur reached it is given up as a failure, so a brain without a controller (tests) still cannot stand at a door for ever. A door shut by someone else just stops being a candidate and earns no cooldown.

Wired into the level through `AgentSpawner.CreateBrain` (see Spawn hookup): the controller carries `CloseDoor` out and answers through `IActionFeedback`. `CompositeCandidateSource` combines it with `AttackPlayerSource`, `FleeSource`, `ArmTrapSource` and `StealBatterySource`.

### ArmTrap and StealBattery

Two candidate sources read S2's trap and battery facts (`world.SabotageFacts`: id, cell, kind, available) the way `CloseDoorSource` reads the door detours. Ids are unique across doors (1-4), traps (11-14) and batteries (21-26), which the claims need because `TargetClaims` keys on the id alone. A fact that is missing, or not available (a trap already armed, a battery already taken), is not a candidate. Both sources move the fact's cell to the nearest walkable cell within 4 cells (`SabotageTargetCells.TrySnap`; traps and batteries can stand in props or on shelves) and skip a cell outside the grid. The squad layer then vetoes a target another Saboteur has claimed.

**ArmTrap(t)** (`ArmTrapSource`). Eligible when the trap is available (not armed) and the player is alive, and either the player is within 10 m of it (near: any walk up to 20 m is worth it, since a player moving about a room with a trap in it will likely cross it) or the squad's `DetourCache` route to the objective passes within 2 cells of it within 10 s at their top speed and the Saboteur gets there a full second before them. Never within 2 m of the player, who would be standing on it. Two considerations: the urgency (`ArmTrapSource.Urgency`: 1 beside the player down to 0.5 at 10 m, or `1 - seconds / 10` on the route, whichever is higher) and the walk over 20 m through `Inverse`.

**StealBattery(b)** (`StealBatterySource`). Eligible when the Saboteur is alerted to the player (its awareness, below: it sees them, saw them in the last 6 s or heard them in the last 8 s), the player is alive, and the battery's walkable cell is within 12 m of the Saboteur by the walk it would really take (the squad's A*), and that walk is shorter than the player's straight line to the battery: the battery is closer to the Saboteur than to the player. There is no race at top speed any more (the player sprints at 7 m/s and the Saboteur walks at 4.3 m/s, so the race ruled out nearly every battery), and a full blaster no longer vetoes the theft, because a battery also gives the player a spare cell and 25 integrity. Two considerations: the player's need (`StealBatterySource.NeedScore`, 0.5 for a full blaster rising to 1 for an empty one) and the walk over 12 m through `Inverse`. At most two walks are measured per decision, after straight distances have ruled most batteries out.

**Reachability.** Straight distances rank the targets cheaply first; then the real walk is measured with the squad's A* for at most two doors, two traps and two batteries per decision, and a target with no route or a walk past its range is not offered (doors 30 m, traps 20 m, batteries 12 m). A trap offered only for being on the route must also be reached a full second before the player; none is armed within 2 m of them. The brain still finds its own route when it commits, and a target it cannot reach ends the plan as a failure, shared with the squad.

**In the brain** (`TryFactAction`, the same shape as CloseDoor):

- **Travel.** One A* route to the target's walkable cell, sent again if a changed cell lies on it. A route that cannot be found, or ends out of reach, ends the plan as a failure.
- **Execute.** Within `FactReachMetres` (1.5 m) of that cell the body stops once and each tick the intent is `AgentAction.ArmTrap` or `StealBattery` with `ActionTargetId` = the fact id, looking at the target. The controller acts within its own 2.5 m reach of the object and answers once through `IActionFeedback`.
- **End.** The runtime's answer starts the cooldown and ends the plan on the next tick. A target that vanishes or becomes unavailable ends the plan with no cooldown. A request still unanswered `FactTimeoutSeconds` (3 s) after the Saboteur reached it is a failure. Stun drops the plan. The cooldown is new: `SelectorSettings.FactCooldownSeconds` (10 s, a starting value like the door's), so an unreachable trap or battery is not chosen again at every decision.

**Departures from the design tables above, to tune in play (C2).** The design scores a battery on a logistic of the ammunition deficit, the arrival advantage and its distance from the player's route; the source scores the linear ammunition deficit and the Saboteur's distance, and uses the arrival advantage only as an eligibility rule. It does not use the Captain's `PredictedGoal` for the trap route beyond what `DetourCache` already does. Taking a battery does not yet make the Saboteur carry it, so a destroyed Saboteur drops no battery (`ItemDropKind.Battery` stays unused; the battery pickup contract is S2's). Whether the executor's arming and taking look right on screen has not been looked at.

### Permanent destruction

`IAgentBrain.OnDestroyed()` is called by `AgentController.Scrap` before `AgentEvents.OnDestroyed`, and there is no reboot. The brain:

- calls `TargetClaims.Release(agentId)` and releases any reservation;
- drops a carried battery (none yet: StealBattery does not make the Saboteur carry one);
- for Saboteur A, works out where the keycard lands and reports it through `IDropsItems.GetDrops` (see below), never on a blocked cell or inside a box;
- stops emitting intents.

The squad stops counting a destroyed instance for saturation, and `DetourCache` stops when none remain. Despawning or fading the body belongs to S4, and the keycard pickup prop to S2; the brain only reports the drop.

**Keycard drop (`IDropsItems`).** A brain cannot spawn objects, so `SaboteurBrain` implements the AI.Core contract `IDropsItems { int GetDrops(List<ItemDrop> buffer); }`. `ItemDrop` holds a `Kind` (`Keycard`, `Battery`), an `ItemId` and the `Cell`. The runtime calls `GetDrops` after `OnDestroyed` and creates the pickup; before destruction, and for Saboteurs B-D, it returns 0. The caller owns the buffer: drops are appended, existing entries are kept, nothing is allocated, and repeated calls return the same drop.

- **Where it lands.** `OnDestroyed` fixes the cell once. It starts from the cell of the last tick (the controller passes the cell under the body, which can be a blocked cell or one inside a box) and takes the nearest traversable cell within `DropSearchRadius` (8 cells, 4 m) using `GridGraph.TryFindNearestTraversable`.
- **Nothing walkable nearby.** The search widens to the whole grid, so the keycard is never dropped on a blocked cell and never lost. If the grid has no traversable cell at all, the start cell clamped onto the grid is reported anyway, because a missing keycard would be worse than an unreachable one.
- **Destroyed before its first tick.** The brain has no cell yet, so it starts from its first patrol cell, or from the middle of the grid when it has no patrol.
- **Later grid changes.** The cell is fixed at destruction; a box that lands there afterwards is not tracked.
- **`ItemId`.** Reported as given to the constructor (default 0). What the id means for the keycard (one fixed id, or the chapter task's id) is still to be agreed with S4 and S2.
- **Battery.** StealBattery does not make the brain carry the battery it takes (see ArmTrap and StealBattery), so `ItemDropKind.Battery` is only in the contract for now.

### Flee and the keycard carrier

Flee is built (`FleeSource`, `FleePlanner`). The runtime does not report whether the player is aiming, so aiming is worked out from `PlayerSnapshot.Forward`: the player's flat facing direction must be within 40 degrees of the direction to the Saboteur. The consideration is 0 at the edge of the cone and 1 dead on, so a player who looks away stops being a reason to run.

**Eligibility** (decided before scoring): the player is known and alive, less than 14 m away, seen by the Saboteur (its awareness: the vision cone, the all-round 3.5 m, or a player it is already tracking, all with a line of sight) and facing it. Otherwise Flee is not a candidate, except that a flight already offered stays on offer at the same score for 1.5 s after the player stops qualifying, so a wobbling aim does not flip it between running and coming back.

**Score.** Three considerations, compensated as for every action (three considerations, so `modFactor = 2/3`):

| Consideration | Input | Curve |
| --- | --- | --- |
| Low health | `1 - own health` | logistic, steepness 10, midpoint 0.5: about 0.08 after one hit of four, 0.5 after two, 0.92 after three |
| Player aims at me | 0 to 1 across the aim cone | direct |
| Player close | `distance / 14 m` | `1 - x` |

In the level the Saboteur prefab has 2 hit points (Appendix A says 4), so one hit leaves it at 50% and Flee scores about 0.5 raw when the player faces it; with the design's 4 hit points the same curve starts after the second hit.

Worked example: health 0.25, player dead on at 5 m: `0.9241 x 1 x 0.6429 = 0.5941`, compensated `0.5941 + 0.4059 x 0.6667 x 0.5941 = 0.7549`. At the same spot AttackPlayer scores about 0.12 (`(1 - 5/8) x 0.25`), so a badly hurt Saboteur runs while a healthy one (Flee about 0.004) fights: Flee at full health is far below Idle (0.1).

**Keycard carrier.** Saboteur A's Flee score is multiplied by 1.25 and capped at 1. A multiplier and not an addition, so A at full health, whose Flee score is near 0, still does not run from a player who has not hurt it. The 0.25 is a starting value; tune it in play and record the result here.

**Where it runs** (`FleePlanner`). Candidate refuges are the cells 5, 8 and 12 m from the Saboteur in the eight compass directions, plus its patrol points. Each is snapped to a walkable cell or dropped, and kept only if it is at least 3 m farther from the player than the Saboteur is now. They are ranked by `distance from the player - 0.4 x distance to run`, and the shared A* runs for at most the best three. A route that passes closer to the player than the Saboteur stands now (down to 2.5 m) is rejected, so it never runs through the player. The brain chooses again once a second, or on arrival, because the player moves. Speed is unchanged (4.3 m/s, Appendix A).

**Cornered.** If no refuge gives a route, the Saboteur fights instead (it keeps Flee selected, but the brain carries out AttackPlayer while the player is in range) and looks again after 1 s. It never freezes in place.

**Ends** when the player dies or goes out of view, looks away, or the Saboteur is stunned or destroyed. Flee has no cooldown and no claim: it is not a squad target.

### Starting tuning

Plan Appendix A values: Saboteur health 4, speed 4.3 m/s, melee damage 10. Any change and its reason is recorded in this document.

**Review of 15 October 2026: no change.** The squad scenario (two doors, four Saboteurs) chose as designed: each of A and B took the door beside it, C and D, with both doors claimed, fell back to Idle, a destroyed holder's door passed to another Saboteur, and a closed door was not offered again. In the live scene the Saboteur 4 m from the player attacked instead of taking a door, which is the intended order (AttackPlayer scores above CloseDoor at that range), and the other two shared the doors. Two close doors scored 0.60 and 0.58 and moving by 0.1 either way switched 232 times in two minutes without stability and 8 with momentum 0.15 and 1.5 s commitment, so those two values stay. The attack saturation factor 0.45, the door distance cap (30 m) and the 4 Hz decision interval were not exercised hard enough here to justify a change.

### Perception, memory, search and patrol (19 October 2026)

Before this change the squad reacted to the player with full knowledge: AttackPlayer and Flee needed only a line of sight, and doors, traps and batteries used the player's position anywhere on the map. Each Saboteur now has its own `SaboteurAwareness`, built in the brain on the line-of-sight query of its attack or flee source and bound to all its sources (`ISaboteurAwarenessUser`), so every action reacts only to a player that Saboteur could have seen or heard. All of it is in `AI/Agents/Saboteur/`; the spawner, the controller, animation, models and interactions are unchanged.

| Sense | Rule |
| --- | --- |
| Vision cone | 60 degrees each side of the body's facing (120 in all), out to 12 m, with a line of sight |
| All-round sense | Any direction within 3.5 m, with a line of sight |
| Tracking | A player seen in the last second stays seen out to 14 m whatever the facing, with a line of sight |
| Memory | The last sighting (position, cell, velocity, time) is kept 6 s |
| Hearing | The last player noise (source id -1, not a lure) is kept 8 s; agents' noises and thrown toys are ignored |

The line of sight is the same physics ray as before (from the player's eye to the Saboteur's cell); it is only cast when one of the rules could make the player seen, at most every 0.1 s.

**What needs what.** AttackPlayer and Flee need the player seen. StealBattery needs the player seen. ArmTrap and CloseDoor need the Saboteur to be alerted: it sees the player, saw them within 6 s or heard them within 8 s. A source used on its own (no brain bound it) keeps its earlier rules, which is how the older unit tests of door feedback still run.

**Search** (a new action, `Search`, after Idle in the tie order). One candidate at most:

| Situation | Goal | Score |
| --- | --- | --- |
| Player seen beyond the 2 m melee reach | the player (shown as `Approach`) | 0.12, times attack saturation |
| Sighting remembered, player not seen | last position plus 1 s of their velocity (at most 4 m) | 0.5 fresh, falling to 0.15 at 6 s |
| Player noise remembered | where the noise came from | 0.12 to 0.3 by loudness and age |

Two or more squad mates already searching halve it. The Saboteur walks there, stops and turns its head round over 2.4 s, checks one spot 3 m further on (the way the player went, or on from the noise), and gives the search up at 14 s at the latest. A finished search marks the sighting and noise as searched, so they are not offered again; a new one is. Then it returns to its patrol.

**Room patrol** (`RoomPatrolPlanner`, `SaboteurOptions.RoomPatrol`, on in the game). The Saboteur's room is the walkable floor reached from its first patrol point without passing a door, up to 18 m. Where two Saboteurs start in one room (A and D in the Storage Area) each keeps the floor nearer its own first point. Three to seven points are spread over that area (at least 3 m apart, away from walls where possible), seeded with the level's own patrol points, and walked as a nearest-neighbour loop. In the level the planned loops are A 5 points, B 7, C 7, D 5. A Saboteur given no patrol points still holds in place.

**Stuck detection.** Progress on a walk is now what is left of the route (`SaboteurPaths.RemainingMetres`), not the straight distance to the target, so a route that first bends away round a wall is not taken for no progress. The rule is unchanged otherwise (0.5 m in 4 s, one re-send, then give up) and now also covers patrol walks: a patrol point it cannot make progress to is skipped.

**Squad failure memory.** A door, trap or battery that one Saboteur failed at (no route, stuck, out of reach, refused, timed out) is left alone by the whole squad for 10 s (`SquadCoordinator.NoteFailure`), not only by the one that failed. In the squad scenario test this cut the routes four Saboteurs planned in 20 s from 10 to 3, because the others no longer repeat the same failed trip.

**Steadier hand-overs.**

- An attack under way carries on out to 10 m and through up to 1 s out of sight; a new one still starts within 8 m.
- A flight stays on offer for 1.5 s after the player stops qualifying.
- For 2 s after a flight, an attack scores half, so a Saboteur that ran does not turn straight back.
- Idle no longer gets the 0.15 momentum: it is the fallback, not a plan. With momentum, any reason to act scoring under 0.25 (a distant sighting, a quiet noise) could never take over from Idle; the probe in the level showed a Saboteur seeing the player at 11 m and staying Idle.

**Traps** go off when the player walks into an armed one; that is S2's `Trap` and was already the case.

### Sabotage before combat (20 October 2026)

**What the level showed.** A probe on 20 October 2026, before this change, put the player 6 m in front of each Saboteur in turn on the real level (journey start, every door as the level opens it) and logged every decision for 6 s with a full blaster, then 6 s at 40% ammunition:

- **AttackPlayer was the only action any Saboteur was offered** besides Idle and Search; it rose to 0.88 as the Saboteur closed in.
- **Doors.** The grid has doors 1 to 4; 3 and 4 (the Control Room's) start locked and shut. Doors 1 and 2 were each either off the player's route (gain 0) or their only way on (a lockout), so CloseDoor was never offered.
- **Batteries.** A full blaster vetoed every theft. At 40% the nearest batteries still failed the race at top speed (Saboteur A: battery 23 at 8.2 m, the player 9.4 m from it, but 1.9 s against 1.3 s).
- **Traps.** All four start armed (S2's `Interactables.unity`, `startArmed`), so none was available.

**What changed** (all in `AI/Agents/Saboteur/`):

| Part | Rule |
| --- | --- |
| Priority bands | A sabotage whose score after the squad layer is at least 0.15 is mapped into its kind's band: doors 0.66 to 0.74, traps 0.58 to 0.66, batteries 0.50 to 0.58 (`SaboteurBrain.PriorityScore`). The source's score still orders targets within a band. |
| Attack held back | While a sabotage worth doing is on offer, AttackPlayer is multiplied by 0.25 (`AttackDeferFactor`), so the best attack (0.88) plus momentum (0.15) stays under the battery band: a Saboteur attacks only when no sabotage is worth doing. |
| Weak sabotage | Below 0.15 a door, trap or battery keeps its own score and holds nothing back. |
| Flee | The top of the door band plus momentum is 0.89, under the 0.9 emergency threshold, so a badly hurt Saboteur still breaks off to flee. |
| Doors | The ways out of the room the Saboteur shares with the player are offered (CloseDoor above); no last lockout check. Until the same-room rule below, their only way on was offered from anywhere. |
| Traps | An available trap within 10 m of the player is offered, as well as one on their route; 2 m minimum. |
| Batteries | Alerted is enough; no top-speed race; a full blaster scores half. |
| Shut out | A Saboteur that cannot reach any point of its own loop (a door shut behind it, which agents cannot open) patrols the room it stands in, planned like its own, and goes back to its own loop at the next grid change (`SaboteurBrain.IsStranded`). |

The squad layer is unchanged and still applies first: a door claimed by one Saboteur is vetoed for the rest, so a squad mate with nothing else to sabotage attacks at full score.

**What the level shows now.** The same placement after the change, 10 s each, with the player shooting once a second and kept alive:

| Saboteur | First choice on seeing the player | Doors closed | Then |
| --- | --- | --- | --- |
| A | CloseDoor at 0.1 s | 2 | attack |
| B | CloseDoor at 0.2 s | 1 | search, approach, attack |
| C | CloseDoor at 0.2 s (StealBattery 22 also offered, in its band) | 1 and 2 | approach, attack |
| D | CloseDoor at 0.3 s (StealBattery 24 below 0.15, not banded) | 2 | search, attack |

While A's run went on, squad mates took batteries 22 and 25. Then the player reopened every unlocked door, cleared all four traps and stood 5 m from the trap nearest the Saboteur for 12 s: trap 11 was claimed and armed again by the squad in A's and C's runs, and C and D each chose StealBattery once alerted: D took battery 25; C walked for battery 23 for 9 s without reaching it before the run ended. Traps 12 and 13 were not armed in D's and B's runs: they had just shut the door between themselves and the trap, or the walk was past 20 m. Before the shut-out fallback, A stood in Hold for most of its 12 s after shutting itself out; with it, A patrolled and searched the room it was in.

**Dependency (S2).** Because every trap starts armed, a Saboteur can only arm one after the player has sprung or cleared it. Starting some traps unarmed would let the squad lay them from the start; that is S2's scene and has not been changed.

### Bodies in the way and same-room doors (20 October 2026)

**Bodies in the way.** The grid does not know about other agents, so a route could run straight through a Guard, Tracker or Captain lying knocked out (6 to 8 s), a squad mate standing still, or a scrapped Saboteur before it sinks away. A probe on the real level dropped a knocked-out Guard 1.5 m ahead on a Saboteur's patrol walk, three times for each Saboteur, with the other Saboteurs scrapped and the player out of the way, and watched 20 s:

| Code | Walks that got past to their end | Longest standing still |
| --- | --- | --- |
| Before (sabotage before combat only) | 1 of 12 | worst per Saboteur 11.6 to 19.6 s |
| Route cost and waiting, before legs (two runs) | 2 and 1 of 12 | up to 19.9 s |
| After (route cost, legs, waiting) | 11 of 12 | 4.0 s at most |

The first attempt showed why routing alone was not enough: the brain routed round the body within 0.7 s, but the runtime string-pulls every path of more than two waypoints over the grid (`AgentController`, S4's), and the grid has no body in it, so the detour was pulled straight back through the body. What the brain does now (all in `AI/Agents/Saboteur/`):

| Part | Rule |
| --- | --- |
| Route cost | `SaboteurRouteCost` (an `ICostModel`, like the Captain's) prices a step within 1.1 m of a standing agent (below 0.3 m/s; a knocked-down one is at 0) at 21 base steps, so A* goes round it when there is a way. The bodies come from the runtime through `ICrowdAware`, as for the Captain. |
| Body ahead | A standing body on the next 3 m of the route asks for a new route at once, at most once a second, instead of after the 4 s stuck wait. |
| Legs | A route that passes within 2 m of a standing body is sent a leg at a time: two waypoints, which the runtime walks as they are, from the body to the furthest route cell it can walk to in a straight line clear of walls and bodies. Reaching the end of a leg sends the next. Routes away from bodies are sent whole, as before. |
| No way round | A route that would go deeper into a body than the Saboteur already is counts as no route: the patrol tries its other points and, if none can be reached, holds and looks again every second until the body gets up or sinks; a door, trap or battery behind it ends as a failure; a search looks round from where it is. |
| Unknown obstacles | A walk that makes no progress for 4 s prices the next 1.5 m of its route as blocked for 8 s, so the route sent again goes round whatever is there (a scrapped body the runtime no longer lists, a crate the grid does not know). |

The one walk that did not reach its end after the change (Saboteur D, first run) did not stand still either: it kept moving (longest stop 0.1 s) but did not arrive within the 20 s.

**Same-room doors.** A Saboteur closes a door only from inside the room it shares with the player, and only that room's ways out (CloseDoor above). The level probe put each Saboteur in turn (the others scrapped) 2 m from door 2 on the Storage side, with the player 4 m from it on the far side, shooting once a second, for 10 s:

| Code | Door 2 | Where the Saboteur ended |
| --- | --- | --- |
| Before | Closed by all four from their own side, within 0.5 s | Outside the player's room, every time |
| After | Closed by all four from inside the player's room, at 1.7 to 2.3 s, after walking through to them | In the player's room, then attacking |

**On an errand.** A first run of the fix left three of the four standing beside the player for the whole 10 s: the runtime's stand-off (`PlayerStandOff`, S4's) holds a body still within 1.4 m of the player, so a Saboteur that had come into the room could not walk back past them to the door. The brain now sets `IgnorePlayer` on its door, trap and battery intents, the flag the Tracker uses while it watches a thrown toy, so the body walks on and does not swing at the player on the way.

With the player in a room far from the tested Saboteur (beside another one's home), no Saboteur entered CloseDoor, before or after the change. With the player 6 m in front of it, A, C and D each closed a way out of their own room and stayed in it; B's run put the player inside a wall and says nothing either way.
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

### Edge-case sweep (15 October 2026)

Each case above has a test. The new ones are in `SaboteurEdgeCaseTests` (EditMode) and `SaboteurEdgeSceneTests` (PlayMode, the real level); the others were already covered, as listed.

| Case | Covered by |
| --- | --- |
| Door closed by someone else before the Saboteur gets there: plan ends, claim released, no cooldown, door offered again when reopened | `SaboteurEdgeCaseTests` |
| Player steps into the doorway while the Saboteur is walking, or already asking to close: the close is withdrawn on that tick, claim released, no cooldown; offered again once they leave | `SaboteurEdgeCaseTests`, and `SaboteurDetourTests` for the source rule |
| All scores below Idle: Idle wins, patrols, no claim; a candidate just above Idle still wins | `SaboteurEdgeCaseTests` |
| No blackboard, unknown player, dead player (during a door plan, a trap walk, and for a stale detour cache): every player-dependent source is silent, plans end, the Saboteur patrols | `SaboteurEdgeCaseTests`, `SaboteurDetourTests`, `SaboteurTrapBatteryTests` |
| No objective | `SaboteurDetourTests`, `SaboteurTrapBatteryTests` |
| Battery collected or trap armed on the way | `SaboteurTrapBatteryTests` |
| Claim holder destroyed on the way to a trap or battery: claim released at once, the next instance takes it; in the level, Saboteur A scrapped while holding a battery claim releases it and drops the keycard once on a walkable cell | `SaboteurEdgeCaseTests`, `SaboteurEdgeSceneTests`, `SaboteurSquadTests` |
| Keycard next to a box, nothing walkable nearby, destroyed before the first tick | `SaboteurDropTests` |
| All four destroyed: claims released, destroyed brains only return a stop and never ask the detour cache again, only A drops the keycard (once), the all-Saboteurs bonus is awarded once, the other agents and the game carry on | `SaboteurEdgeCaseTests`, `SaboteurEdgeSceneTests` |
| Destroyed twice, or stunned, resolved or told of a graph change afterwards: nothing changes, the drop stays where it was fixed | `SaboteurEdgeCaseTests` |
| Cutscene or pause mid-action: a request in reach is not timed out by the pause, attack pacing resumes from the same game time, a repeated tick at the same time neither swipes nor decides twice; in the level, a cutscene mid-attack freezes the body and the brain and it carries on after | `SaboteurEdgeCaseTests`, `SaboteurEdgeSceneTests`, `GuardFreezeTests` (the freeze) |

**Two defects the sweep found and fixed.** (1) `TryCloseDoor` checked the doorway only when a decision was made (4 Hz), so a player who stepped into a doorway just after the Saboteur had started asking could still have the door shut on them; it now checks on every tick and ends the plan without a cooldown. (2) `CloseDoorSource` read the detour cache without checking the player, and the cache is up to half a second old, so a door kept being offered for up to 0.5 s after the player died or went missing; it now offers nothing for a dead or unknown player, like the other sources.

**Left open, with their owner.** A close request the Saboteur has already sent stays open in `AgentController` until it is carried out or given up, even if the brain has since withdrawn it, so a Saboteur that walks back into reach of that door could still close it on a player standing in it. The brain does not know this; whether the controller should drop a request when the brain stops asking is S4's decision.

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
| Player aiming at Saboteur | `clamp01((cos(angle) - cos 40 degrees) / (1 - cos 40 degrees))` from `PlayerSnapshot.Forward` and the direction to the Saboteur, both flattened onto the ground plane. | Flee |
| Own health lost `1 - h` | logistic, steepness 10, midpoint 0.5 | Flee |
| Player distance `d` | Require `d < 14 m`; `1 - d/14`. | Flee |

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

Resolve the objective with the chain: the Captain's `PredictedGoal` if it is current and confident, otherwise the nearest `ObjectiveTargets` entry, otherwise the nearest active switch. If none exists, CloseDoor is ineligible. Both are on the blackboard: `ObjectiveTargetWriter` (Runtime) copies the chapter's active objectives into `WorldBlackboard.ObjectiveTargets`, and the Captain's controller writes `WorldBlackboard.PredictedGoal` through `SetPredictedGoal`. `DetourCache` takes the prediction at a confidence of 0.5 or more, otherwise the nearest objective target; switches are objective targets (`ObjectiveTargetKind.Switch`), so the third step needs no source of its own. EditMode tests still use fixed inputs. For each eligible open door, compare the shortest player-to-objective path in the current world with a **hypothetical** view where only that door is closed:

```text
gain(d) = (closedCost(d) - openCost) / openCost
doorConsideration(d) = clamp01(max(0, gain(d)))
```

The open cost must be finite and positive. Reject stale results, invalid costs, and closures that leave no player route. A zero or negative gain yields zero detour value. Open cost `10` and closed cost `16` in the same search cost units give gain `0.6`; a second door giving `12` has gain `0.2`, so the first ranks higher with equal other inputs.

Reuse the shared A* and base cost model. Do not mutate the live grid while scoring. The current `PathResult` contains cells but no total-cost field, so sum the returned steps with the same cost model. A hypothetical graph view and search scheduling need agreement with S1/S2. Cache the open-route result for the same player cell, objective, and graph version. The comparison is made once per door every 0.5 s for the whole squad by the shared `DetourCache` (see Squad layer), through S2's scheduler once it exists. Invalidate a comparison when its target, objective, or relevant graph cells change. The plan assumes at most six doors and thus up to 12 A* runs per complete pass; defer or cap work if the actual level exceeds the shared search budget. Never use a result from a different graph version.

## Integration boundaries

- Put pure scoring and selection code in `Assets/_Project/Scripts/AI/Agents/Saboteur/` under `ToyFactory.AI.Agents`. The Agents assembly references AI.Core only. Do not duplicate A* or add reverse references.
- Consume `AgentContext` and read-only `WorldBlackboard` facts. The blackboard carries the player (`Player`, written every frame by `PlayerStateWriter`: cell, position, facing, alive, health, ammunition, overcharge), the trap and battery facts (`SabotageFacts`, from `SabotageFactWriter`), `ObjectiveTargets` (`ObjectiveTargetWriter`), the Captain's `PredictedGoal` and the squad's `Claims`. Door state is read from the grid (`IsDoorClosed`). The player's aim is not reported, so the brain works it out from `PlayerSnapshot.Forward` (see Flee). `SensorSnapshot` carries only hearing, which the Saboteur does not use.
- Return `AgentIntent` with path, look target, action, target ID, and concise debug state. The action enum supports `CloseDoor`, `ArmTrap`, `StealBattery`, and `Shoot`; movement-only Flee and Idle/Patrol use `None`. `AgentController` passes the real cell under the body, carries out `CloseDoor`, `ArmTrap` and `StealBattery` through `SabotageTargets` and answers each request once through `IActionFeedback`; `Shoot` drives S4's claw swipe and gets no answer. It reports the brain's own health through `IHealthAware`.
- Runtime adapters supply physics, sight, doorway occupancy, aiming, interaction range, and hypothetical door state. The brain neither touches scene objects nor calls door or trap methods directly.
- On `OnGraphChanged`, replan movement only when changed cells affect its current route; invalidate a door comparison when changed cells affect a compared route or doorway. On `OnStunned`, cancel pending action and enter Stunned.

### Handoffs this design depends on

| Need | Owner | Status |
| --- | --- | --- |
| `TargetClaims` reachable through `WorldBlackboard` | S2 | Done: `WorldBlackboard.Claims`, passed to the brain by the spawner. |
| Player state (position, cell, ammo, overcharge, alive, aiming), door/trap/battery state | S2 | Done: `PlayerStateWriter` writes `WorldBlackboard.Player` every frame (cell, position, facing, alive, health, ammunition, overcharge); door state is on the grid; trap and battery state is `world.SabotageFacts` (row below). Aiming is worked out from `PlayerSnapshot.Forward`. EditMode tests use fake snapshots. |
| Pathfinder, grid and instance identity (letter, claim id, keycard carrier) given at spawn; an id on `IAgentState` | S4 (with S1 `GridManager`) | Done: `BrainSetup` and `AgentIdentity` (`IAgentState.Identity`); see Spawn hookup. |
| Action execution and success/failure feedback | S4 controller, S2 targets | Done (S4, #217): `AgentController` carries out door, trap and battery actions through `SabotageTargets` and answers through `IActionFeedback`; the brain uses it for door cooldowns. It also tells the brain its health through `IHealthAware`. |
| `ISabotageable` on traps and batteries; their facts on the blackboard | S2 | Done: traps and batteries are executable targets, and `world.SabotageFacts` lists each one's id, cell, kind and availability. |
| `ObjectiveTargets`; `PredictedGoal` with confidence | S1; S4 | Done: `ObjectiveTargetWriter` fills `WorldBlackboard.ObjectiveTargets` from the chapter's objectives, and the Captain's controller writes `PredictedGoal` through `SetPredictedGoal`. EditMode tests use a fixed objective. |
| Game time in `AgentContext` | S4 / S2 | Done: the controller passes `GameClock.Current.GameTime`, which stops during cutscenes and pause (`Time.time` only in a scene with no GameManager). |
| Infinite-cost step behaviour in `AStarSearch` | S2 | Tested, no change needed (see Hypothetical door closure); S2's confirmation outstanding. |

### Implementation status

| Part | Status | Code and tests |
| --- | --- | --- |
| Response curves and curve settings | Implemented | `ResponseCurve`, `SaboteurCurveSettings`; `UtilityCurveTests`, `SaboteurCurveSettingsTests` |
| Considerations, compensation, selection, momentum, commitment, door cooldown | Implemented | `Consideration`, `UtilityAction`, `ActionSelector`; `UtilityScoringTests`, `ActionSelectorTests` |
| Brain skeleton: identity, 4 Hz selection, Idle/Patrol, stun, graph changes, destruction | Implemented | `SaboteurIdentity`, `SaboteurBrain`; `SaboteurBrainTests` |
| Decision trace for the debug panel: each candidate's raw and base score, each consideration's input and score, ranking, and why a candidate lost | Implemented; filled by the brain and `ActionSelector`, no allocation per decision | `UtilityDecisionTrace`, `SaboteurBrain.LastDecision`; `UtilityDecisionTraceTests` |
| AttackPlayer | Implemented, tested and wired into the level through `AgentSpawner.CreateBrain` (see Spawn hookup) | `AttackPlayerSource`, `IPlayerSight`, `CoverVisibilitySight`, `SaboteurBrain`; `SaboteurAttackTests` |
| CloseDoor | Implemented, tested and wired into the level; uses `IActionFeedback`. Offered in every chapter, only to a Saboteur in the player's room, for that room's ways out (see Bodies in the way and same-room doors) | `CloseDoorSource`, `CompositeCandidateSource`, `DetourCache.For`, `SaboteurBrain`; `SaboteurDetourTests`, `SaboteurFeedbackTests`, `SaboteurDoorSafetyTests`, `SaboteurEdgeCaseTests`, `SaboteurPriorityTests`, `SaboteurDoorSceneTests`, `SaboteurChapterFourSceneTests` |
| Flee | Implemented, tested and wired into the level through `AgentSpawner.CreateBrain` (see Spawn hookup). The keycard carrier's bonus is a starting value | `FleeSource`, `FleePlanner`, `SaboteurBrain`; `SaboteurFleeTests`, `SaboteurFleeEvidenceTests`, `SaboteurFleeSceneTests` |
| Saboteur layer on the F3 debug overlay: each Saboteur's squad row (letter, action, health, destroyed) and its latest decision | Implemented; registers itself, no edit to the overlay. Shows each action's raw and final score and why it lost, not the individual considerations | `UtilityDecisionText`, `SaboteurOverlayLayer`; `UtilityDecisionTextTests`, `SaboteurOverlayLayerTests` |
| Squad scenario: four Saboteurs, two doors, a way round each, with the real sources, cache, claims and A*; searches for one and four instances; tick cost; anti-dithering | Implemented, measured | `SaboteurSquadScenarioTests`, `Test_SquadClaims.unity`, `SquadClaimsSceneTests`, `SquadScenarioObjective`; results in `Docs/AIPerformanceLog.md` |
| Detour cache spread over ticks (one A* search per call, results published together after the last) | Implemented, measured | `DetourCache.SearchesPerCall`; worst call on a level-sized grid 8.09 ms -> 4.69 ms; see `Docs/AIPerformanceLog.md` |
| ArmTrap, StealBattery | Implemented, tested and wired into the level through `AgentSpawner.CreateBrain`; they use `IActionFeedback`. Scores are starting values; no battery is carried or dropped | `ArmTrapSource`, `StealBatterySource`, `SabotageTargetCells`, `DetourCache.PlayerRoute`, `SaboteurBrain`; `SaboteurTrapBatteryTests`, `SaboteurBatterySceneTests` |
| Squad layer: claim on commit, "not claimed" veto, displacement check, release, attack saturation, tick stagger | Implemented in the brain: claims on commit, the veto and saturation applied to every candidate, stagger by letter, release on stun and destruction. The candidates come from an `ICandidateSource`; in the level that is five sources in one `CompositeCandidateSource` | `SquadCoordinator`, `ICandidateSource`, `SaboteurBrain`; `SquadClaimTests`, `SaboteurSquadTests` |
| Keycard drop through `IDropsItems` | Implemented; no battery is dropped, because StealBattery does not carry one | `IDropsItems`, `SaboteurBrain.GetDrops`; `SaboteurDropTests` |
| `DetourCache` and `DoorClosureCostModel` | Implemented and tested; refresh at most every 0.5 s, searches only for doors on the route, `AI.Saboteur.Detour` profiler marker | `DetourCache`, `DoorClosureCostModel`; `SaboteurDetourTests` |

The skeleton's behaviour:

- **Selection.** At most every 0.25 s of `AgentContext.Time`, the brain rebuilds its candidates and asks `ActionSelector` for a choice. The candidates are Idle/Patrol at 0.1, which the brain always adds, and whatever its `ICandidateSource` offers; in the level that is AttackPlayer, Flee, CloseDoor, ArmTrap and StealBattery.
- **Patrol.** Patrol points are given in world space and converted to cells once. Points outside the grid are dropped; a blocked point snaps to the nearest free cell within 2 cells. A leg counts as finished when the Saboteur is within 0.5 m of its last waypoint on the ground plane, because the body stops up to 0.3 m short and may still be in the previous cell; `AgentContext.Cell` is only used as the start of the next search. Routes come from the injected `IPathfinder` with `BaseCostModel`, and are sent once per leg (`Path = null` keeps the current route). An unreachable point is skipped. If no point is reachable the Saboteur holds, and A* is retried only on the next selection pass or grid change, never every tick. With no patrol points it holds in place.
- **Debug data.** Every selection pass overwrites `SaboteurBrain.LastDecision`: the candidates in the order they were scored, their raw and compensated base scores, each consideration's name, input and score, the ranking score (base plus momentum), whether the commitment window was active, and for each candidate that did not win one reason: `Vetoed`, `OnCooldown`, `CommitmentHeld` or `Outranked`. The brain and selector write the numbers they already computed, so the panel only displays them. Candidates that are ineligible before scoring (for example a target claimed by another Saboteur) are not candidates, so they are not in the trace; the candidate sources do not record them. Storage is allocated once per brain; a test checks that 1,000 passes allocate 0 bytes.
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

### AttackPlayer

`AttackPlayerSource` offers AttackPlayer when the player is known and alive, the ground-plane distance is under 8 m (height is ignored) and the player has a line of sight to the Saboteur. Two considerations through `UtilityAction`: `1 - d / 8` for the distance and the Saboteur's own health fraction (`IHealthAware`, full health until the controller says otherwise). At full health a player 2 m away gives raw 0.75, compensated 0.84; at half health raw 0.375, compensated 0.49. The score meets Idle (0.1) at about 7.2 m at full health, and a damaged Saboteur stops attacking from nearer. A Saboteur with no health left offers no attack. The squad layer then multiplies the score by 0.45 when two other live instances are attacking: a Saboteur 7.04 m away (raw 0.12, compensated 0.17) idles once two others attack, and attacks when it is the first.

- **Line of sight.** The brain asks `IPlayerSight.CanSeeCell(cell)`. `CoverVisibilitySight` answers it from the Guard's `ICoverVisibility` (a ray from the player's eye to a point 1 m above the Saboteur's cell, characters skipped), so the runtime's one physics sight check, S2's `PhysicsCoverVisibility`, serves both agents and the brain stays free of physics.
- **Intent.** While AttackPlayer is selected and the player is in range, `Tick` sets `LookTarget` to the player and either walks at them or, within 2 m, stops the body once (an empty path) and requests `AgentAction.Shoot` at most every 1.2 s (`AttackIntervalSeconds`); see Melee reach below. It is checked every tick, so a dead player or one who steps out of range ends the attack at once rather than at the next 4 Hz decision. When the attack ends, the next tick routes the patrol afresh.
- **Own health.** The controller reports the Saboteur's hit points through `IHealthAware.OnHealthChanged`, and the brain passes them to its sources: own health is AttackPlayer's second consideration (above) and Flee's input. There is no attack-ready cooldown beyond the 1.2 s pacing, because the runtime reports no result for `Shoot`.
- **Melee reach (B15).** The claw swipe is S4's `AgentClawSwipe`: it deals its 10 damage itself at contact and needs no `AgentWeapon`, but it only lands within the body's stand-off plus its lunge. On the Saboteur prefab that is 1.6 m + 0.6 m = 2.2 m, with a swipe every 1.2 s. The first version stopped the body wherever the player came into range (up to 8 m) and asked for `Shoot` from there, so the swipe never reached and the Saboteur dealt no damage. Now `Attack` closes in first, with the numbers as named constants on `SaboteurBrain`:
  - Beyond `MeleeReachMetres` (2.0 m, inside the 2.2 m the swipe lands, with slack for the body's travel between ticks) it walks at the player along an A* route to the player's cell and never asks for `Shoot`. The runtime's `PlayerStandOff` (`MeleeStandOffMetres`, 1.6 m) holds the body in front of the player, so the route does not need to stop short. The route is sent once and searched again only when the player has moved to another cell, at most every `ChaseReplanSeconds` (0.5 s, so four chasers cost at most eight searches a second), or after a graph change. If there is no route to the player the body is stopped once, no swipe is asked for, and the search is tried again after the same delay.
  - Within `MeleeReachMetres` it stops the body once, faces the player and asks for `Shoot` at most every `AttackIntervalSeconds` (1.2 s, the prefab's swipe interval; it was a provisional 1.5 s). The pacing is shared with the chase, so stepping in and out of reach cannot ask for a swipe before the last one has finished. A player who backs out of reach is chased again at once.
  - `DebugState` stays `AttackPlayer` in both phases. The attack score and its 8 m range are unchanged: the Saboteur still decides to attack from 8 m, it just walks the distance first.
  - A cornered Saboteur (Flee selected, no refuge) swipes only if the player is within 2 m; farther away it holds, since a swipe would not reach.

**Limits.** In the level the candidates are Idle, AttackPlayer, Flee, CloseDoor, ArmTrap and StealBattery. Flee is offered whenever the player faces a hurt Saboteur it can see, so in play it shows once the Saboteur has taken a hit. **When CloseDoor can fire.** Probed on 15 October 2026 on the real grid, in Chapter 4 with every door open, over 436 player and objective pairs (a lattice of player cells against the four real objectives): no door was ever a lockout; doors 1, 2, 3 and 4 had a finite, positive detour for 43, 69, 157 and 167 pairs, up to a gain of 0.62, 0.68, 3.98 and 5.80. The four rooms form a ring (Assembly - door 1 - Painting - door 2 - Storage - door 3 - Control - door 4 - Assembly). Doors 3 and 4, the Control Room's entrances, are locked and closed until the Chapter 3 cutscene (`LevelLayout.md`), so **before Chapter 4 every open door is the only link between its rooms**. Until 20 October 2026 such a closure counted as a lockout and CloseDoor was not offered (this is what the 10 October probe saw); it is now offered at full value, as the player's only way on (see Sabotage before combat). **In Chapter 4 it can fire**, and `SaboteurChapterFourSceneTests` shows it on the real map with no wall cut: a player deep in Assembly heading for the Control Room has door 4 closed by Saboteur A, the long way round stays open, and the squad does not go on to shut it; with the player in Storage, door 3 is closed. Since 20 October 2026 a second Saboteur may also close door 4, now the only way into the Control Room, as long as no player stands in it. The player can always reopen a door with Interact. `SaboteurDoorSceneTests` still cuts a gap through the wall next to door 1 so it can test the chain in the earlier layout. The claim behaviour is exercised in `SaboteurSquadTests`, `SaboteurDetourTests` and `SaboteurFeedbackTests`.

### Spawn hookup

`AgentSpawner.CreateBrain(point, setup)` builds the Saboteur brain from the `BrainSetup` the spawner passes to every case. The Saboteur case is S3's to replace (S4 confirmed in writing on 4 October):

```csharp
if (setup.HasGrid && setup.Identity.IsInSquad && setup.Identity.SquadIndex <= (int)SaboteurLetter.D)
{
    DetourCache detours = DetourCache.For(setup.Blackboard, setup.Grid, setup.Pathfinder);
    var candidates = new CompositeCandidateSource(
        new AttackPlayerSource(new CoverVisibilitySight(new PhysicsCoverVisibility(setup.Grid))),
        new FleeSource(new CoverVisibilitySight(new PhysicsCoverVisibility(setup.Grid))),
        new CloseDoorSource(detours),
        new ArmTrapSource(setup.Grid, detours),
        new StealBatterySource(setup.Grid));
    return new SaboteurBrain(
        new SaboteurIdentity(setup.Identity.Id, (SaboteurLetter)setup.Identity.SquadIndex),
        setup.Grid, setup.Pathfinder, setup.Blackboard.Claims, setup.PatrolPoints, null, 0,
        candidates, detours);
}
return new MockPathProvider(setup.PatrolPoints);
```

- The claim owner id is the spawn-order `AgentIdentity.Id`, and the letter is the spawn point's squad slot (0 = A to 3 = D). The four slots are set on the spawn points in `Agents.unity` (S4's scene) and `Test_AgentSpawner`.
- All four brains get the blackboard's one `TargetClaims`, so `SquadCoordinator.For(claims)` makes them one squad. Each level load builds a new blackboard, so a new squad.
- The grid and pathfinder are the level grid and the one `AStarSearch` the spawner shares with every brain.
- Without a grid, or for a Saboteur spawn point with no squad slot or a slot past D, the case keeps the mock. A mis-set spawn point never becomes a second Saboteur A, and it does not stop the other agents spawning, as an exception from `SaboteurIdentity` inside `SpawnAll` would. The spawner already logs an error when two Saboteur spawn points share a slot.
- Each Saboteur gets its own `AttackPlayerSource` and `FleeSource`, each over its own `PhysicsCoverVisibility` (the Guard's physics sight check), and its own `CloseDoorSource`, `ArmTrapSource` and `StealBatterySource`, so the brains share no scratch buffers. All four share one `DetourCache`, found with `DetourCache.For(blackboard, grid, pathfinder)` (a weak table keyed by the level's blackboard, like `SquadCoordinator.For`), so the squad costs the searches of one Saboteur.
- The keycard item id stays at its default (0). S2's `KeycardDrop` places the keycard from `AgentEvents.OnDestroyed`, and nothing in the runtime calls `IDropsItems.GetDrops` yet.

Checked on 9 October 2026 in batch-mode Play, in `Test_AgentSpawner` (6 s) and in `Agents.unity` loaded from Bootstrap with the intro skipped (8 s): all four Saboteurs ran `SaboteurBrain` as A-D, reported the Patrol state and logged no console errors.

With the attack wired in, `SaboteurAttackSceneTests` (PlayMode, real scenes from Bootstrap) puts the player 4 m from Saboteur A in the open: it switches to `AttackPlayer` and reports the Alert level, and when the player is moved 40 m away it returns to Patrol. In the whole PlayMode suite (202 tests) the only failure is `AgentAnimationTests.WheelsRollWithoutSlippingAsTheAgentMoves`, which fails the same way without this change. That first version dealt no damage, because it asked for a swipe from wherever the player came into range. Since B15 (#281, 15 October) the Saboteur closes to 2.0 m before it swipes, and `SaboteurClosesInOnAPlayerWhoStandsStillAndHurtsThem` shows a still player 4 m away taking damage (see Melee reach).

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
| Hurt Saboteur, player within 14 m facing it, in sight | Flee is a candidate scoring about 0.75 at 5 m and health 0.25; a player looking away, out of range, out of sight, dead or unknown removes it. |
| Keycard carrier A against B at the same health | A's score is B's x 1.25, capped at 1; an unhurt A still scores below Idle. |
| Player 3 m east, open grid; a one-cell corridor with the player in the only way out; a sealed 3 x 3 pocket | The retreat ends 3 m or more farther from the player; no route when it leads through the player; no route when cornered, and the brain then fights (a swipe only when the player is within 2 m). |
| Open route `10`, closed routes `16` and `12` | First door ranks higher with equal other considerations. |
| Player in doorway; door already closed; zero open cost; unreachable objective; stale route | CloseDoor candidate rejected; no live grid mutation. |
| Battery collected or trap armed during travel | Current pair invalidates immediately and fresh decision follows. |
| Missing/dead player, absent objective, unreachable target, stun | No invalid action intent; valid fallback or independent action remains; stun suppresses actions. |
| Unrelated versus route-intersecting grid change | Only affected path/comparison is requested again. |
| Runtime reports action failure versus success; target shut or taken by someone else | Either answer starts the cooldown, so a failed target is not retried at once; a target that becomes invalid on its own starts none; emitting the intent never does. |
| Two instances want one door | Only one holds the claim; a tie goes to the lower id; the other instance's candidate is vetoed. |
| A holder is outscored | Its next tick sees `ClaimedBy` differ and cancels the plan. |
| Third instance in AttackPlayer | Its score is multiplied by `0.45`. |
| Instance destroyed holding claims | All its claims are released; a carried battery is dropped (none is carried yet). |
| Saboteur A destroyed next to a box | Keycard lands on the nearest traversable cell. |
| One versus four instances | `DetourCache` A* count is the same. |

During integration, check that intents move the Saboteur, execute on real targets, respect doorway occupancy, and display the computed scores. Record actual decision time, search count/time, replan count, and action choices in `Docs/AIPerformanceLog.md` with machine and scenario. This design makes no runtime or performance claim.
