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

**Why it is surprising to play against:** the player expects the enemy behind them and instead finds it waiting in front of them. Its tactics change with what it believes and with the player's state:
- **With its confidence:** unsure, it watches and keeps its distance (Observe); sure, it cuts the player off at a chokepoint (Intercept, Ambush); split between two goals, it holds a chokepoint both routes share; with nothing to watch, it guards the likeliest goal; and with the player busy at a goal, it closes in (Converge).
- **With the player's weapon:** battery pickups become goals only while the player's blaster is below 30% charge, so a player running low is expected to detour for a battery, and the Captain waits there.

**Counter-play:** the prediction is based on shortest routes, so a player who takes an unexpected route lowers the Captain's confidence and can slip past it. The Captain is beatable by a player who realises it is reading their movement.

**Team play:** the Captain publishes `PredictedGoal` (goal and confidence) to the shared blackboard at 2 Hz. The Saboteur squad uses it to measure which door closure forces the longest detour. When the prediction is missing or not confident, the squad falls back to the nearest `ObjectiveTargets` entry, then to the nearest active switch. (A further idea, the Tracker investigating the predicted goal when it has nothing to hear, was not built.)

**Decoupled from the story:** the Captain learns about the chapters only through the `ObjectiveTargets` blackboard entry written by S1's `ChapterManager`. The brain never references any Journey code, and the asmdefs make that a compile error.

## Classification (in the course's terms)

**PEAS**

| | The Captain |
| --- | --- |
| **Performance measure** | Reach the player before they reach their goal (88% of trips against 75% for a plain chaser); predict the right goal (71% of samples on straight walks in Chapter 4, against 22% for "nearest goal"); never stuck, never firing blind, never ignoring a player in view (370 scripted runs, 0 violations); stay beatable (a feint drops the accuracy to 48%); cheap (p99 0.14 ms a frame) |
| **Environment** | The factory as a grid of 0.5 m cells (83 x 83): four rooms, doors that open and shut, props and pushable boxes that change the grid, six other agents and the player |
| **Actuators** | Its intent each tick: a route and speed, a look target, `Shoot`, `OpenDoor` and `CloseDoor`; the body carries them out |
| **Sensors** | The player's cell, position and velocity from the blackboard (Factory OS's cameras in the story); its own 70 degree, 18 m view cone with grid line of sight; the grid (walls, doors, blockers); the chapter's `ObjectiveTargets`; where the other agents stand; game time |

**Task environment**
- **Partially observable:** where the player stands is known, but where they are going is hidden, and that hidden goal is exactly what it reasons about.
- **Multi-agent:** competitive with the player, cooperative with the Saboteurs, which read its prediction.
- **Stochastic (from its point of view):** the player's choices cannot be known in advance, so the goal is a probability, not a fact.
- **Sequential:** where it waits now decides whether it can meet the player later.
- **Dynamic:** the player, the boxes and the doors change while it decides, so it decides again at 2 Hz and repairs its distance fields when the grid changes.
- **Continuous, discretised:** the world is continuous; searching is done on the 0.5 m grid.
- **Known:** the map and the rules are known; only the player's intention is not.

**Agent type: a hybrid.**
- **Model-based:** it keeps internal state the percept alone does not give: the player's last 5 s of movement (`PlayerTrack`), where it last saw them, doors it gave up on, cells it avoids, and its FSM state.
- **Goal-based, by search:** uniform-cost search (Dijkstra) builds a distance field per goal, like a pattern database of precomputed distances, and A* with the admissible and consistent octile heuristic plans its own routes; its cost model only adds to the octile cost, so the heuristic stays admissible.
- **Probabilistic and utility-based in its choices:** P(goal | movement) comes from Bayes' rule (prior times the likelihood of the observed route), and it commits only above a confidence threshold, with hysteresis.
- **Run by a data-driven FSM** with prioritised transitions: the fight and the stun react at once (a reactive layer) over the deliberate prediction and intercept.
- **Not a learning agent, by choice:** every number has a stated reason and can be defended; a learned model would need training data the project does not have.

**Where the prediction comes from.** It is a simplified form of cost-based goal recognition (plan recognition as planning): a goal is likely when the route the player has taken costs little more than the shortest route to it (Ramírez and Geffner, 2010; Masters and Sardina, 2017). It differs in three ways: it looks at the last 5 s, not the whole route, so it notices a change of mind; the priors come from the game's state (sealed switches, the console, batteries when the blaster is low); and each goal's distances are precomputed once as a field, so every prediction is a few lookups.

## Architecture

