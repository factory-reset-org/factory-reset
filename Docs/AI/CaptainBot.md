# Captain Bot

Owner: S4 · Architecture: Goal prediction + Dijkstra fields + A* intercept

## Role in gameplay

The Captain is the boss of the Control Room area. It does not chase the player. It watches where the player is going, predicts which objective they are heading for, and gets to a point on their route first.

- **Journey role:** the Captain is **Dormant** in the Control Room for Chapters 1 and 2. It wakes on camera in the Chapter 3 cutscene, after the second switch is restored. From then on it intercepts the player through Chapters 3 and 4.
- **Candidate goals:** the current chapter's active task targets (for example the keycard, the next relay in the sequence, a power core), the unrestored switches, the console, and battery pickups when the player's ammo is below 30%.
- **Threat:** the player meets the Captain in front of them, at a chokepoint, rather than being followed from behind.
- **Pacing:** the Captain only commits to an ambush when it is confident. Otherwise it keeps its distance and observes, so it never feels like it is cheating.

## Creative hook

**The gameplay problem:** a boss that chases is predictable. The player always knows where it is (behind them) and can beat it by simply outrunning it or looping around cover.

**The Captain's solution: it predicts where you are going, not where you are.** Every other enemy reacts to the player's position. The Captain reasons about the player's *intention*:

1. It compares the route the player has taken over the last 5 seconds with the shortest route to each goal. A player who stays on the shortest route to the red relay is probably going to the red relay. Because the goals include chapter tasks, the Captain guesses which *task* the player is doing next, not only which switch.
2. It works out which chokepoints on that predicted route it can reach at least 1 second before the player.
3. It walks there and waits, facing the way the player will come.

**Why it is surprising to play against:** the player expects the enemy behind them and instead finds it waiting in front of them. Its tactics also change with its confidence: it shadows the player while unsure, and commits to an ambush once the prediction is strong.

**Counter-play:** the prediction is based on shortest routes, so a player who takes an unexpected route lowers the Captain's confidence and can slip past it. The Captain is beatable by a player who realises it is reading their movement.

**Team play:** the Captain publishes `PredictedGoal` (goal and confidence) to the shared blackboard at 2 Hz. The Saboteur squad uses it to measure which door closure forces the longest detour. When the prediction is missing or not confident, the squad falls back to the nearest `ObjectiveTargets` entry, then to the nearest active switch. Nice-to-have: the Tracker investigates the predicted goal when it has nothing to hear.

**Decoupled from the story:** the Captain learns about the chapters only through the `ObjectiveTargets` blackboard entry written by S1's `ChapterManager`. The brain never references any Journey code, and the asmdefs make that a compile error.

## Architecture

The Captain is a finite-state machine built on the shared FSM framework: each state is an object with `Enter`, `Tick` and `Exit`, and transitions are data (condition + priority + target), not a `switch` statement. Every tick the machine takes the highest-priority transition whose condition is true.

**Confidence** below means the probability of the most likely goal, `P(g*)`, from goal inference (see Maths to defend). It is recomputed at 2 Hz.

### States