The Captain is a finite-state machine built on the shared FSM framework: each state is an object with `Enter`, `Tick` and `Exit`, and transitions are data (condition + priority + target), not a `switch` statement. Every tick the machine takes the highest-priority transition whose condition is true.

**Confidence** below means the probability of the most likely goal, `P(g*)`, from goal inference (see Maths to defend). It is recomputed at 2 Hz.

### States

| State | What the Captain does |
| --- | --- |
| **Dormant** | Start state. Stands powered down in the Control Room and ignores the player. The brain skips goal inference and returns an empty intent. Leaves only when the Chapter 3 wake signal arrives. |
| **Observe** | Prediction is too uncertain to commit. Faces and watches the player and backs off 4 m whenever they come within 8 m, so it never simply chases while the prediction settles. A back-off step that makes no progress for 2 s is dropped and not tried again for 10 s. With nothing to watch (no contact and no confident prediction for 3 s) it guards `g*` (see Guarding when there is nothing to watch). |
| **Intercept** | Confident about `g*`, or guarding it. Picks the first chokepoint on the player's predicted route it can reach at least 1 s before them, and walks there with A*. If no cell qualifies, it heads to `g*` itself to defend it, but only if it can reach `g*`. If it has no route yet (a door was still shut), it asks again at every decision. If it does not move for 2 s, it gives the cell up (see Getting unstuck). |
| **Ambush** | At the intercept cell. Stands still, facing the player's predicted route 3 m before the cell (the body turns to the brain's look target while standing, so it can see them coming). Not the next route cell: the body stops up to 0.6 m off its cell, and coming from the player's side it can stand past that cell and would face the wrong way. Holds its ground while the cell is still ahead of the player on their predicted route. |
| **Converge** | The player is busy at a goal they reached (a task, the console hold) and nothing else is predicted. Closes in on them at intercept speed until they are in view, then fights. Gives up for 10 s if it gets stuck on the way. |
| **Engage** | Starts when the player is in view within 18 m (a 70° half-angle cone, or anywhere within 2.5 m, with line of sight traced on the grid). Faces the player and asks for a shot every 1.2 s while in contact; the body's weapon aims for 0.3 s (the telegraph, the same for every agent) before each hitscan shot. Once started, contact (line of sight within 20 m, its cannon's reach, in any direction) keeps it going, and it lasts at least 2 s. Out of contact it faces where it last saw the player and holds fire. |
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
| Observe, Intercept, Ambush, Converge | Engage | Player visible within 18 m | 80 |
| Pursue | Engage | Contact again (line of sight within 20 m) | 80 |
| Engage | Reassess | Player dead or missing | 75 |
| Engage | Pursue | No contact for 0.7 s, after at least 2 s engaged | 70 |
| Pursue | Reassess | Looked round the last-seen spot for 1 s, 5 s passed, no route there, stuck for 2 s, or the player is gone | 65 |
| Intercept, Ambush | Reassess | `g*` changes by a clear margin, confidence drops below 0.4 (so there is no plan), the player reaches `g*`, the intercept cell becomes blocked, (Intercept) stuck for 2 s, or (Ambush) the player has passed the cell | 60 |
| Converge | Reassess | The player left the goal, or the way to them is blocked | 60 |
| Intercept | Ambush | Captain reaches the intercept cell | 50 |
| Reassess | Converge | The player is busy at a goal | 45 |
| Reassess | Intercept | A plan exists: confidence ≥ 0.5, or guarding `g*` after 3 s unsure and out of contact | 40 |
| Reassess | Observe | No plan: confidence < 0.5 with the player in contact (or within the 3 s) | 30 |
| Observe | Converge | The player is busy at a goal | 25 |
| Observe | Intercept | A plan exists: confidence ≥ 0.5, or guarding `g*` after 3 s unsure and out of contact | 20 |

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
     └── any of Observe / Intercept / Ambush: player visible within 18 m
```

**Why the fight has hysteresis.** At first Engage started at 10 m in view and ended after 0.7 s out of view at the same 10 m. A player walking in and out of 10 m switched it on and off: Engage, then Observe backing off to 8 m, then Engage again, which looked irrational. Now starting and keeping a fight use different thresholds, as a thermostat does:
- It starts at 18 m in view and keeps going out to 20 m with line of sight, so a 2 m band separates the two. (The first version used 10 m and 14 m; see the next paragraph for why the range grew.)
- It lasts at least 2 s once started.
- When contact is lost, it goes where it last saw the player instead of backing off.

**Why the fight range is 18 m.** With a 10 m range, in a long room the Captain looked straight at a player 12–17 m away, well inside its cannon's 20 m reach, and only watched. In play that read as a bug: it can see you and could hit you, so it should fight. The start range is now 18 m and the keep range is the cannon's 20 m, so the Captain never stays in a fight it cannot shoot in. Starting still needs the player inside the 70° view cone with grid line of sight, so a player behind it or behind a wall is not seen. The 2 m band is narrower than the old 4 m one, but together with the 2 s minimum it still stops the flipping (`SteppingInAndOutOfEighteenMetresDoesNotFlipTheFight`).

**Why committing has hysteresis too.** The same edge problem existed for the plan: it was made at confidence ≥ 0.5 and dropped below 0.5. A player weaving between two goals kept the confidence near 0.5. In a scripted run that caused three switches between Intercept and Observe in 13 s, each one a dead stop mid-route, including a turn back towards the other goal. Now a plan is made at 0.5 but kept down to 0.4. While committed, the Captain keeps planning for its goal unless another goal leads it by 0.15. The prediction it publishes for the Saboteurs is still the true most likely goal. The same run now commits once.

**Getting unstuck.** Heading for a target without moving 0.25 m in 2 s (pinned on a prop the grid does not know about, a jam of bodies, no route) gives the target up. Its cell and the eight cells round it are not chosen again for 10 s, through the planner's existing "reserved cell" check. The measure is movement, not distance to the target, because a route round a shelf row can lead away from the target for a while. Pursue, Converge and Observe's back-off step use the same check. This is the safety net, not the fix: solid props must also block the grid. Agents are not in the grid, so a body lying in an aisle used to be exactly the case this net caught; since 15 October its own routes go round other agents (below), and the net is left for what is still unknown. The body's animation speed is the speed it really moved at, so a pinned agent stands instead of running on the spot.

**Guarding when there is nothing to watch.** Observe watches the player while the prediction settles. A player who fled out of sight and stands still, away from every goal, gives it nothing to watch and no movement to read. Seen in Chapter 4: the player ran into Storage, the Captain followed to where it lost them, the console sat at 0.45 against three tasks at 0.18, and it stood there showing "?" indefinitely. Now, unsure and out of contact for 3 s (`GuardDelay`), it plans a `Guard` at the most likely goal itself: walk there with A*, then ambush facing the player's predicted route: a place the player must come to. In Chapter 4 that is the core it rates highest while the cores stand (the console is sealed until they are down), then the console. When goals are tied (the three cores while the player hides, 0.31 each), it guards the one nearest the player by walking distance, the one they are likeliest to reach first; before, the first goal in the list won every tie. A guard already held moves only when another tied goal is nearer the player by 8 m or more (they turned up elsewhere out of sight), so a few steps do not send it back and forth. A knock-down drops the commitment, so the Captain chooses again after the reboot. The 3 s count from the last contact, the last confident prediction, or waking, so a freshly woken Captain still watches first. A guard is not a confident commitment, so it does not lower the keep-threshold to 0.4. Contact or a confident prediction ends it. Tests: `CaptainRobustnessTests.APlayerHidingAwayFromEveryGoalIsWaitedForAtTheLikeliestGoal`, `TiedGoalsAreGuardedAtTheOneNearestThePlayer`, `AGuardFollowsAPlayerWhoTurnsUpElsewhereOutOfSight`, `AKnockDownDropsTheGuardSoItIsChosenAgain`, `WhileThePlayerIsInContactAnUnsureCaptainOnlyWatches`.

**Why a goal the Captain cannot reach is no plan.** "Defend `g*`" used to be returned even when the Captain had no path to `g*`, with an arrival time of infinity. It then sat in Intercept with no route, showing "!". This happened when the Chapter 3 cutscene was skipped: the Captain decided before the Control Room door had finished opening. A goal it cannot reach now gives no plan, so it watches. When the door opens, the grid change brings the next decision forward, and Intercept also re-asks for a route at every decision while it has none.

**Doors.** The Captain can open doors; the Tracker cannot, so doors stay the player's tool against the toy.
- **Routing:** for each new route it runs a door-aware A* (`DoorRouter`) beside the shared one. A closed door is passable at a cost of 11 cells, which is about 1.2 s to stop, open it and step through at 4.6 m/s. It takes the door route only when that crosses a closed door and is cheaper, or is the only way. The penalty only adds to the octile step cost, so the heuristic stays admissible.
- **Shut in:** when the planner (which only walks open cells) has no plan because a door shuts the Captain out, it defends `g*` by way of the door.
- **Opening:** within 1.5 m of a closed door on its route it stops, faces the door and emits `AgentAction.OpenDoor`. The body opens it through the door registry, and the route carries on when the door's cells open in the grid. A door that does not open within 3 s (blocked, out of reach) is treated as a wall for 10 s.
- **Shutting it behind:** after passing a door it opened, at 1-2.3 m beyond it, the Captain shuts it if the door is on the player's predicted route and the player is at least 3 m away. It only ever shuts a door that was closed before it came through: a door that was already open is left as it was. The squad's Saboteurs close doors to slow the player; a Captain that left them open would undo their work. Shutting the door behind it makes boss and squad work together, and it uses the prediction: it only shuts doors the player is predicted to need.
- **The doorway as an ambush:** if the player is far enough away, the open doorway (a chokepoint) is often the first cell the Captain can hold in time, so it ambushes there instead of shutting it.

**Seeing over props.** Since solid props (the Console, the switch cages, the pressure-plate stop) block their footprint in the grid, the Captain walks round them. Its line of sight uses `GridLineCheck.IsSightClear`, the same cell-by-cell trace as walking, but only walls and closed doors block it, not props and boxes. At 3.25 m it sees over a 1 m console, and its shots pass over it. With the walking check instead, a player holding the Console on the far side was "hidden" from 2 m away. The Captain closed in, the stand-off held it there, it never saw the player and gave up. Closing in also no longer counts being held by the stand-off, within 3 m of the player, as stuck. Test: `CaptainRobustnessTests.APropBetweenItAndThePlayerAtTheConsoleDoesNotStopTheFight` (it fails with the walking check).

**Seeing along a shelf (15 October).** In the Storage Area the Captain stood 3 m in front of a player and did not fight. Every shelf and wall has a one-cell band round it that is not walkable, only because a 0.55 m agent cannot stand there; nothing stands in it. The walking-based sight check treats that band as solid, so a line to a player standing by a shelf counted as blocked. Now either check may clear the line: the walking-based one (which sees over props) or the grid's sight layer (`GridLineCheck.HasLineOfSight`: only what is at least 1.3 m tall blocks, with no clearance band). The sight-layer check gets the whole line to the player: it already leaves out the target's own cell, and stopped short its last cell could be a wall it then skipped. Tests: `CaptainRoutingTests.ItSeesAPlayerStandingByAShelfThroughTheShelfsWalkingClearance` (it fails without the change), `TheShelfItselfStillHidesThePlayer`, and the existing `PlayerOutOfRangeOrBehindAWallIsNotEngaged`.

**Its size and the other agents (15 October).** The grid keeps a 0.55 m capsule clear, but the Captain's shoulders, arms and cannon are 1.9 m across. Measured on the level, on about 700 of 4,294 walkable cells its shoulders scrape a shelf or a wall facing one way or both, and on 39 they scrape both ways. Its shortest routes hugged the shelf edges, so the model slid through shelves. The other agents were not in the grid at all, so it walked straight into a Saboteur standing in an aisle and waited 2 s for the stuck check. Now its own routes use `CaptainRouteCost`, which only adds to the octile cost (the heuristic stays admissible):
- **Where it fits.** The body measures, once, at every walkable cell, whether its shoulders fit walking along X and along Z (`ShoulderFit.MeasureFit`, a box the size of its shoulders at shoulder height), and hands the map to the brain (`IBodyFitAware`). A step where they would scrape costs 3 base steps more (4 in all), so a route down the middle of an aisle beats one along a shelf edge, and a scraping stretch is taken only when going round is more than four times as long. It is a cost, not a wall: where nothing keeps them clear (a narrow doorway) it still goes through, as its capsule fits.
- **Other agents.** Before every tick the body passes the brain where the others stand and how fast they move (`ICrowdAware`, from the spawner's list: no scene search). A step within 1.2 m of one costs 12 base steps if it stands still (knocked out, holding, blocked), 2 if it moves; within 2 m, 3 and 0.5. At every decision, if a standing agent is on the next 8 cells of its route, it plans again round it, instead of walking into it and waiting for the stuck check.
- Measured on the live level, Storage Area, across one aisle row with a Saboteur knocked out in the middle: the plain shortest route goes through the Saboteur and has 21 steps where its shoulders scrape; the Captain's route is the same length (33 cells), keeps 2.0 m from the Saboteur and has none. Tests: `CaptainRoutingTests` (the scrape cost, keeping clear of where its shoulders scrape, a tight gap only when going round is much longer, walking round standing and moving agents, through doors too).
- **Turning sideways through narrow gaps was tried and removed** (15 October): it was buggy in play. Where its shoulders scrape it now simply walks through, and its routes avoid those places.

**Call-outs: making the prediction visible.** The prediction is the Captain's whole idea, but in play it was invisible: the player only saw a robot that was somehow ahead of them, which reads as luck or cheating. Now every new commitment (a goal it starts cutting off or guarding, or a switch to another) flashes **a red ring and beam on that goal** for 2 s, at most every 2 s: S1's beacon look in red. It shows wherever the Captain is, so the player always learns it has been read. Until 15 October the Captain also said a line in a comic speech bubble above its head ("THE CONSOLE. OF COURSE."), with its own lines for a feint; the lines were removed, and its words are now in the cutscenes only.

`CaptainCallouts` (pure C#, beside the brain) decides when to flash; `CaptainCallout` on the body only shows it. It reads the brain through `TryGetCommitment`, so the brain still outputs nothing visual. Tests: `CaptainCalloutsTests` (3), `CaptainCalloutTests` (2, PlayMode: the goal flashes and nothing appears above its head; it flashes behind a wall too).

**Known limit:** the player's distance fields still treat a closed door as a wall, so a goal behind a closed door counts as unreachable for the player until the door opens. In the level the task doors are open while their chapter is played, so this only matters briefly.

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
- **Fairness:** the prediction reads where the player is going from how they move, and a player who takes an unexpected route can beat it. It only fights what it can see. The one thing it is given, where the player is, is explained below.
- **Cost:** Dijkstra fields are computed once per goal and reused, so each prediction is a few O(1) lookups.

### What the Captain knows about the player

**The fact:** the Captain reads the player's cell from the blackboard at every decision (2 Hz), also when the player is out of sight. Seeing (line of sight within 18 m in its view, 20 m to keep a fight) only decides whether it fights, shoots or searches.

**In the story:** the Captain is wired into Factory OS, which watches the whole factory through its cameras (the Control Room screens), counts the switches as they fall ("Two switches lost") and wakes the Captain to deal with 047. The building knows where 047 is; the Captain is its enforcer.

**What keeps it fair:**
- **It predicts the goal, not the position.** Knowing where the player stands does not tell it where they are going. That still comes from how they have moved over the last 5 s, so detours and feints beat it (measured: Chapter 4 accuracy falls from 71% on a straight walk to 48% with a feint).
- **It never fights what it cannot see.** Shooting, chasing to the last-seen spot and searching all need line of sight. Out of sight it can only wait at a goal.
- **It shows its hand.** A red ring flashes on each goal it commits to, so the player gets the same information back and can change route.

**The honest alternative, and why it was not used:** give the Captain only what the team can see. The squad, the Guard and the Tracker would report sightings to the blackboard, and between sightings the Captain would keep a belief over where the player could be: a probability map spread out from the last sighting at walking speed and cleared wherever an agent is looking (an occupancy map, like a grid-based particle filter). That is more honest, but:
- **The prediction needs a continuous track.** The 5 s window needs the player's cell now and 5 s ago. The player is out of every agent's sight most of the time, so the inference would mostly run on a guessed position, and its accuracy would fall with it.
- **The intercept timing needs the player's position.** The intercept cell is chosen by racing the player's arrival against the Captain's. With a spread-out belief there is no single arrival time to race.
- **The cost and the time.** A belief map updated every frame is a second system about as large as the goal inference, and the measured results (intercepting beats chasing 88% to 75%) depend on the current one. With submission on 21 October it would have replaced a tested design with an untested one.

It is the natural next step if the Captain is extended: the goal inference would then run on the belief map's most likely cell, and the squad's sightings would become part of the team play.

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
| Unrestored switches | 0.25 once the chapter's tasks are done; 0.05 (sealed) before | Switches not yet restored |
| Console | 0.50 in Chapter 4 once the cores are down; 0.05 (sealed) otherwise | The Control Room console |
| Batteries | 0.20, only while ammo < 30% | Battery pickups on the level |

- **Renormalise whenever the goal set changes.** Empty categories are dropped and the remaining shares are scaled so they sum to 1. For example, in Chapter 4 there are no unrestored switches, so the three cores (0.60) and the sealed console (0.05) are rescaled to 0.31 each and 0.08.
- **Why tasks get most of the mass:** the player spends most of each chapter on its tasks.
- **Why sealed goals get only 0.05:** a switch is sealed until its chapter's tasks are done, and the console until the final chapter's cores are down, so the player cannot be heading there to use them. Not 0, because they may still walk up to one. This was measured, not assumed: with the console at 0.45 next to the cores (its old share), the Captain was sure of the console before the player had moved and was confidently wrong on 53% of straight walks to a core. With the sealed share it is never confidently wrong on them, and its accuracy rose from 30% to 71% (see Measured results).
- **Why the console rises to 0.50 once the cores are down:** holding E at the console ends the game, so it is then the player's final destination.
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
| Shut in by a closed door, or a door is a shortcut | Route through it (door-aware A*, a door costs 11 cells), stop at it, open it, carry on; defend `g*` through it when shut in. | `CaptainDoorTests.TheRouterGoesThroughTheOnlyDoorAndPaysForOpeningIt`, `TheRouterWalksRoundWhenThatIsCheaperThanOpeningTheDoor`, `ShutInTheCaptainOpensTheDoorAndGoesThrough` |
| Passed through a door on the player's predicted route | Shut it behind (1-2.3 m past it, player at least 3 m away), but only a door it had to open itself; an open door is left open. | `CaptainDoorTests.ItShutsTheDoorBehindItWhenTheDoorIsOnThePlayersRoute`, `ItNeverShutsADoorThatWasAlreadyOpen` |
| A door that will not open | Treated as a wall for 10 s after 3 s of waiting, or at once when the body reports failure. | `CaptainDoorTests.ADoorThatWillNotOpenIsGivenUp`, `ADoorTheCaptainGaveUpOnIsAWall` |
| Confidence hovering round 0.5 (player weaving between two goals) | Commit at 0.5, keep down to 0.4; switch goal only on a 0.15 lead. | `CaptainRobustnessTests.WeavingBetweenTwoGoalsDoesNotStopAndStartTheCaptain` |
| Player hiding out of sight, away from every goal (fled into Storage in Chapter 4) | After 3 s unsure and out of contact, guard the most likely goal instead of standing where it lost them: in Chapter 4 the core nearest the player (the three are tied), the console once the cores are down. | `CaptainRobustnessTests.APlayerHidingAwayFromEveryGoalIsWaitedForAtTheLikeliestGoal`, `TiedGoalsAreGuardedAtTheOneNearestThePlayer`, `AGuardFollowsAPlayerWhoTurnsUpElsewhereOutOfSight`, `AKnockDownDropsTheGuardSoItIsChosenAgain`, `WhileThePlayerIsInContactAnUnsureCaptainOnlyWatches` |
| Observe's back-off step blocked by a body in the aisle | Dropped after 2 s with no movement; the spot is avoided for 10 s; standing, it turns to the player. | `CaptainRobustnessTests.ABackOffStepItCannotFinishIsDroppedAndNotRetried` |
| Stopped short of the ambush cell on the player's side | Faces the route 3 m back, so still the way the player comes. | `CaptainBrainTests.StoppedShortOnThePlayersSideItStillFacesThePlayersWay` |
| Route blocked by a pushed box or closed door | Repair the stale goal fields in place, one per frame (predicted goal first), and decide again at once. If the intercept cell is blocked, Reassess. | `DijkstraFieldRepairTests`, `GoalInferenceTests.TheLastPredictedGoalsFieldIsRebuiltFirst` |
| Player steps in and out of the 18 m range | The fight keeps going while in line of sight within 20 m, lasts at least 2 s, and then goes to the last-seen spot instead of backing off. | `CaptainBrainTests.SteppingInAndOutOfEighteenMetresDoesNotFlipTheFight` |
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
| `GoalPriorsTests` (10 tests) | The category priors follow the table: sealed switches and console while tasks remain, the switch opening after them, the final-chapter console only once the cores are down, and batteries only below 30% ammo |
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

Implemented: `CaptainBrainTests` (24 tests), on a three-room level with two goals and a long hall.

| Test | What it proves |
| --- | --- |
| `TransitionTableIsDataInPriorityOrder` | The FSM is a data table, printed by `DescribeTransitions` |
| `DormantIgnoresThePlayerUntilTheWakeSignal` | Nothing but the wake flag wakes the Captain, not even the player next to it |
| `ChapterThreeWakesTheCaptainEvenIfTheSignalWasMissed` | The chapter safety net |
| `WalkingTowardsAGoalCommitsToTheFirstChokepointItCanBeat` | Observe → Intercept at confidence ≥ 0.5; the first doorway is skipped because the player would beat it, the second qualifies, and `t_captain + 1 s ≤ t_player` holds; A* walks to it |
| `ReachingTheCellTurnsToAmbushFacingTheWayThePlayerComes` | Intercept → Ambush, facing the approach |
| `StoppedShortOnThePlayersSideItStillFacesThePlayersWay` | Standing 0.55 m short on the player's side, it still faces the player's route |
| `TheCommitmentIsOfferedForTheCallouts` | No commitment while watching; committed, it reports the goal itself (not the intercept cell) for the commitment flash (`CaptainCallouts`) |
| `AmbushHoldsWhileTheCellIsStillAheadOfThePlayer` | No creeping towards the player while waiting |
| `TaskCompletedMidInterceptDropsThePlanAndRepredicts` | A goal leaving the objectives sends the Captain to Reassess and onto the other goal |
| `PlayerInViewWithinTenMetresIsEngagedOneShotPerInterval` | Engage asks for a shot at once (the body adds the 0.3 s telegraph), then one per 1.2 s |
| `PlayerOutOfRangeOrBehindAWallIsNotEngaged` | The 18 m range (a player 19.5 m away down a hall) and grid line of sight |
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
| Candidate goals and priors | `Captain/CandidateGoal`, `GoalCategory`, `GoalPriors` | Implemented, 10 tests |
| Player history (5 s window) | `Captain/PlayerTrack` | Implemented, 9 tests |
| Goal inference | `Captain/GoalInference` | Implemented, 16 tests |
| Intercept planner | `Captain/InterceptPlanner`, `InterceptPlan` | Implemented, 14 tests |
| `CaptainBrain` states and transitions | `Captain/CaptainBrain`, `CaptainBrain.States` | Implemented, 24 tests |
| Robustness: no route, stuck, Converge, guarding, ties | `CaptainBrain` | Implemented, 13 tests (`CaptainRobustnessTests`) |
| Doors: route through, open, shut behind, give up | `Captain/DoorRouter`, `CaptainBrain` | Implemented, 7 tests (`CaptainDoorTests`) |
| Its size and the other agents in its routes | `Captain/CaptainRouteCost`, `Core/BodyFit`, `Runtime/Movement/ShoulderFit` | Implemented, 9 tests (`CaptainRoutingTests`) |
| Commitment flash | `Captain/CaptainCallouts`, `Runtime/CaptainCallout` | Implemented, 3 + 2 tests |
| Body: gait, kneel, cannon | `CaptainGait`, `AgentKneel`, `CaptainCannon` | Implemented, 3 + 5 tests |
| Evidence in the real level | `CaptainAccuracyEvidenceTests` | 4 runs (category Evidence), tables in `AIPerformanceLog.md` |
| `PredictedGoal` on the blackboard | `Core/IGoalPredictor`, `Blackboard/PredictedGoal`, copied by `AgentController` | Implemented |
| Wake flag | `Blackboard.CaptainAwake`, `Runtime/CaptainWakeWriter` | Implemented |
| Live view (F3 debug overlay) | `Runtime/Debug/CaptainOverlayLayer` | Implemented: P(g) per goal, predicted route, intercept cell and arrival times |

**How the implemented parts fit together:** each 2 Hz decision tick, the brain records the player's cell in `PlayerTrack`, takes the cell from about 5 s ago with `TryGetPast`, and calls `GoalInference.Update` with the current candidate goals. `Update` computes the category priors, looks up each goal's cached field and returns the posteriors, the most likely goal and the confidence. When the confidence reaches 0.5, the brain takes `g*`'s cached field with `TryGetGoalField` and calls `InterceptPlanner.Plan` with the player's and its own cell. When the top two goals are within 0.1 it calls `PlanShared` with both fields instead.

## Measured results
<!-- Numbers from AIPerformanceLog.md -->

**Accuracy in the real level (2026-10-10, `CaptainAccuracyEvidenceTests`).** The level is a snapshot of the game's grid and goals taken in Play mode from Bootstrap (`Tests/EditMode/Data/CaptainLevelSnapshot.txt`), so the runs are deterministic and take about 5 s. Seeded scripted players walk at 4 m/s from random places to a goal: straight there, by a detour (35-70% longer), or with a feint (half way towards another goal, then turning). The prediction is fed as the brain feeds it, at 2 Hz with the cell 5 s ago, and compared with two simpler guesses: the goal nearest the player, and the highest prior. 60 trips per row; full tables in AIPerformanceLog.md.

In game: real priors, and the player heading where the game lets them. In Chapter 4 that is a core; in Chapter 3 the snapshot has one open task, so even the prior alone gets that right, and only Chapter 4 tells the predictors apart:

| Chapter 4, to a core | Captain | Nearest goal | Prior only | Captain, last quarter | Captain confidently right | Captain confidently wrong |
| --- | --- | --- | --- | --- | --- | --- |
| Straight | **71%** | 22% | 38% | 98% | 90% of trips | 0% |
| Detour | **54%** | 31% | 37% | 87% | 82% | 47% |
| Feint | **48%** | 25% | 28% | 98% | 83% (98% switch back, median 1.1 s after the turn) | 2% |

Movement only (every goal equally likely beforehand, any goal the true one), the inference on its own beats the nearest goal in both chapters overall (Chapter 3 straight 66% against 47%, Chapter 4 straight 62% against 34%). It ties or loses slightly on Chapter 4 detours and feints (37% against 40%, 36% against 37%): the cores and the console all lie behind the same two Control Room doorways, so until the player is inside, any route there fits every goal. That is the case `PlanShared` covers by holding a chokepoint common to the likely goals.

Intercepting against chasing (Chapter 3, the Captain waking at its spawn, the player heading for the open task from at least 20 m away and out of sight; 40 trips per row): contact (within the 18 m fight range, with line of sight) before the player reaches the task. Re-run on 17 October with the 18 m range; the 10 m results of 10 October are in brackets.

| Player | Predicts and intercepts | Chases |
| --- | --- | --- |
| Walking, straight | **98%** (85%) | 73% (50%) |
| Walking, detour | **100%** (70%) | 93% (78%) |
| Sprinting, straight | **75%** (50%) | 58% (10%) |
| Sprinting, detour | **80%** (63%) | 75% (38%) |

What this shows:
- **Predicting wins every row:** 88% contact overall against 75% for a chaser (67% against 44% at 10 m).
- **Predicting pays most against a fast player:** a sprinting player (7 m/s) outruns a 4.6 m/s chaser; on a straight route the chaser reaches them before the task in 58% of runs, the Captain, already heading for the task, in 75%.
- **To see it:** in Play mode, *Factory Reset → Demo → Intercept vs Chase* puts this chaser beside the Captain and *Top-Down View* shows both routes from above (DesignDoc, debug overlay).
- **The gap is smaller than at 10 m, honestly:** the longer fight range helps a chaser more, because a chaser behind the player is usually far away but in view down the same corridor. At 10 m the chaser even won the walking detour (78% against 70%); at 18 m the Captain wins it (100% against 93%).
- **The sealed-goal priors were a measured fix.** With the old console share (0.45 next to the cores), Chapter 4 accuracy on straight walks was 30% and the Captain was confidently wrong on 53% of them, sure of the sealed console before the player moved. The sealed share (0.05) raised it to 71% and 0%, and feints are now read back in 98% of trips instead of 77%.
- **Limits:** these are scripted players on the level's grid, not people; detours are random rather than tactical; and the Chapter 3 number is easy because only one task was open when the snapshot was taken.

**Behaviour check across every kind of player (17 October).** The real brain was run on the level snapshot against 15 scripted players in Chapters 3 and 4: walking straight, by a detour or with a feint, sprinting, hiding out of sight, busy at a goal, walking up to it, standing behind it, hit-and-run, dying or vanishing half way, knocked out half way, its goal completed half way, a box dropped on its route, and before the wake signal. 370 runs, with six rules checked every 50 ms: it never fires at a player who is dead, missing, beyond 20 m or behind a wall; it never ignores a player in view for a second; it never stands still for 3 s while it should be moving; its routes stay on walkable cells; it does not switch state more than 10 times in 10 s; and its values stay finite. **No rule was broken in any run.** It engaged every walking player before their goal (110 of 110), 24 of 30 sprinters (the counter-play), every player busy at a goal or behind it, guarded a goal in all 30 hiding runs, stopped at once for a dead or missing player, and recovered from every stun, completed goal and box.

**In the game:** a scripted player played Chapters 3 and 4 to the Results screen from Bootstrap: the Captain woke as Chapter 3 began, intercepted, fought, pursued and re-predicted, with no stuck spell, no player ignored in clear view and no console error. Knocked out mid-fight, it went down after its 6 hit points, rebooted after exactly 6.0 s and was fighting again 0.1 s later. Unsure where an unseen player was going (confidence 0.31), it guarded the likeliest goal facing the way they would come.

**Seeing over props, measured (17 October):** in the level only 44 cells are tall enough to hide behind while standing on walkable floor (switch cages, chargers, terminals, crate dispensers), all at most 1.6 m tall; the Captain's visor is at 1.91 m even kneeling, so it does see over them. A player behind one can be seen, but a shot at their chest hits the prop.

**Prediction speed (EditMode scenario, not yet measured in game):** three task goals to the east, north and west of the player, on an open 40 × 40 grid. The player walks east at 3 m/s and is sampled at 2 Hz. Output of `WalkingStraightAtAGoalIsConfidentWithinThreeSeconds` (2026-10-01):

| Time | P(east) | P(north) | P(west) | Captain state |
| --- | --- | --- | --- | --- |
| 0.5 s | 0.637 | 0.221 | 0.142 | Intercept threshold (0.5) reached |
| 1.0 s | 0.855 | 0.102 | 0.043 | Above the 0.8 test target |

The prediction is confident after 1.0 s, well inside the 3 s requirement. The north goal keeps more probability than the west goal because walking east costs less detour towards north than towards west. These values match the formula worked by hand to two decimal places. The real-level runs above measure the same prediction in the level.

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