| State | What the Captain does |
| --- | --- |
| **Dormant** | Start state. Stands powered down in the Control Room and ignores the player. The brain skips goal inference and returns an empty intent. Leaves only when the Chapter 3 wake signal arrives. |
| **Observe** | Prediction is too uncertain to commit. Faces and watches the player and backs off 4 m whenever they come within 8 m, so it never simply chases while the prediction settles. |
| **Intercept** | Confident about `g*`. Picks the first chokepoint on the player's predicted route it can reach at least 1 s before them, and walks there with A*. If no cell qualifies, it heads to `g*` itself to defend it, but only if it can reach `g*`. If it has no route yet (a door was still shut), it asks again at every decision. If it does not move for 2 s, it gives the cell up (see Getting unstuck). |
| **Ambush** | At the intercept cell. Stands still, facing the route cell the player will arrive from (the body turns to the brain's look target while standing, so it can see them coming). Holds its ground while the cell is still ahead of the player on their predicted route. |
| **Converge** | The player is busy at a goal they reached (a task, the console hold) and nothing else is predicted. Closes in on them at intercept speed until they are in view, then fights. Gives up for 10 s if it gets stuck on the way. |
| **Engage** | Starts when the player is in view within 10 m (a 70° half-angle cone, or anywhere within 2.5 m, with line of sight traced on the grid). Faces the player and asks for a shot every 1.2 s while in contact; the body's weapon aims for 0.3 s (the telegraph, the same for every agent) before each hitscan shot. Once started, contact (line of sight within 14 m, in any direction) keeps it going, and it lasts at least 2 s. Out of contact it faces where it last saw the player and holds fire. |
| **Pursue** | Lost contact for 0.7 s mid-fight. Walks at intercept speed to where it last saw the player, then stands there for 1 s looking the way they were heading. Contact again returns it to Engage; otherwise it predicts again (Reassess), after that look or 5 s at most. |
| **Reassess** | Something invalidated the plan. Discards the current intercept cell, re-runs goal inference immediately, then hands over to Observe or Intercept. Lasts one decision tick. |
| **Stunned** | Knocked out. The controller owns the 6 s reboot and does not tick the brain meanwhile; the brain drops its plan and prediction at once. On the first tick after the reboot it passes straight through to Reassess. |

**Alert icon:** the brain sets `AgentIntent.Alert`: "!" once it has committed to the player (Intercept, Ambush, Converge, Engage, Pursue), "?" while it watches and re-predicts (Observe, Reassess, with a player present), nothing while Dormant or down.

**Waking up:** the Chapter 3 cutscene fires the `CaptainWake` Critical signal. The runtime's `CaptainWakeWriter` turns it into `WorldBlackboard.CaptainAwake`, and the brain reads the flag. The signal fires even when the player skips the cutscene. As a second safety net the Captain also wakes once Chapter 3 has started (`ChapterIndex ≥ 3`), so it can never stay asleep for the chapters it guards. Test scenes have no cutscene, so a spawn point can start it awake.

**Cutscenes and pause:** `AgentController` does not tick any brain unless the game state is Playing (so not in Title, Cutscene, Paused or Results), and the body holds its route while frozen. All Captain timers use `AgentContext.Time`, which is game time from S2's `GameManager`, so the 5 s history window, the 2 Hz prediction and the stun reboot resume where they stopped.

### Transitions

Higher priority wins when several conditions are true on the same tick.

| From | To | Condition | Priority |
| --- | --- | --- | --- |
| Dormant | Observe | Wake signal received | 110 |
| Any except Dormant | Stunned | Hit points reach 0 | 100 |
| Stunned | Reassess | Stun timer ends | 90 |
| Observe, Intercept, Ambush, Converge | Engage | Player visible within 10 m | 80 |
| Pursue | Engage | Contact again (line of sight within 14 m) | 80 |
| Engage | Reassess | Player dead or missing | 75 |
| Engage | Pursue | No contact for 0.7 s, after at least 2 s engaged | 70 |
| Pursue | Reassess | Looked round the last-seen spot for 1 s, 5 s passed, no route there, stuck for 2 s, or the player is gone | 65 |
| Intercept, Ambush | Reassess | `g*` changes by a clear margin, confidence drops below 0.4 (so there is no plan), the player reaches `g*`, the intercept cell becomes blocked, (Intercept) stuck for 2 s, or (Ambush) the player has passed the cell | 60 |
| Converge | Reassess | The player left the goal, or the way to them is blocked | 60 |
| Intercept | Ambush | Captain reaches the intercept cell | 50 |
| Reassess | Converge | The player is busy at a goal | 45 |
| Reassess | Intercept | Confidence ≥ 0.5 | 40 |
| Reassess | Observe | Confidence < 0.5 | 30 |
| Observe | Converge | The player is busy at a goal | 25 |
| Observe | Intercept | Confidence ≥ 0.5 | 20 |

```text
  Dormant ── wake signal (Chapter 3 cutscene, also applied on skip)
     │
     ▼      confidence ≥ 0.5              reached cell
  Observe ───────────────────▶ Intercept ─────────────▶ Ambush
     ▲                            │  ▲                    │
     │ confidence < 0.5           │  │ confidence ≥ 0.5   │ goal changed /
     │                            ▼  │                    │ confidence dropped
     └──────────────────────── Reassess ◀─────────────────┘
                                  ▲
      searched, or 5 s passed     │        stun ends
  Pursue ─────────────────────────┴──────────────────── Stunned
   ▲  │ contact again
   │  ▼
  Engage ── no contact for 0.7 s (after at least 2 s) ──▶ Pursue
     ▲
     └── any of Observe / Intercept / Ambush: player visible within 10 m
```

**Why the fight has hysteresis.** At first Engage started at 10 m in view and ended after 0.7 s out of view at the same 10 m. A player walking in and out of 10 m switched it on and off: Engage, then Observe backing off to 8 m, then Engage again, which looked irrational. Now starting and keeping a fight use different thresholds, as a thermostat does:
- It starts at 10 m in view and keeps going out to 14 m with line of sight, so a 4 m band separates the two.
- It lasts at least 2 s once started.
- When contact is lost, it goes where it last saw the player instead of backing off.

**Why committing has hysteresis too.** The same edge problem existed for the plan: it was made at confidence ≥ 0.5 and dropped below 0.5. A player weaving between two goals kept the confidence near 0.5. In a scripted run that caused three switches between Intercept and Observe in 13 s, each one a dead stop mid-route, including a turn back towards the other goal. Now a plan is made at 0.5 but kept down to 0.4. While committed, the Captain keeps planning for its goal unless another goal leads it by 0.15. The prediction it publishes for the Saboteurs is still the true most likely goal. The same run now commits once.

**Getting unstuck.** Heading for a target without moving 0.25 m in 2 s (pinned on a prop the grid does not know about, a jam of bodies, no route) gives the target up. Its cell and the eight cells round it are not chosen again for 10 s, through the planner's existing "reserved cell" check. The measure is movement, not distance to the target, because a route round a shelf row can lead away from the target for a while. Pursue and Converge use the same check. This is the safety net, not the fix: solid props must also block the grid.

**Why a goal the Captain cannot reach is no plan.** "Defend `g*`" used to be returned even when the Captain had no path to `g*`, with an arrival time of infinity. It then sat in Intercept with no route, showing "!". This happened when the Chapter 3 cutscene was skipped: the Captain decided before the Control Room door had finished opening. A goal it cannot reach now gives no plan, so it watches. When the door opens, the grid change brings the next decision forward, and Intercept also re-asks for a route at every decision while it has none.

Locking on until the player leaves the room was considered and not used. The brain has no room data; it would turn the Captain into a plain chaser and give up its prediction; and standing in a doorway would beat it. Pursue ends in Reassess, so the Captain goes back to predicting.

## Why this architecture over the alternatives

The gameplay need is a boss that feels smart without cheating: it must anticipate the player, and the player must be able to beat it by playing unpredictably. Each alternative below fails one of those two requirements.

| Alternative | What it would do | Why not |
| --- | --- | --- |
| **Direct chase** (A* to the player's current cell) | Follows the player | Reacts to where the player *is*, not where they are going. It is the predictable chaser the creative hook exists to replace, and the player beats it by outrunning or looping around cover. |
| **Velocity extrapolation** (lead the target) | Aims at `position + velocity · t` | Assumes straight-line movement. In a level of corridors and doors, the player's straight-line projection often goes through walls. The Captain's prediction follows real paths because it uses shortest-path costs through the grid. |
| **"Nearest goal" rule** | Assumes the player goes to the closest active switch | Ignores what the player is actually doing. A player walking away from the nearest switch would still be "predicted" to go there. Goal inference uses the observed route, so it changes its mind when the player does. |
| **Behaviour tree** | Hand-authored priority tree of checks and actions | Organises behaviour well, but does not decide *where* to ambush. The prediction and intercept maths would still be needed inside it. The Captain has few states with clear confidence-based transitions, which a data-driven FSM expresses more simply and can be printed as a table. |
| **Utility AI** | Scores every possible action on one scale | Suited to many competing actions (as the Saboteur has). The Captain has one main question, "where will the player be?", which is a probability over goals rather than a trade-off between actions. |
| **GOAP / classical planning** | Plans a sequence of actions to reach a goal state | Plans the Captain's own action sequence, but the Captain's difficulty is modelling the *player*, not ordering its own actions. It adds planning cost without improving the prediction. |
| **Minimax / MCTS** | Searches the player's possible moves as an adversarial game tree | The real-time 3D level has a huge, continuous move space, so tree search would be too slow for a 2 ms frame budget. Goal inference summarises the player's options as a handful of goals instead. |
| **Learned model** (reinforcement learning, neural net) | Learns to predict or intercept from training data | Needs large amounts of gameplay data and training time the project does not have. The result would be a black box: weights could not be justified in the viva, and the player could not learn a clear counter-play. |

**What the chosen design gives instead:**

- **Anticipation:** the prediction uses the level's real shortest paths, so it respects walls, doors and boxes.
- **Explainability:** every number (β, the 5 s window, the 1 s margin) has a stated reason, and the goal probabilities can be shown live in the debug overlay.
- **Fairness:** the prediction only uses the player's observed movement, never hidden information, and a player who takes an unexpected route can beat it.
- **Cost:** Dijkstra fields are computed once per goal and reused, so each prediction is a few O(1) lookups.

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
- `C(x → g)` and `C(s → g)` are lookups in each goal's field. `C(s → x)` is a single pair of cells, so it comes from one A* query (`OneToOneCost`), bounded at 50 m, not from a field.
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

**Worked example:** three chapter task targets, which share the task prior equally (other goals left out to keep the numbers simple). The player is on the shortest route to A (`D = 0`), 4 m off the route to B, and 6 m off the route to C.

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

The prior `P(g)` is split between goal categories first, then shared equally inside each category:

| Category | Share | Members |
| --- | --- | --- |
| Chapter task targets | 0.60 | Every entry in `ObjectiveTargets` for the current chapter |
| Unrestored switches | 0.25 | Switches not yet restored |
| Console | 0.15 (0.50 in Chapter 4) | The Control Room console |
| Batteries | 0.20, only while ammo < 30% | Battery pickups on the level |

- **Renormalise whenever the goal set changes.** Empty categories are dropped and the remaining shares are scaled so they sum to 1. For example, in Chapter 4 there are no unrestored switches, so the cores (0.60) and the console (0.50) are rescaled to 0.55 and 0.45.
- **Why tasks get most of the mass:** the player spends most of each chapter on its tasks. A uniform prior would waste early confidence on switches that are still sealed.
- **Why the console rises to 0.50 in Chapter 4:** holding E at the console ends the game, so once the cores are down it is the player's final destination.
- **Why batteries only below 30% ammo:** with a full blaster the player has no reason to detour for a battery. Below 30% ammo, recharging becomes a real plan.
- A goal with no reachable path (infinite field cost) is left out of the candidate set.
- Goals appear and disappear as tasks complete. Their Dijkstra fields are built lazily the first time a goal enters the set, cached by goal id, and discarded when the goal leaves.

### Numerical safety

The weights are computed in log space: `log w(g) = log P(g) − β·D(g)`. Before taking the exponent, the largest log-weight is subtracted from every goal's log-weight. This does not change the normalised result, because it multiplies every weight by the same constant, but it gives the most likely goal a weight of exactly 1. The sum is therefore at least 1 and can never underflow to 0, even when every goal has a huge detour (`e^(−0.5 · 1000)` is far below the smallest float). Subtracting the log-weight rather than only the smallest detour also stays correct when the priors differ.

### Intercept point selection

Once confidence ≥ 0.5, the Captain picks where to wait.

1. **Predict the player's route.** Starting at the player's cell `x`, repeatedly step to the neighbour `n` with the smallest `step(x, n) + C(n → g*)`. On a shortest-path field that sum equals `C(x → g*)` exactly for the next cell of a shortest route (picking the neighbour with the lowest `C` alone can take a diagonal that is not on one), so this walks the player's optimal route to `g*`. Ties keep the first neighbour in grid order, so the route is deterministic. Call the cells on it `r_1, r_2, …, g*`.
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
5. **Walk there** with A* (S2's shared `AStarSearch`, called through `IPathfinder` and the path scheduler).

**Why the first qualifying chokepoint:** it is the earliest point where the Captain can be waiting, so it meets the player furthest from their goal and gives the player the least time to notice and reroute.

**Why a 1.0 s margin:** the Captain needs time to stop, turn to face the approach and settle before the player arrives. The margin also absorbs small prediction errors in the player's speed. Without it, the Captain would often arrive at the same moment as the player, which looks like chasing rather than ambushing.

**Why `v_player` is the player's sprint speed:** it is the worst case. If the Captain can beat a sprinting player to a cell, it can also beat a walking one, so the inequality never over-promises.

### Why Dijkstra fields and A* together

| Question the Captain asks | Search needed | Tool |
| --- | --- | --- |
| How far is every cell from each goal? (goal inference, predicted route) | One-to-all | Dijkstra field per goal |
| How soon can I reach every cell on the predicted route? | One-to-all | Dijkstra field from the Captain |
| How far did the player walk in the last 5 s, `C(s → x)`? | One-to-one, cost only | `OneToOneCost` (A* that keeps no route) |
| How soon can I reach `g*` when defending it? | One-to-one, cost only | `OneToOneCost` |
| What path do I actually walk to the chosen cell? | One-to-one | A* |

- **Dijkstra (uniform-cost search)** expands cells in order of cost from its source and gives the exact cost to every reachable cell. One run answers the arrival-time question for the whole route at once. Running A* separately for every route cell would repeat most of the same work.
- **A*** is the efficient choice when there is a single destination. The octile heuristic is admissible and consistent on the 8-connected grid, so A* returns an optimal path while expanding far fewer cells than Dijkstra. It also goes through the same `IPathfinder` and path scheduler as the other agents, so the Captain's movement follows the shared frame budget and replanning rules.

**Cost:** each field is a Dijkstra run, O(V log V) with the binary heap, reading neighbours from a precomputed table (`GridAdjacency`). Goal fields are built once per goal and cached. When a door or a pushed box changes the grid they go stale, and each one is **repaired in place** (`DijkstraField.Refresh`). Only the cells whose cost came through a changed cell are invalidated, and Dijkstra runs again from the edge of that damage, the idea behind LPA* and D* Lite. Stale fields are repaired one per frame, the predicted goal's first, so several fields never land in the same frame. Choosing the intercept cell is then O(L) for a route of L cells, because every step is a field lookup.

**Bounding the Captain's field:** a cell can only qualify if `t_captain(i) ≤ t_player(i) − 1 s`, and no route cell is further for the player than `g*` itself. So the Captain's field stops spreading at `(t_player(g*) − 1 s) · v_captain`. Cells beyond that bound could never be chosen, so the search skips them. If no cell qualifies, the planner times the Captain's walk to `g*` with one A* query (`OneToOneCost`), so it can still defend it.

## Edge cases

| Case | Handling | Test |
| --- | --- | --- |
| Two goals nearly equally likely (top two within 0.1) | Look for a chokepoint shared by both predicted routes and ambush there. If none exists, stay in Observe. | `InterceptPlannerTests.TwoCloseGoalsBehindTheSameDoorwayShareTheChokepoint`, `TwoCloseGoalsWithNoSharedChokepointGiveNoPlan` |
| Player standing still | Every detour is 0, so the posterior equals the prior. The Captain keeps its current plan and does not replan. | `GoalInferenceTests.StandingStillGivesThePrior` |
| Player too close to `g*` (no cell passes the 1 s margin) | Go straight to `g*` and defend it. | `InterceptPlannerTests.NoQualifyingCellDefendsTheGoal` |
| ...and the Captain cannot reach `g*` (shut door, walled off) | No plan: Observe. A door that opens triggers a new decision, and the plan is made then. | `CaptainRobustnessTests.AGoalTheCaptainCannotReachGivesNoPlan`, `ADoorThatOpensAfterTheWakeGetsTheCaptainMoving` |
| Player reaches `g*` (within 1 m) | Remove `g*` from the candidate set and re-predict (Reassess). It counts again once the player is 4 m away or its task completes. | `CaptainBrainTests.GoalThePlayerHasReachedIsLeftOutUntilTheyLeave` |
| Player busy at the only goal (the console hold) | Converge: close in at intercept speed until they are in view, then Engage. | `CaptainRobustnessTests.WhileThePlayerHoldsTheOnlyGoalTheCaptainClosesInAndEngages` |
| Pinned on a prop the grid does not know about, or in a jam | No movement for 2 s gives the target up; its cell and neighbours are avoided for 10 s. | `CaptainRobustnessTests.ATargetItMakesNoProgressTowardsIsGivenUpAndAvoided` |
| Confidence hovering round 0.5 (player weaving between two goals) | Commit at 0.5, keep down to 0.4; switch goal only on a 0.15 lead. | `CaptainRobustnessTests.WeavingBetweenTwoGoalsDoesNotStopAndStartTheCaptain` |
| Route blocked by a pushed box or closed door | Repair the stale goal fields in place, one per frame (predicted goal first), and decide again at once. If the intercept cell is blocked, Reassess. | `DijkstraFieldRepairTests`, `GoalInferenceTests.TheLastPredictedGoalsFieldIsRebuiltFirst` |
| Player steps in and out of the 10 m range | The fight keeps going while in line of sight within 14 m, lasts at least 2 s, and then goes to the last-seen spot instead of backing off. | `CaptainBrainTests.SteppingInAndOutOfTenMetresDoesNotFlipTheFight` |
| Player ducks out of sight mid-fight | Hold fire, face the last-seen spot, then Pursue there; contact resumes the fight; give up after a 1 s look or 5 s. | `CaptainBrainTests.AFightLastsAtLeastTwoSecondsThenPursuesToTheLastSeenSpot`, `ContactDuringThePursuitResumesTheFight`, `PursuitGivesUpAfterFiveSecondsIfItNeverGetsThere` |
| Player dies mid-fight | Leave Engage at once, despite the 2 s minimum. | `CaptainBrainTests.ADeadPlayerEndsTheFightAtOnce` |
| Goal unreachable (walled off) | Its field cost is infinite, so it is left out of the candidate set. | `GoalInferenceTests.UnreachableGoalIsLeftOut` |
| All goals unreachable, or no active goals | No prediction: stay in Observe and keep distance from the player (unless the player is busy at a goal: Converge). | `GoalInferenceTests.NoGoalsGivesNoPrediction` |
| Player's cell 5 s ago not available yet (game start, respawn) | Use the oldest recorded cell. With fewer than 2 samples, stay in Observe. | `PlayerTrackTests.ShortHistoryFallsBackToTheOldestSample`, `PlayerTrackTests.FewerThanTwoSamplesGivesNoPast` |
| Player off the grid (jumping, standing on a box) | Snap to the nearest traversable cell before looking up field costs. | `GoalInferenceTests.PlayerOnABlockedCellIsSnappedToANearbyWalkableCell` |
| Chosen ambush cell reserved by another agent | Take the next qualifying chokepoint on the route. | `InterceptPlannerTests.ReservedCellIsSkippedForTheNextChokepoint` |
| Captain stunned mid-intercept | Release the reserved cell. On recovery, go to Reassess, because the old prediction is stale. | PlayMode check in `Test_FourAgentsStress` |
| Player missing or dead | No inference; the brain stands and waits in Observe. | `CaptainBrainTests.NoPlayerMeansNoPredictionAndTheCaptainWaits` |
| Task completes and its target leaves `ObjectiveTargets` mid-intercept | Drop that goal's field, renormalise the remaining goals, and re-predict on the next tick (Reassess if it was `g*`). | `GoalInferenceTests.RemovingAGoalDropsItsFieldAndRenormalises` |
| New chapter adds new task targets | Build their fields lazily, add them to the candidate set with the task share, and renormalise. | `GoalInferenceTests.EachGoalFieldIsBuiltOnceUntilTheGridChanges` |
| Still Dormant | Ignore every stimulus and stand still until the wake signal. | `CaptainBrainTests.DormantIgnoresThePlayerUntilTheWakeSignal` |
| Chapter 3 cutscene skipped | The skip fires every Critical signal not yet reached, so the wake signal still arrives. | PlayMode check in `Test_CutsceneSkip` |
| Cutscene or pause starts mid-intercept | The brain is not ticked. Game-time timers resume from the same values afterwards. | PlayMode check in `Test_CutsceneSkip` |

## Tests

EditMode tests run without a scene, which also proves the brain is decoupled from Unity objects. Each one uses a small hand-made grid.

**DijkstraField**

| Test | What it proves |
| --- | --- |
| Costs on an open grid match the octile distance | Orthogonal steps cost 1 and diagonal steps cost √2 |
| Field cost equals A* path cost for random start/goal pairs | The field holds true shortest paths |
| Blocked cells get infinite cost; no corner cutting | Obstacles and the grid rule are respected |
| Bounded field stops at the max cost | The bound limits work as intended |
| Recomputing after a blocking change matches a fresh field | Incremental updates are correct |

**Goal inference** (implemented: `GoalInferenceTests`, `GoalPriorsTests`, `PlayerTrackTests`)

| Test | What it proves |
| --- | --- |
| `WalkingStraightAtAGoalIsConfidentWithinThreeSeconds` | The prediction becomes confident on a clear route |
| `DetourIsZeroOnAnOptimalRoute` | The bracket term is computed correctly: 0 m towards the goal, 6 m after walking 12 cells away from the other |
| `WorkedExampleFromTheDesignDocument` | The code matches the maths in this document: 0.84 / 0.11 / 0.04 |
| `PosteriorsSumToOneOnRandomGrids` | Normalisation is correct, with no NaN, on 50 random grids |
| `RemovingAGoalDropsItsFieldAndRenormalises` | Goals can come and go as tasks complete |
| `EachGoalFieldIsBuiltOnceUntilTheGridChanges` | Fields are built lazily and cached. A grid change makes them all stale; one is rebuilt per update and the rest by `RefreshOneStaleField` |
| `TheLastPredictedGoalsFieldIsRebuiltFirst` | After a grid change the field the intercept is planned on is fresh first |
| `StaleFieldsAgreeWithFreshOnesOnceRefreshed` | After the refreshes, the posteriors equal those of a brand-new inference on the changed grid |
| `DijkstraFieldRepairTests` (8 tests) | A repaired field equals a fresh one on every cell after 480 random box placements, box moves, openings and door changes; a door opening connects the room behind it; a moved box repairs far fewer cells than a full search; fallbacks and no allocation |
| `OneToOneCostTests` (8 tests) | The cost-only A* equals the field's cost on 50 random grids and under a penalty model; the bound; no allocation |
| `GridAdjacencyTests` (5 tests) | The neighbour table matches the grid's own neighbours on random grids and stays correct, refreshing only the affected cells, through 30 changes |
| `GoalPriorsTests` (9 tests) | The category priors follow the table, including the final-chapter 0.55 / 0.45 example and batteries only below 30% ammo |
| `HugeDetoursDoNotUnderflowToNaN` | The underflow guard works |
| `PlayerTrackTests` (9 tests) | The 5 s window, the short-history fallback and the ring buffer |
| `RepeatedUpdatesAllocateZeroBytes` | A 2 Hz update allocates nothing once the fields exist |

**Intercept planner**

| Test | What it proves |
| --- | --- |
| `ChosenCellSatisfiesTheArrivalTimeInequality` | `t_captain + 1 s ≤ t_player` holds, and both times match fresh, unbounded fields |
| `FirstQualifyingChokepointIsChosenOverEarlierCellsAndLaterChokepoints` | The "earliest ambush" rule: an earlier ordinary cell and a later doorway also qualify, but the first doorway wins |
| `OpenRoomWithoutChokepointsUsesTheFirstQualifyingRouteCell` | With no chokepoint on the route, the first qualifying cell is used |
| `InequalityHoldsAndNoEarlierCellWasSkippedOnRandomGrids` | On 50 random grids the inequality holds and no earlier cell that should have won was skipped |
| `PredictedRouteDescendsTheGoalFieldToTheGoal` | Each route step lowers the remaining cost by exactly its own length, so the route is a shortest one |
| `UnreachableGoalGivesNoPlan` | A walled-off goal gives no plan |
| `CaptainFieldIsBoundedByThePlayersWalk` | The Captain's field stays local instead of covering the level |
| `RepeatedPlansAllocateZeroBytes` | A 2 Hz plan allocates nothing once warm |
| `GoalInferenceTests.GoalFieldIsSharedWithTheInterceptPlannerUntilTheGoalLeaves` | The planner reuses the goal's cached field; no second search |

**Brain and states**

Implemented: `CaptainBrainTests` (16 tests), on a three-room level with two goals.

| Test | What it proves |
| --- | --- |
| `TransitionTableIsDataInPriorityOrder` | The FSM is a data table, printed by `DescribeTransitions` |
| `DormantIgnoresThePlayerUntilTheWakeSignal` | Nothing but the wake flag wakes the Captain, not even the player next to it |
| `ChapterThreeWakesTheCaptainEvenIfTheSignalWasMissed` | The chapter safety net |
| `WalkingTowardsAGoalCommitsToTheFirstChokepointItCanBeat` | Observe → Intercept at confidence ≥ 0.5; the first doorway is skipped because the player would beat it, the second qualifies, and `t_captain + 1 s ≤ t_player` holds; A* walks to it |
| `ReachingTheCellTurnsToAmbushFacingTheWayThePlayerComes` | Intercept → Ambush, facing the approach |
| `AmbushHoldsWhileTheCellIsStillAheadOfThePlayer` | No creeping towards the player while waiting |
| `TaskCompletedMidInterceptDropsThePlanAndRepredicts` | A goal leaving the objectives sends the Captain to Reassess and onto the other goal |
| `PlayerInViewWithinTenMetresIsEngagedOneShotPerInterval` | Engage asks for a shot at once (the body adds the 0.3 s telegraph), then one per 1.2 s |
| `PlayerOutOfRangeOrBehindAWallIsNotEngaged` | The 10 m range and grid line of sight |
| `LosingSightForLongerThanTheDelayEndsTheEngagement` | Engage → Reassess after 0.7 s out of sight |
| `StunDropsThePredictionAndReassessesAfterTheReboot` | Stunned and Reassess pass straight through after the reboot |
| `DormantCaptainIsNotStunnedAwake` | A stun cannot replace the wake |
| `BlockedInterceptCellMakesTheCaptainReplan` | A box in the chosen doorway invalidates the plan |
| `GoalThePlayerHasReachedIsLeftOutUntilTheyLeave` | The player at `g*` removes it from the candidates |
| `PredictionIsOfferedToTheRuntimeThroughIGoalPredictor` | The prediction the Saboteurs will read |
| `NoPlayerMeansNoPredictionAndTheCaptainWaits` | Missing or dead player |

Edge-case tests are listed in the table above.

## Implementation status

| Part | Code | Status |
| --- | --- | --- |
| Distance fields | `AI/Core/Search/DijkstraField` | Implemented, 21 tests + 8 repair tests |
| Neighbour table | `AI/Core/Search/GridAdjacency` | Implemented, 5 tests |
| Cost-only A* | `AI/Core/Search/OneToOneCost` | Implemented, 8 tests |
| Candidate goals and priors | `Captain/CandidateGoal`, `GoalCategory`, `GoalPriors` | Implemented, 9 tests |
| Player history (5 s window) | `Captain/PlayerTrack` | Implemented, 9 tests |
| Goal inference | `Captain/GoalInference` | Implemented, 16 tests |
| Intercept planner | `Captain/InterceptPlanner`, `InterceptPlan` | Implemented, 14 tests |
| `CaptainBrain` states and transitions | `Captain/CaptainBrain`, `CaptainBrain.States` | Implemented, 21 tests |
| `PredictedGoal` on the blackboard | `Core/IGoalPredictor`, `Blackboard/PredictedGoal`, copied by `AgentController` | Implemented |
| Wake flag | `Blackboard.CaptainAwake`, `Runtime/CaptainWakeWriter` | Implemented |
| Live view (F3 debug overlay) | `Runtime/Debug/CaptainOverlayLayer` | Implemented: P(g) per goal, predicted route, intercept cell and arrival times |

**How the implemented parts fit together:** each 2 Hz decision tick, the brain records the player's cell in `PlayerTrack`, takes the cell from about 5 s ago with `TryGetPast`, and calls `GoalInference.Update` with the current candidate goals. `Update` computes the category priors, looks up each goal's cached field and returns the posteriors, the most likely goal and the confidence. When the confidence reaches 0.5, the brain takes `g*`'s cached field with `TryGetGoalField` and calls `InterceptPlanner.Plan` with the player's and its own cell. When the top two goals are within 0.1 it calls `PlanShared` with both fields instead.

## Measured results
<!-- Numbers from AIPerformanceLog.md -->

**Prediction speed (EditMode scenario, not yet measured in game):** three task goals to the east, north and west of the player, on an open 40 × 40 grid. The player walks east at 3 m/s and is sampled at 2 Hz. Output of `WalkingStraightAtAGoalIsConfidentWithinThreeSeconds` (2026-10-01):

| Time | P(east) | P(north) | P(west) | Captain state |
| --- | --- | --- | --- | --- |
| 0.5 s | 0.637 | 0.221 | 0.142 | Intercept threshold (0.5) reached |
| 1.0 s | 0.855 | 0.102 | 0.043 | Above the 0.8 test target |

The prediction is confident after 1.0 s, well inside the 3 s requirement. The north goal keeps more probability than the west goal because walking east costs less detour towards north than towards west. These values match the formula worked by hand to two decimal places. In-game accuracy runs replace them once the level exists.

**Cost in the full game (2026-10-09, `Test_FourAgentsStress`, editor):** 0.095 ms a frame on average, but 4.97 ms at p99 and 14.0 ms at worst, with 67 frames over 1 ms in 30 s. Each 2 Hz decision rebuilds two distance fields while the player moves (the player's, for goal inference, and the Captain's own, for the intercept), and one full-level field costs 4.3 ms in the editor. The cached goal fields are not the cause: they are not rebuilt while the goals stay put.

**After the fix (same day, same tests):** p99 0.14 ms and worst 1.1 ms in normal play, against 7.2 ms and 15.1 ms for `develop` in the same session. With a box pushed every 0.5 s (`Test_PushedBoxStress`), p99 is 0.20 ms against 32 ms, and no frame is over 1 ms. The three changes are in `OptimisationLog.md`:
1. Fields read neighbours from a precomputed table, making a full field 7× faster.
2. `C(s → x)` and the defend-`g*` time come from a cost-only A*, which searches 21–166 cells instead of the whole level.
3. A grid change repairs the stale goal fields in place, one per frame.

**Intercept choice (EditMode scenario, 2026-10-04):** a 45 × 10 m level (90 × 20 cells) with walls at x = 15 m and x = 30 m, each with a one-cell doorway, and `g*` at the far east end. Captain speed 4.6 m/s (the prototype's value). Printed by `InterceptPlanner.Plan` through the Unity editor:

| Player | Captain | Player speed | Chosen | Player arrives | Captain arrives | Lead | Captain field cells (bounded / full) |
| --- | --- | --- | --- | --- | --- | --- | --- |
| west end | just past the first doorway | 7 m/s (sprint) | Second doorway | 3.93 s | 2.88 s | 1.05 s | 1468 / 1762 |
| west end | just past the first doorway | 3 m/s (walk) | First doorway | 4.17 s | 0.99 s | 3.18 s | 1762 / 1762 |
| west end | past the second doorway | 7 m/s | Second doorway | 3.93 s | 1.36 s | 2.57 s | 1143 / 1762 |
| middle room | past the second doorway | 7 m/s | Route cell 1.5 m past the doorway | 1.64 s | 0.57 s | 1.07 s | 665 / 1762 |
| 2.5 m from `g*` | west end | 7 m/s | Defend `g*` | 0.36 s | 8.15 s | −7.80 s | full (unbounded fallback; since 2026-10-09 one A* query instead) |

What this shows:
- **Planning against the sprint speed changes the choice.** Against a walking player the Captain takes the first doorway with 3 s to spare. Against a sprinting one it would only arrive 0.80 s early there (1.79 s against 0.99 s), inside the 1 s margin, so it waits at the second doorway instead. It never over-promises.
- **The bound saves work in proportion to how close the player is to `g*`:** 665 cells instead of 1762 when the player has 1.6 s left to walk. When the player is far away the bound covers most of the level, as it should.
- **At sprint speed the Captain has to be ahead of the player already.** With 7 m/s against 4.6 m/s it cannot overtake the player along the same corridor, which is exactly why it predicts and waits instead of chasing.
