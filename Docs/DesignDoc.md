# Design Document

Architecture, decisions and justifications. Every decision needs a "because" you can say out loud in the viva.

## 1. Architecture overview

The AI is separated from everything Unity-specific, so it can be tested without a scene and shown to be decoupled from the visuals and the story.

```text
Journey + UI       chapters, cutscenes, HUD, scoring      events and blackboard only
Presentation       Animator, fall-apart, "!"/"?" icons    reads agent state
Execution          AgentPathFollower                      walks a path
Runtime adapters   AgentController, AgentSpawner          MonoBehaviours
Agent brains       Tracker, Guard, Saboteur x4, Captain   pure C#
AI Core            grid, heap, A*, Dijkstra, FSM, blackboard   pure C#
```

Assemblies enforce the layers (see `CONTRIBUTING.md` for the reference table). If a brain tries to use a GameObject, Runtime code, chapter code or UI code, it fails to compile.

Each of the four agent types has its own search and decision logic in its owner's folder, so four different architectures are visible: greedy best-first search with a state machine (Tracker), tactical A* with cover scoring (Guard), utility AI with response curves (Saboteur), and goal prediction with Dijkstra fields and an A* intercept (Captain). Only infrastructure is shared: the grid, the binary heap, the search contracts, the finite-state-machine framework and the blackboard.

## 2. Brain / body contract (S4)

Every agent is split in two. The **brain** is pure C# and decides. The **body** is a set of MonoBehaviours that moves the character. They only meet in `AgentController`.

```text
AgentSpawner ──creates──▶ AgentController ──Tick(context)──▶ IAgentBrain
                               │   ◀──────── AgentIntent ────────┘
                               └──SetPath / Stop──▶ AgentPathFollower ──Move──▶ CharacterController
```

### 2.1 What the brain receives: `AgentContext`

A read-only struct built by `AgentController` every frame.

| Field | Meaning | Current source |
| --- | --- | --- |
| `Cell` | Agent's grid cell, for starting searches. **Not an arrival test** (see below) | `grid.WorldToCell(position)` on the level grid; `(0, 0)` only when no grid has been built |
| `Position`, `Forward` | Agent's world position and facing | The agent's transform |
| `Time` | Game time in seconds; stops during cutscenes and the pause menu. Brains must use it, never `UnityEngine.Time` | `GameClock.Current.GameTime` (S2's `GameManager`); `Time.time` in scenes without a `GameManager` |
| `World` | Shared `WorldBlackboard` (read-only for brains): the player (`Player`), the active `ObjectiveTargets` and their `ObjectivesVersion`, the current `ChapterIndex`, the Captain's wake flag (`CaptainAwake`) and its goal prediction (`PredictedGoal`) | One instance created by `AgentSpawner`, filled by its writers: `PlayerStateWriter` (every frame), `ObjectiveTargetWriter`, `ChapterIndexWriter`, `CaptainWakeWriter`; `PredictedGoal` is copied from the Captain's brain by its `AgentController` |
| `Senses` | What this agent can see and hear. Hearing: the loudest noise that reached it since its last tick (source position, level at its cell, source id, game time); each noise is delivered once | `AgentHearing` (owned by `AgentSpawner`) fills it through `AgentController.Hear`; vision is still empty |

### 2.2 What the brain returns: `AgentIntent`

| Field | Meaning |
| --- | --- |
| `Path` | World positions to walk through. **`null` = keep following the current path, empty list = stop, non-empty = replace the route.** |
| `DesiredSpeed` | Walking speed in m/s |
| `LookTarget` | Optional point to face. The body turns to it while it stands still (no route left to walk); while walking it faces where it goes, and while the weapon aims the weapon turns it. Vision uses the body's facing, so this is what lets an agent standing in ambush see the player coming |
| `Action` + `ActionTargetId` | `None`, `Shoot`, `CloseDoor`, `ArmTrap`, `StealBattery` or `Rewind`, plus the id of the door, trap or battery |
| `DebugState` | State name for the debug overlay |
| `Alert` | Optional `AlertLevel` (None, Suspicious, Alert) for the "?"/"!" icon; left at None, the body works it out from `DebugState` |

**Why `null` and an empty list mean different things:** most ticks a brain has no new route, so returning `null` costs nothing and lets the body keep walking. Stopping on purpose is a separate, explicit answer.

**Check arrival by distance, not by cell.** The body counts a waypoint as reached within 0.3 m on the ground plane and stops after the last one. Half a cell is only 0.25 m, so after a straight final step the agent stands in the cell *before* the last one. A brain that waits for `Cell` to equal its last route cell would wait forever, with the body already stopped. Compare `Position` with the last waypoint instead, on the ground plane, with a radius of at least the body's 0.3 m (`MockPathProvider` uses exactly 0.3 m; a little more, such as 0.5 m, leaves a safety margin). The body may also smooth the route, so intermediate cells are not guaranteed either.

### 2.3 The brain interface: `IAgentBrain`

| Method | When it is called |
| --- | --- |
| `AgentIntent Tick(in AgentContext ctx)` | Every frame, by `AgentController` |
| `OnGraphChanged(IReadOnlyList<Vector2Int> changedCells)` | When a door or box changes the grid. Brains replan only if a changed cell affects them |
| `OnStunned(float duration)` | When `AgentController.Disable` knocks the agent out, with the time left until it reboots. The body has stopped and **the brain is not ticked until the reboot**, so the controller owns the timing: `duration` is for information only, and a brain must not start its own stun timer (that would stun it twice). The brain drops its plan, releases what it should not hold while down, and sends a new path on its first tick afterwards |
| `OnDestroyed()` | Once, when the agent leaves the game for good (a Saboteur is scrapped, or the scene unloads). The brain releases anything it holds on the blackboard, such as target claims or cover reservations. `Tick` is never called afterwards. Called from `AgentController.Scrap`, or from `AgentController.OnDestroy` if the agent was never scrapped |

### 2.4 Runtime wiring (implemented)

- **`SpawnPoint`** (in `Agents.unity`): marks where an agent's feet go, which way it faces, its `AgentType`, its squad slot (0-3 = A-D for the four Saboteurs, -1 otherwise) and optional patrol waypoints. Each type has its own gizmo colour: Tracker yellow, Guard blue, Saboteur green, Captain red.
- **`AgentSpawner.SpawnAll()`**:
  - Instantiates one body per child spawn point, raised by the CharacterController's feet-to-pivot height so it stands on the floor.
  - Parents each body under the spawner, so agents stay in the Agents scene when scenes load additively.
  - Gives each agent an `AgentIdentity`: its type, a unique `Id` handed out in spawn order (0, 1, 2, ...), and the spawn point's squad slot. Two spawn points of the same type with the same slot log an error.
  - Reads the level grid from S1's `GridManager.Current` and creates **one** `AStarSearch` over it, shared by every brain. Without a grid it logs one warning and agents still spawn with the mock brain.
  - Names the body after its identity (e.g. `Saboteur B (#3)`), builds the brain in `CreateBrain(point, setup)` and passes the identity, the brain, the shared blackboard and the grid to `AgentController.Initialise`.
  - Can only run once. The scene loader calls it after the grid is built; test scenes can tick "Spawn On Start", which first builds the grid itself if the scene has a `GridManager`.
- **`BrainSetup`**: everything a brain may need at spawn, in one struct: the identity, the level grid, the shared pathfinder (both null without a grid), the blackboard and the patrol route. A new dependency (for example the squad's shared target claims) becomes a field here instead of a new `CreateBrain` parameter in every owner's case.
- **`CreateBrain(point, setup)`**: every type currently gets `MockPathProvider`, a fake brain that loops a patrol route. Each owner replaces only their own case when their brain is ready, using `setup.Identity` for anything that must tell instances apart (`Id` as the target-claim owner, `SquadIndex` for the Saboteur letter) and `setup.Grid` / `setup.Pathfinder` to plan routes.

**Why one shared A\* instead of one per brain:** `AStarSearch` reuses its per-cell arrays between searches and returns a fresh path list each time, so one instance serves all seven brains safely. Seven instances would hold seven copies of those arrays for no benefit.

**Why ids are handed out but squad slots are set by hand:** an id only has to be unique, so the spawner generates it and nobody can type a duplicate. The squad slot is a design choice (which spawn room holds Saboteur A, the keycard carrier), so the level designer sets it on the spawn point, and the spawner only checks that no slot is used twice.
- **`AgentController.Update()`**: asks the frame budget (`BrainTickScheduler`) whether its brain may tick this frame, builds the context (with the real grid cell under the agent), calls `Tick` and times it, applies the path semantics above, and stores `DebugState`. With no brain it logs one warning and does nothing, instead of throwing every frame.
- **Freezing outside Playing**: the controller registers with S2's `GameClock.Current` as an `IGameStateListener`. In any state other than Playing (Title, Cutscene, Paused, Results) its brain is not ticked and the path follower component is paused. Pausing the component instead of stopping the path keeps the route, so the agent carries on along it when play resumes, without asking the brain again. Stun reboots use the same game time, so a pause cannot shorten a stun. Scenes without a `GameManager` have no clock and always play.
- **Path smoothing in the body**: when a brain sends a new route of three or more waypoints, `ApplyPath` runs `PathSmoother.StringPull` then `CatmullRom` before handing it to the follower (see Path smoothing under Search contracts). It runs once per new route, not per frame, into reused lists. Both stages keep the end points and never cross a cell the brain's route avoided, so the arrival rule still holds. The "Smooth Paths" Inspector toggle turns it off for side-by-side comparison.
- **The player reaches the blackboard**: S2's player publishes itself as `PlayerState.Current`. The spawner's `PlayerStateWriter` copies it into `WorldBlackboard.Player` every frame, converting the position to a cell on the level grid. The spawner runs before the default script order (`[DefaultExecutionOrder(-50)]`), so the snapshot is fresh when the agents' brains tick. With no player, or one that has been destroyed, `IsKnown` is false.
- **The chapter reaches the blackboard**: the spawner's `ChapterIndexWriter` listens to S1's `ChapterEvents.OnChapterStarted` and calls `WorldBlackboard.SetChapterIndex`, so brains learn the chapter without referencing journey code.
- **The Captain's prediction reaches the blackboard**: brains never write the blackboard, so a brain that predicts the player's goal implements `IGoalPredictor` (AI.Core). After each tick its `AgentController` copies `Prediction` into `WorldBlackboard.PredictedGoal`, where the Saboteurs can read it. The copy is cleared when the brain is scrapped and refreshed when it is stunned, so a stale prediction never outlives its brain.
- **The Captain wakes**: the spawner's `CaptainWakeWriter` listens to `CutsceneEvents.OnCriticalSignal` and sets `WorldBlackboard.CaptainAwake` on `CaptainWake`. The director fires that signal on skip too.
- **Agents hear**: the spawner's `AgentHearing` listens to `NoiseEvents.OnNoise`. For each noise it runs S1's `NoisePropagation` **once** and reads the level at every agent's cell, so seven agents cost the same as one. An agent that is scrapped or knocked out does not hear. Each agent keeps the loudest noise until its brain's next tick (`SensorSnapshot.Loudest`), passes it in `ctx.Senses` and then clears it, so a noise reaches a brain once. A knock-out clears it too, since a noise from before the stun is stale by the reboot. The agents are reached through a small `INoiseListener` interface, so hearing is tested without spawning agents.
- **Objectives reach the blackboard**: the spawner creates an `ObjectiveTargetWriter` together with the blackboard. It listens to S1's `ObjectiveEvents.OnTargetsChanged` (world positions), turns each position into a grid cell with `GridManager.Current`, maps the kind explicitly, and calls `WorldBlackboard.SetObjectiveTargets`. The chapter manager never sees the blackboard, and only Runtime code writes it. If targets arrive before the grid exists, the latest list is written as soon as `GridManager.Ready` fires. The event's list is copied straight away, because the sender may reuse it.
- **Grid changes reach the brain**: the controller subscribes to `GridGraph.Changed` and passes the changed cells to `IAgentBrain.OnGraphChanged`, also while the agent is knocked out, but not after it is scrapped. It unsubscribes on destroy, because the grid outlives the agents.

**Why dependencies are injected at spawn:** no `FindObjectOfType` or `GetComponent` calls in `Update`, so there is no per-frame search cost and no hidden null references. EditMode tests can also create a brain without any scene.

### 2.5 The body: `AgentPathFollower` (implemented)

- Walks a `CharacterController` through the waypoints in order.
- A waypoint counts as reached within 0.3 m on the ground plane, so pivot height does not matter.
- Speed is capped at the remaining distance each frame, so the agent never overshoots on a long frame.
- Turns towards the direction of travel with `Quaternion.RotateTowards`, at up to 360°/s, around the vertical axis only. It never snaps round, and it never tilts on slopes.
- Applies gravity by hand, because `CharacterController` has none. While grounded, vertical speed is held at −2 m/s so the agent stays pressed onto slopes and steps.
- Exposes `GroundSpeed` (m/s) and `TurnRate` (°/s, positive = turning right) for the animation Blend Tree. `GroundSpeed` is how far the body really moved this frame, capped at `CurrentSpeed`, the speed it was told to move at. Pressed against something it cannot pass (an agent's body in an aisle), it shows standing instead of running on the spot, and a push or a teleport never shows as walking. `CurrentSpeed` still feeds the route blending.
- Reuses one waypoint list, so a new route allocates no memory.
- A new route starts at the centre of the cell the agent stands in. If the agent is already nearer the second waypoint than that centre is, the centre is skipped. Otherwise a route planned mid-walk sent the agent back to the centre first, which showed as a hitch (once in seven re-routes in a scripted run).

**Why not `NavMeshAgent`:** the brains plan their own paths on the grid (GBFS, tactical A*, intercepts). A `NavMeshAgent` would replan on its own and fight those decisions, and the Captain's timing maths needs the agent to walk exactly the route it was given.

**Easing speed:** the follower eases towards the route's speed at 12 m/s² instead of jumping to it, so it reaches 4.6 m/s in about 0.4 s. Starting off, setting off again after a hold, and a new route at another speed all ramp. A stop is still immediate, so arrival stays exact. Intercept timings keep their 1 s margin, which covers the few tenths of a second lost at the start.

**Blending into a new route** (`PathBlender`, AI.Core, beside the smoother).
- **The problem:** a brain that replans mid-walk hands over a route whose first leg can point well away from where the agent is going, and walking it as it is makes the body pivot on the spot.
- **When:** while moving (at least 0.5 m/s) and when the new route turns 25° to 120° from the current movement direction.
- **The curve:** the controller replaces the start of the route with a quadratic Bézier B(t) = (1−t)²P + 2(1−t)tC + t²E from the agent P to a point E about 0.35 s of travel along the route (0.6 to 1.6 m), with its control point C half that distance ahead along the heading. The curve leaves along the heading and stays inside the triangle P, C, E, so it bends onto the route without overshooting it. A sharper turn is a turn back and reads better as a turn on the spot.
- **Safety:** every segment passes the same grid line check as string pulling, so a blend never cuts a corner the route avoided; if one fails, the route is used as it is. "Blend Replans" on the controller turns it off for comparison.

**The path request scheduler** (`BrainTickScheduler`): a per-frame budget for brain decisions, 2 ms by default.
- **How:** each agent asks before its brain ticks. Once this frame's brains have used the budget, the rest wait for the next frame while their bodies keep walking, and an agent never waits more than 2 frames. The first brain of a frame always ticks.
- **Why decisions, not individual path requests:** brains ask for routes synchronously inside their tick and use the answer at once. A queue that hands a path back frames later would mean rewriting every brain, which belong to four owners.
- **Why waiting is safe:** brains time everything with `AgentContext.Time`, so a decision a frame or two late is the same decision, and noises heard meanwhile are kept until the brain's next tick.
- **Effect:** two agents that would each run a heavy search in the same frame now run them in consecutive frames. One decision heavier than the budget, the Guard's long route search, still takes its frame; splitting that would need its brain to search over several ticks. Measured in the level against the same run with no budget: about a third fewer AI frames over 2 ms (19 and 24 against 33 and 32 in two pairs), with under 4% of decisions waiting a frame (AIPerformanceLog.md).

**Tests:** `MockPathProviderTests` (8 EditMode tests), and two test scenes:
- `Scenes/Test/Test_PathFollower`: step, ramp and drop.
- `Scenes/Test/Test_AgentSpawner`: all four types patrolling, with the Saboteur squad A-D and a real level grid. `RuntimeNavMeshBake` bakes the floor's NavMesh in `Awake`, so no bake output is committed (bake commits are S1's), then "Spawn On Start" builds the grid. Checked in Play mode: a 60 x 60 grid with 3,364 walkable cells (the blocked 236 are the one-cell border the NavMesh leaves at the floor's edge), all seven agents receive it, and the console stays empty.

### 2.6 What other systems read: `IAgentState` (implemented)

A read-only view of an agent's body, in `Scripts/Interfaces/`. `AgentController` implements it.

| Property | Meaning | Source |
| --- | --- | --- |
| `Type` | Tracker, Guard, Saboteur or Captain (`AgentType`, also in `Interfaces`) | `Identity.Type` |
| `Identity` | `AgentIdentity`: type, unique `Id` and `SquadIndex` (`SquadLetter` gives A-D). Fixed for the agent's life | The spawner, through `Initialise` |
| `Position` | World position of the body's root, on the floor under its feet, in metres. A scrapped agent reports where it went down; after its body is destroyed, the last position it had | The root `Transform`, read live; saved in `OnDestroy` |
| `Speed` | Ground speed in m/s: how fast it really moves, 0 while pinned | `AgentPathFollower.GroundSpeed` |
| `TurnRate` | Degrees per second, positive = turning right | `AgentPathFollower.TurnRate` |
| `IsAttacking` | True while the brain's action is `Shoot` | The latest `AgentIntent` |
| `IsDead` | True once the agent is scrapped; never becomes false again | Set by `AgentController.Scrap` |

**Why it lives in `Interfaces`:** that assembly references nothing, so animation, the HUD, scoring and the journey can read an agent without being able to see its brain. Readers get it once with `GetComponent<IAgentState>()` when they set up, never in `Update`.

**Following an agent:** `Position` is what lets the journey use an agent's whereabouts without reaching for its `GameObject`. Chapter 3's beacon follows Saboteur A (the agent whose `Identity` is a Saboteur with `SquadIndex` 0) while it is alive, and the keycard drops where `AgentEvents.OnDestroyed` says A fell. Keep the reference from the event or from `GetComponent` and read `Position` each frame; the lookup is the only part that belongs in set-up.

### 2.7 Knock-outs and scrapping: `AgentEvents` (implemented)

`AgentController` has two public calls for whoever deals the damage:

- **`Disable(duration)`**: the agent stops, its brain gets `OnStunned` and is not ticked, and it reboots by itself when the time is up. A hit while already disabled can only extend the time. The reboot time is one float checked in `Update`, so there is no coroutine and no allocation.
- **`Scrap()`**: the agent stops for good and `IsDead` becomes true. Later calls do nothing, so two hits in the same frame count once. The brain gets `OnDestroyed` *before* `AgentEvents.OnDestroyed` is raised, so listeners such as the Saboteur squad already see its released claims.

Each change is announced through the static `AgentEvents` class in `Interfaces`. Every event passes the agent's `IAgentState`.

| Event | Raised when | Raised how often |
| --- | --- | --- |
| `OnDisabled` | `Disable` knocks out an active agent | Once per knock-out, not again when the time is extended |
| `OnRebooted` | The knock-out time runs out | Once per knock-out |
| `OnDestroyed` | `Scrap` is called | Once per agent |

Listeners (scoring, the HUD, the Saboteur squad) subscribe in `OnEnable` and unsubscribe in `OnDisable`.

**Why static events:** with seven agents, a listener would otherwise need a reference to every one of them. One hub means scoring subscribes once and hears about all of them.

**Why the listeners are cleared on play:** domain reload is turned off in this project (Enter Play Mode Options), so static fields keep their values between play sessions. A listener left over from the last session would be called on a destroyed object. `AgentEvents` clears every event with `[RuntimeInitializeOnLoadMethod(SubsystemRegistration)]` at the start of each session.

### 2.8 Agent animation (S4, implemented)

Each agent type has its own body prefab, `Prefabs/Agents/Agent_<Model>`. The spawner picks one per type from its `bodies` list, and the placeholder capsule is the fallback.

| Part | What it is |
| --- | --- |
| Root | `CharacterController` sized to the measured body (Tracker 0.50 / 1.40 m, Saboteur 0.45 / 1.60, Guard 0.55 / 2.45, Captain 0.55 / 3.25), with the pivot at the feet. Also `AgentPathFollower`, `AgentController` and `AgentAnimatorBridge` |
| Model | S3's prefab from `Prefabs/Characters`, nested unchanged. An `Animator` is added on the nested instance as an override, so S3's prefab is never edited |
| Spinners | `WheelSpinner` (Tracker's four wheels, the Saboteur's one) and `WindUpKeySpinner` (Tracker's key) |

**Blend tree:** each agent's controller has one state, a 2D Freeform Cartesian Blend Tree over `Speed` (m/s) and `TurnRate` (°/s, positive = right), with five looping clips:

| Clip | Position (Speed, TurnRate) |
| --- | --- |
| Idle | (0, 0) |
| Walk | (walk speed, 0) |
| Run | (run speed, 0) |
| LeanLeft | (run speed, −180) |
| LeanRight | (run speed, +180) |

The walk and run speeds are the brains' own speeds (Tracker 1.9 / 4.6, Saboteur 2.2 / 4.3, Captain 2.5 / 4.6; the Guard uses 2 / 4 until its brain exists). The lean clips are the run plus a roll into the turn. Every clip of an agent keys the same channels, so blending never pulls a pivot towards a default.

**Feeding the tree:** `AgentAnimatorBridge` copies `Speed` and `TurnRate` from `IAgentState` in `LateUpdate`, after the path follower has moved, with 0.1 s damping so speed changes do not pop. While the agent is frozen, knocked out or scrapped, both go to 0, so it settles into Idle instead of walking on the spot. The brain never sees the Animator.

**Clips as sine waves:** every clip is written as sine waves on the frozen pivots (`value = offset + amplitude · sin(2π(cycles · t/L + phase))`) by `ToyFactory.Editor.Animation.AgentAnimationBuilder` (menu Factory Reset → Animation → Build Agent Animations). Whole cycles per clip make every loop seamless. The numbers are in `AgentMotionLibrary`, so a change is one edit and a rebuild. The builder updates clips, controllers and prefabs in place, so GUIDs and scene references survive. It refuses to write a clip if a pivot path is missing from the model.

**Captain stride:** a hip swing of ±θ moves a boot 2·l·sin θ per step for a 1.08 m leg. One cycle is two steps, so the cycle that keeps the boots from sliding at speed v is `L = 2 · 2·l·sin θ / v`: 0.65 s for the walk (±22° at 2.5 m/s) and 0.54 s for the run (±35° at 4.6 m/s).

**Wheels and key in code, not clips:** a wheel of radius r rolling distance d turns `d / r` radians (rolling without slipping), so `WheelSpinner` turns each wheel by `Speed · Δt / r` every frame. A clip could only match one speed and would visibly slip at every other. `WindUpKeySpinner` turns the key at 180°/s × energy, so it slows as the Tracker runs down, and at −720°/s while it rewinds. It reads the brain's energy through `IWindUpState` (AI.Core), copied by `AgentController.WindUp`. Wheel and key pivots are never keyed in clips, so the Animator never fights the code.

**Not done yet:** the Guard's tread scroll (waiting on S3's model). Unit 047's cutscene motion is `Unit047Motion` (Cutscenes). The aim pose is in 2.9, the knock-down under "Hit effects, lights and the icons".

**Evidence:** every motion technique with its numbers, the tests that check it and screenshots is in `Docs/AnimationLog.md`; its frame cost is in `OptimisationLog.md` (the S4 rows of 2026-10-10).

### 2.9 Taking hits and shooting (S4, implemented)

**Taking hits:** `AgentController` implements S2's `IDamageable.TakeHit()`. The agent's capsule (`CharacterController`) is its hitbox, on the **Agents** layer, so S2's blaster finds the agent with `GetComponentInParent<IDamageable>()` on whatever it hits. Each hit costs one hit point; at 0 the agent goes down.

| Agent | Hit points | When it goes down |
| --- | --- | --- |
| Tracker | 3 | Knocked out for 7 s, then reassembles with full hit points |
| Guard | 4 | Knocked out for 8 s |
| Captain | 6 | Knocked out for 6 s |
| Saboteur | 2 | Scrapped for good (`AgentEvents.OnDestroyed`) |

Hits on an agent that is already down, frozen in a cutscene or scrapped are ignored. The numbers are serialized on each body prefab, so they can be tuned without code.

**Shooting:** a brain only decides *when* to shoot (`AgentAction.Shoot`); `AgentWeapon` on the Guard and Captain bodies carries the shot out the same way for every agent:

1. **Aim for 0.3 s (the telegraph):** a thin red line from the cannon to the player's chest. A request while the weapon is still busy is ignored.
2. **Hitscan:** one ray from the front of the barrel to the player's chest. If the first thing it meets is the player, `IPlayerState.TakeDamage(damage, agentId)` is called (Guard 10, Captain 15); a wall, a box or a prop in between takes the shot instead.
3. **Tracer:** a bright line for 0.08 s, on the **Tracer** layer.

The Captain alternates its two cannons. The aim pose is an **Aim** override layer in the Guard's and Captain's controllers (cannon arm lifted, shield or torso braced), faded in over 0.1 s by `AgentAnimatorBridge` while `IsAttacking`; its weight is driven from code, so it never fights the locomotion tree.

**Stand-off and the Tracker's bite.** A brain that chases plans its route onto the player's own cell, so a body that simply walked it drove into the player's capsule, was pushed back and drove in again, with the run cycle playing against the player. `PlayerStandOff` (every agent) holds the body still once it is within 1.4 m of the player (Tracker; Saboteur 1.6, Guard 1.6, Captain 2.0) and turns it to face them; it walks its route again once the player is 0.3 m further away, a gap that stops it flickering at the limit. The route is kept, not dropped (`AgentPathFollower.Hold` and `Release`), and the brain never knows. While held, speed is 0, so the locomotion tree settles to Idle. The Tracker then pounces with its whole body (`AgentBite`), every 1.3 s from start to start: it crouches and rocks back (wind-up, 0.2 s), hops 0.35 m forward nose first with the head snapping down in the bite (0.15 s), then hops back to where it stood (0.25 s); its ears flatten and its tail flicks up with it. A head-only bite was tried first and looked wrong, with the rest of the toy frozen. The pounce is keyframed as curves over normalised time, editable in the Inspector. The root carries the wheels and no clip animates it, so it is set from its rest pose each frame; the body, head, ears and tail are keyed by every Tracker clip, so the pounce is added on top of the Animator's pose in LateUpdate and nothing builds up. The Saboteur swipes (`AgentClawSwipe`), every 1.2 s, alternating arms: the arm rises with its pincer open while the body twists away (wind-up), then the body twists back and leans in, the robot rolls 0.3 m forward on its wheel and the arm slashes down with the pincer snapping shut, then it rolls back. Both moves share `AgentMeleeMove`, which also starts a move whenever the body starts an attack (`IsAttacking`), so the Saboteur swipes by itself once its AttackPlayer action (S3) requests one. A move never starts while another is playing, so the brain's attack and the stand-off cannot swipe twice at once.

**Melee damage.** Each move hurts the player once, at the moment of contact (`contactAt`, half way through the move, as the pincer snaps shut), not when it starts. The hit lands only if the player is alive, within the stand-off distance plus 0.6 m (the lunge), within 1.5 m of height and in front of the agent; otherwise the move whiffs. `IPlayerState.TakeDamage(damage, agentId)` is the same call the shots use. The Saboteur's swipe deals 10 (the plan's value). The Tracker's pounce deals 8 (S1's call, 2026-10-10; it was 0 and cosmetic until then). The value is serialized on `AgentBite`.

**Why the damage lands at contact:** a hit at the start of the move could not be avoided. Landing it after the wind-up makes the raised arm the telegraph, as the 0.3 s aim is for shots: a player who backs off in time is not hit.

**Facing the target, and the kick.** Nothing turned a body towards what it shot at: bodies faced the way they walked, so a Captain walking across the player's path fired out of the side of a cannon that pointed ahead. While the weapon aims and fires it now turns the body to the player, even on the move (`AgentPathFollower.FaceTowards`), and the shot leaves only once the body faces within 25 degrees of the target. If it cannot turn in time (0.5 s past the telegraph) the shot is dropped, never fired sideways. Each shot then kicks (`AgentShotRecoil`): the arm holding that barrel snaps 28 degrees up, the torso jolts back 6 degrees and the head 8, peaking at 15% of 0.28 s and easing back. The Captain alternates cannons, so its kick alternates arms. Like the attack moves it is added in LateUpdate on top of the aim pose, which keys the same pivots.

**Why the telegraph is in the body, not the brain:** hitscan cannot be dodged once fired, so the 0.3 s aim is the player's chance to step behind cover. Putting it in one place makes it identical for every agent; the brains keep only their own decision (the Guard's peek timing, the Captain's 1.2 s interval).

**No friendly fire:** the Agents layer is not in the shot's hit mask, so another agent in the line of fire neither blocks nor takes the shot. Because agents are hit through their capsules (not S3's per-part colliders, which stay disabled), S2's cover check, which skips capsules, is unaffected.

**Cancelled:** a knock-out or scrap during the aim cancels the shot. A cutscene or the pause menu holds it where it is (game time).

**Sabotage actions.** The Saboteur's brain names a door, trap or battery by id (`CloseDoor`, `ArmTrap`, `StealBattery` with `ActionTargetId`); the controller carries it out. The Captain's `OpenDoor` goes through the same path: the registered door is opened through S2's `IDoor.Open()` instead of closed, and a registered target that is not a door is a failure.

- **Finding the target:** `SabotageTargets` (Interfaces) maps a kind (`SabotageKind.Door`, `Trap`, `Battery`) and an id to S2's `ISabotageable` and the transform it stands at. Doors, traps and battery pickups register themselves in `OnEnable` and unregister in `OnDisable`. A door's id is its `doorId`, the same number as `DoorwayMarker.DoorId` and the grid's door id; trap and battery ids are the ones S2 publishes. The table is cleared at the start of each play session, like `AgentEvents`.
- **One request, one action:** a request starts when the brain's (action, target) pair changes, so a brain that repeats `CloseDoor` every tick while standing at the door asks once. Within 2.5 m on the ground plane of the target (above the brain's own 1.5 m to the door cell, so a door registered at its hinge is still in reach) the controller calls `Execute()` once.
- **Exactly one answer per request:** success when it is carried out; failure when the id is not registered, the agent stayed out of reach for 2 s, a newer request replaced it, or the agent was knocked out first. A scrapped agent's brain is released and gets no answer. Nothing happens while frozen or down; a request repeated after the reboot is a new one.
- **The answer reaches the brain** through `IActionFeedback.OnActionResolved(action, targetId, success)` (AI.Core), only if the brain implements it, so no other brain changes. The Saboteur uses it for its claims and cooldowns.

**The brain's own health:** a brain that implements `IHealthAware` (AI.Core) gets `OnHealthChanged(hitPointsLeft, maxHitPoints)` once when it is given to the body, after every hit that counts, and at the reboot. The Saboteur needs it to decide when to flee. `AgentContext` is unchanged, so the other three brains are untouched.

**Why optional interfaces, not new context fields:** the same pattern as `IGoalPredictor` and `IWindUpState`. Only the brain that needs the information implements it, the controller finds it once in `Initialise`, and a change in health or an action's outcome is an event, not something to re-read every tick.

**Why a registry, not a scene search:** the Saboteur's brain only knows ids, and the props live in S2's Interactables scene, which the Agents scene cannot reference. A static table keyed by id is filled by the props themselves, so the controller looks a target up in one dictionary read and needs no `FindObjectOfType`.

**Not done yet:** S2's `Door`, `Trap` and `BatteryPickup` registering themselves (until they do, every request fails at once and nothing in the world changes). Player health is S2's.

### 2.10 Hit effects, lights and the "?"/"!" icons (S4, implemented)

**Hit effects:** `AgentKnockdown` on every body. They are kept small on purpose: the prototype put a comic word on every hit, which cluttered fights, so here a word only marks going down.

| When | What happens |
| --- | --- |
| Each hit (`AgentController.Hit`) | A few sparks at the chest and a 0.15 s squash of the model; no word |
| Knocked out | A small explosion (a bright flash, hot sparks, a grey puff), one comic word ("KRZZT!") that pops, rises and fades over 1 s, and the body tips over onto its side with a small bounce (the Captain kneels instead, see below); the Animator is paused so the pose holds |
| Reboot | It gets back up over 0.5 s (the Captain steps back up over 1.2 s) and the Animator runs again |
| Scrapped (Saboteurs) | The same explosion with "SCRAPPED!", lies there for 1.2 s, then sinks into the floor and shrinks over 0.7 s with a last puff, and the agent is switched off (not destroyed, so the spawner's list stays valid) |

**The Captain kneels instead** (`AgentKneel`, used by `AgentKnockdown` in place of the tip when the body has one). A 3.25 m boss toppling like a toy looked wrong, and a legged robot can do better. It drops onto its right knee in 0.55 s, landing a little heavy, with the left foot planted forward, the head bowed and the left cannon resting on the front knee. At the reboot it steps back up over 1.2 s: it leans in and pushes, rises into a lunge, lifts the front foot and steps it back beside the other, then hands back to the Animator.

- **The numbers:** every Captain pivot is a hinge on local X (negative swings a leg forward, positive bends a knee, as in the walk clips). The front leg's shin stays vertical (hip −91°, knee +91°), so its boot stays on the floor when the model drops 0.44 m · (1 − cos 91°) ≈ 0.45 m, where 0.44 m is the thigh. The back leg's angles (hip 15°, knee 85°, ankle −95°) were found by measuring the model's mesh bounds over a grid of angles: the knee, shin and boot all rest within 3 mm of the floor. Through the stand-up both boots stay within 2.5 cm of the floor, except the front foot's 5 cm lift for the step.
- **Moving like a machine:** the stand-up keys are eased in and out (smoothstep between keys), so each phase starts and stops cleanly instead of flowing like a person.
- **With the Animator:** the pose is applied in LateUpdate over what the Animator wrote, blending from it while going down and back into it over the last 15% of the stand-up, so there is no pop at either end.

**Cutscene states** (also in `AgentKnockdown`, so one component owns every whole-body pose):

- **Dormant and the wake (Chapter 3):** the Captain kneels, switched off, from its first frame until `CaptainAwake` is set: no drop, no explosion, the Animator paused and the visor dark. The wake signal fires at the start of the shot that looks up at it through the server row. Its visor flickers on first (0.6 s), and 0.7 s after the wake it steps up with the same 1.2 s stand-up as after a knock-out. So the cutscene shows the boss powering up and rising, not just a light coming on. The pose ignores the cutscene freeze, which only stops the brain and the path follower.
- **Power-down (ending):** on `FactoryShutdown` every agent powers down for good, one after another in spawn order (0.4 s plus 0.15 s per agent id), so the factory goes quiet in a ripple rather than all at once. Its lights sputter out, its Animator winds down to a stop over 1.2 s, and the body sags 5° forward about its feet. The Captain instead sinks onto its knee at half speed, with no heavy landing. A body already down stays down, and nothing gets back up afterwards.

The tip rotates the model root about the feet and lifts it by part of the body radius so its side rests on the floor. No clip animates the model root, so nothing fights the tilt. The sparks come from one world-space particle system per agent that only emits on demand, with an additive material (`AgentSpark`). The comic words are two pre-rendered sprites (`Textures/FX`), shown as a camera-facing sprite, so no font asset or UI canvas is needed.

**Lights:** `AgentLights` on every body. The Tracker's antenna ball, the Guard's and the Captain's visors and the Saboteur's goggles glow in the prototype's colours (green, cyan, red, green). They sputter out when the agent goes down, and flicker back on at the reboot. The Captain's visor stays dark while it is dormant and flickers on when the wake signal sets `CaptainAwake`, during the Chapter 3 cutscene. Both read the agent's own blackboard (`AgentController.World`), so a test can wake the Captain without a spawner. S3's visors and goggles are frames, so the glow is a thin lens added inside each one on the body prefab (a box behind the visor frame, a disc on each goggle face); the frame itself stays unlit and S3's model prefab is unchanged. Each agent gets one copy of the emissive `AgentLight` material for all its lenses, which stays SRP Batcher compatible.

The bodies are updated by **Factory Reset/Animation/Update Agent Hit Effects**, which only touches these two components and the lenses. The full animation build adds them too.

**Alert icons:** `AlertIcon` on every body shows a yellow **"?"** (suspicious) or a red **"!"** (has the player) above the agent's head, facing the camera, with a short pop when the level rises. It hides while the agent is down, scrapped or frozen. The level comes from `AgentController.Alert`:

- **From the brain:** `AgentIntent.Alert` (`AlertLevel.None`, `Suspicious`, `Alert`). The Captain sets it: "!" in Intercept, Ambush and Engage, "?" in Observe and Reassess.
- **Fallback:** a brain that leaves it at `None` gets a level worked out from its state name by `AlertFromState` (for example Tracker Chase, Guard PeekAndShoot → "!"; Tracker Investigate and Search → "?"). So the Tracker, Guard and Saboteurs show icons before their owners add the one line.

**Why an explicit level instead of only state names:** a renamed state would silently lose its icon; a brain that sets `Alert` keeps it whatever its states are called. The fallback is there so nothing waits on that change.

**Captain call-outs:** `CaptainCallout` on the Captain's body shows its prediction to the player. On each new commitment it flashes a red ring and beam on the goal for 2 s (S1's beacon meshes and shader, `Materials/Captain/M_CaptainMark_*`). While the player can see the Captain (on screen, no wall in the line from the camera to its chest) it also says one line in a comic speech bubble above the "!", for 2.8 s, growing with distance so it stays readable. The bubbles match the cutscene comic words: pre-drawn sprites (`Textures/FX/CaptainSays/Say_*.png`, one per line, 400 px per metre, pivot at the tail tip) with a dark outline, the Captain's orange and white capitals with a dark stroke, popping in with the same overshoot. A line's sprite is found by `CaptainCallouts.BubbleName`, and an EditMode test checks every line has one. It reads the brain only through `CaptainBrain.TryGetCommitment`; when and what to say is the pure `CaptainCallouts` (see Docs/AI/CaptainBot.md, and Story.md for the lines). Nothing shows while frozen or down.

### 2.11 Debug overlay (S4, implemented)

Press **F3** in play to see what every agent is doing and thinking, drawn over the level (`AgentDebugOverlay`, in `Agents.unity`; `Test_AgentSpawner` shows it from the start). It works only in the Editor and development builds, so it can never appear in the submitted build, and it costs nothing while hidden.

**Chapter jump (F6, F7, F8).** Jumps the journey to Chapter 2, 3 or 4 (`ChapterJump`, on `Debug Tools` in `Agents.unity`), so a later room can be tested without playing the earlier ones. Every earlier chapter is finished the way the game finishes it: each task is completed through `ChapterFlow.CompleteTask`, each switch restored, and each cutscene played and skipped, so its Critical signals fire and every listener (doors, the Captain, the lighting) ends up as a player would leave it. The player is then put at the new room's entrance. Only forward jumps; Editor and development builds only, like F3. It needed no change to the chapter manager, so S1's code is untouched.

| Layer | Draws |
| --- | --- |
| **Agents** (every agent) | A label above the head: name, state, alert ("?"/"!"), hit points, or "knocked out 3.2s" / "scrapped"; coloured by alert level. The route still to walk, in cyan |
| **Captain** | Every candidate goal with its live probability P(g), the most likely in green; the player's predicted route in yellow; the cell it is heading for or holding, in magenta, with the player's and its own arrival times and the lead |

Lines are drawn through walls (no depth test), so a route behind cover stays visible. Labels further than 25 m from the camera are hidden, so agents in other rooms do not pile up on the horizon.

**Adding a layer (each owner, in their own file):** implement `IAgentOverlayLayer` (`Name`, `Handles(agent)`, `Draw(agent, canvas)`) and register it once:

```csharp
[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
static void Register() => AgentDebugOverlay.Register(new TrackerOverlayLayer());
```

`Handles` usually tests the brain type (`agent.Brain is TrackerBrain`). `Draw` writes into an `OverlayCanvas`: `DrawLine`, `DrawLabel`, `DrawCell` and `DrawCellPath` (cells need `canvas.Grid`, which is null without a level grid). Layers never draw directly, so they are plain code, easy to test. Registering the same layer type twice is ignored, and registrations are cleared at the start of each play session.

**Debug-only accessors:** `AgentController.Brain` and `AgentController.Follower` exist for the overlay. Game code must not use `Brain`: the journey and UI still talk to agents only through events and the blackboard. `AgentPathFollower.RemainingWaypointCount` / `RemainingWaypoint(i)` expose the route left to walk; the Captain exposes its goals, `GoalProbability(i)`, `PredictedRoute`, `HasTarget` and `TargetCell`.

**Demo: intercept against chase** (for recording the comparison the accuracy evidence measured). In Play mode, *Factory Reset → Demo → Intercept vs Chase* adds a second Captain body, "Captain (chaser)", two metres from the real one, driven by `ChaserBrain`: every 0.5 s the shortest route to the player's cell at the same 4.6 m/s, no prediction, no shooting. It is spawned through `AgentSpawner.Spawn`, so the overlay draws it, and the overlay is switched on. *Factory Reset → Demo → Top-Down View* adds a camera 37 m above the level and hides the ceilings while it is on (play only; nothing is saved), and the overlay places its labels for that camera (`AgentDebugOverlay.LabelCamera`). From Chapter 3, run from the Painting Room to the Storage task: the Captain's predicted route, its intercept cell with both arrival times, and the chaser's route behind the player are all on screen. Evidence: `Docs/Evidence/Agents/intercept_vs_chase_top.png`, `intercept_vs_chase_overlay.png`.

## 3. Search contracts
<!-- ICostModel, PathResult, IPathfinder -->

### 3.1 `DijkstraField` (S4, implemented)

A one-to-all search: after one `Compute(source, costModel, maxCost)`, `Cost(cell)` returns the shortest-path cost from the source to any cell in O(1).

- **Algorithm:** Dijkstra (uniform-cost search) on the shared grid with the shared `BinaryHeap`. A cell's cost is final when it is popped.
- **Units:** the same step costs as A* (`BaseCostModel`: 1 orthogonal, √2 diagonal), in grid units. Multiply by `GridGraph.CellSize` (0.5 m) for metres.
- **Symmetry:** grid movement and the base cost model are symmetric, so a field computed *from* a goal also gives the cost *to* that goal from every cell.
- **Unreachable cells:** cells that are blocked, off the grid, or beyond `maxCost` return infinity. A non-traversable source leaves the whole field unreachable, so callers snap it first with `GridGraph.TryFindNearestTraversable`.
- **Bounded search:** `maxCost` stops the flood early, so the work scales with the area that matters. Costs inside the bound stay exact.
- **Staleness:** the field stores the grid `Version` it was computed on, and `IsStale` turns true once the grid changes.
- **Neighbour table:** neighbours come from `GridAdjacency`, one table per grid that holds each cell's neighbour indices and base step costs in flat arrays. It listens to `GridGraph.Changed` and refreshes only the affected cells. It is built from `GetNeighboursNonAlloc`, so it follows the same corner and door rules. It made a full field 7× faster (4.82 to 0.67 ms).
- **Repair (`Refresh`):** a stale unbounded base-cost field is repaired in place. Cells whose cost came through a changed cell lose it, in turn, and Dijkstra runs again from the edge of that damage and across new edges, the idea behind LPA* and D* Lite. The result equals a fresh compute. Every other field, and a field whose change log (the last 64 grid changes, kept by `GridAdjacency`) no longer reaches back, is computed again.
- **No allocation:** per-cell arrays are allocated once per grid size, and each compute bumps a stamp instead of clearing them, so recomputing and repairing allocate nothing.
- **Evidence hooks:** `NodesExpanded` and `ElapsedMs` for `AIPerformanceLog.md`, and the `AI.DijkstraField.Compute` and `AI.DijkstraField.Repair` profiler markers.
- **Used by the Captain:** one field per candidate goal, plus one rooted at the Captain for its own arrival times. See `Docs/AI/CaptainBot.md`.
- **One-to-one questions:** `OneToOneCost` answers the cost between two cells with A* and keeps no route. It is exact, takes an optional bound and allocates nothing. The Captain uses it where it needs one number, not a field.

**Tests:**
- `DijkstraFieldTests` (21 EditMode tests), including:
  - field costs equal A* path costs on random grids, and under a penalty model;
  - blocked cells and corner-cutting are respected;
  - the bound stops the search;
  - a recompute allocates nothing.
- `DijkstraFieldRepairTests` (8): a repaired field equals a fresh one on every cell after 480 random changes.
- `GridAdjacencyTests` (5).
- `OneToOneCostTests` (8).

### 3.2 Path smoothing: `GridLineCheck` and `PathSmoother` (S4, implemented)

A brain's path has one waypoint per cell centre, so walking it directly gives a 45-degree staircase and a sharp turn at every corner. Smoothing runs in two stages, both pure C# in AI Core:

```text
brain path (cell centres) ──StringPull──▶ turning points only ──CatmullRom──▶ rounded route ──▶ AgentPathFollower
```

**`GridLineCheck.IsWalkable(grid, from, to)`**: can an agent walk in a straight line from A to B?

- Traces the segment through every cell it touches (a supercover line, using the Amanatides and Woo traversal) and requires each cell to be traversable. Closed doors and off-grid cells block it.
- A line through a cell corner is treated as a diagonal step, so both side cells must be traversable. That is the grid's own no-corner-cutting rule, so a line accepted here is never a move A* would forbid.
- Ignores world height; allocates nothing.

**`PathSmoother.StringPull(grid, path, result)`**: keeps only the waypoints where the route must turn.

- From each kept point it skips ahead while the straight line stays walkable, then keeps the last reachable point and carries on from there. Each waypoint is tested once.
- **Invariant:** every segment of the result is either a step of the original path or a line that passed `GridLineCheck`, so it never crosses a cell the original route avoided. The kept points are a subset of the original in order, so the route is never longer.

**`PathSmoother.CatmullRom(grid, path, result, spacing)`**: rounds the corners.

- A centripetal Catmull-Rom spline (alpha = 0.5) through every waypoint, sampled about every 0.5 m (one cell). Centripetal spacing never forms loops or cusps and overshoots less at sharp corners than the uniform version.
- The ends use mirrored phantom points, so the route leaves the start and reaches the goal in a straight line.
- **Safety fallback:** each span is sampled and every sampled piece must pass `GridLineCheck`. If the curve would enter a blocked cell (a tight turn round the end of a wall), that span stays straight. The invariant above still holds.

Both write into a caller-owned list and allocate nothing once it has capacity.

**Why the grid and not a NavMesh raycast:** the brains plan on the grid, so checking lines on the same grid keeps smoothing consistent with their plans, including closed doors, and it can be tested in EditMode without a scene or a baked NavMesh.

**Tests:** `GridLineCheckTests` (12) and `PathSmootherTests` (17), including:
- on random grids, every one-cell line agrees with the grid's neighbour rule, and lines are symmetric;
- on 100 random A* paths, string pulling keeps the same ends, walkable segments, original points in order and a length no longer than the original;
- on 100 random A* paths, string pulling then curving leaves every piece walkable;
- smoothing and curving into a reused list allocate 0 bytes.

## 4. Grid

- **Cells:** 0.5 m, eight-connected, no corner cutting. Built once by `GridManager.BuildGrid()` from the baked NavMesh (agent radius 0.55 m, height 3.25 m), so cells within the agent clearance of static walls are already unwalkable.
- **Changes:** every edit goes through a `GridGraph.Batch` and raises one `Changed` event with the changed cells and a new `Version`. Brains replan only when a changed cell is on their route.
- **Doors:** `GridManager.SetDoorClosed(doorId, closed)` updates every cell of a doorway at once. Closed doors block movement but carry sound (35 lost per door).
- **Blockers (S1):** `GridManager.SetBlocker(ownerId, worldBounds)` blocks the cells under the bounds grown by the 0.55 m clearance, replacing what that owner blocked before in one change. `GridManager.ClearBlocker(ownerId)` releases them. Blocking is a per-cell count, so overlapping boxes keep a shared cell blocked until both have gone. Requests made before the grid exists are applied by the build.
  - **Pushable boxes (S2):** call `ClearBlocker` on `OnBoxMoved` and `SetBlocker(id, collider.bounds)` on `OnBoxSettled`, plus once at start for a box that begins settled. Use positive owner ids.
  - **Static props:** add a `GridFootprint` component (terminal, relays, anything solid outside the `Level` bake). It blocks its collider's bounds while enabled and uses negative owner ids.


## 5. Scenes and loading order

| Scene | Owner | Contents | Status |
| --- | --- | --- | --- |
| `Bootstrap` | S2 | Scene loader and game manager. Build index 0 | Empty scene (light and camera); scene loader and game manager not written yet |
| `Env` | S1 | Static geometry, lighting, NavMesh, grid, chapter manager | Empty scene (light and camera) |
| `Interactables` | S2 | Doors, boxes, belts, switches, task props, pickups. All non-static | Empty scene (light and camera) |
| `Agents` | S4 | Agent spawner and spawn points, debug overlays, cutscene director, Timelines, cutscene cameras | Spawner, seven spawn points, cutscene director and debug overlay in place; Timelines and cameras planned |
| `UI` | S3 | HUD, subtitles, chapter card, scoring, results screen, leaderboard | HUD, chapter card, subtitles and the score manager in place; results screen and leaderboard planned |
| `ModelShowcase` | S3 | Model turntable. Not in the build | In use |

- Playing starts in `Bootstrap`, which loads the other scenes additively. `Env` is the active scene, so lighting is baked with only `Env` loaded and only `Env` holds static geometry.
- Agents are parented under the spawner, so they stay in the `Agents` scene when scenes load.
- **Spawn points** (in `Agents.unity`, in spawn order, which is also the agent id order), placed in the rooms from the level layout. Every point and waypoint was checked against the built grid: all are walkable, and all except the Captain's are reachable from `player.start` (the Control Room starts locked).

  | Agent | Room | Spawn (x, z) | Patrol |
  | --- | --- | --- | --- |
  | Tracker | Assembly | 4, 14 | Loop round the presses: (4, 14) → (17, 14) → (17, 5) → (4, 5) |
  | Guard | Painting | 31, 15 | (25, 10.5) ↔ (37, 10.5), past the terminal |
  | Saboteur A | Storage | 37, 24 | (37, 24) ↔ (23, 28) |
  | Saboteur B | Assembly | 17, 17 | (17, 17) ↔ (11, 17) |
  | Saboteur C | Painting | 38, 3 | (38, 3) ↔ (24, 3) |
  | Saboteur D | Storage north | 30, 39 | (25, 39) ↔ (38, 39) |
  | Captain | Control | 10.5, 36, facing south | None: Dormant until the Chapter 3 wake |
- Timelines live in `Agents` but animate objects in other scenes (doors, lamps, cores). They find those objects at runtime by a stable `CutsceneBindingId` on the target, never by a serialised cross-scene reference (see Cutscene contracts under Event flow).
- Test scenes live under `Scenes/Test/` and are not in the build.

## 6. Event flow

Systems talk through events and the blackboard, not direct references. The journey and UI layers never reference a brain.

| Event | Raised by | Consumed by | Status |
| --- | --- | --- | --- |
| Grid changed (`GridGraph.Version`, `IAgentBrain.OnGraphChanged`) | Grid, when a door opens or closes or a box settles | Every brain replans only if its path is affected | Grid and brain hook exist |
| Noise (`NoiseEvents`, levels in `NoiseLoudness`) | Gunshots, thrown toys, doors, boxes, task props | `NoisePropagation` (S1: L0 - 4 per metre - 35 per closed door, heard above 10), then each agent's `SensorSnapshot` (S4's `AgentHearing`), then the Tracker | Implemented |
| Player state (`PlayerState.Current`) | Player (S2) | `PlayerStateWriter` → blackboard `Player` every frame, then every brain | Implemented |
| Task progress and completion | Task props (`ITask`) | Chapter manager (`ChapterEvents.OnTaskCompleted`), HUD, scoring | Chapter manager implemented; the props are planned |
| Objective changed (`ObjectiveEvents`) | Chapter manager (S1) | `ObjectiveTargetWriter` → blackboard `ObjectiveTargets`, then Captain, Saboteurs, beacon and HUD | Implemented: the chapter manager publishes after every change |
| Switch unsealed, switch restored, chapter started (`ChapterEvents`) | Chapter manager | Switch cage (opens on unseal), cutscene director (next cutscene 1.3 s after a restore), blackboard writer (`ChapterIndex`), HUD, scoring | Raised by the chapter manager; cutscene director and `ChapterIndex` writer implemented, other listeners planned |
| Cutscene started, ended (`CutsceneEvents`) | Cutscene director | Chapter manager (starts the next chapter on ended), HUD | Implemented. The director itself sets the Cutscene state through `IGameClock.RequestState` |
| Dialogue line shown, cleared (`DialogueEvents`) | Cutscene director (`DialogueRunner`) | Subtitles (S3's UI; `PlaceholderSubtitles` until then) | Implemented in the director; S3's subtitles planned |
| Critical cutscene signal (`CutsceneEvents.OnCriticalSignal`) | Cutscene Timeline (`CriticalSignalMarker`), through the director | Captain wake (`CaptainWakeWriter` sets `CaptainAwake`), Control Room door unlock, core shields drop, factory shutdown (S1's lighting goes to its shutdown state). Also fired when a cutscene is skipped | Implemented in the director; Captain wake implemented, other listeners planned |
| Agent disabled, destroyed, rebooted (`AgentEvents`) | `AgentController` | Scoring, Saboteur squad, HUD | Events and raising implemented; nothing calls `Disable` or `Scrap` yet |
| Game state changed (Title, Playing, Cutscene, Paused, Results) | Game manager (`IGameClock`, published as `GameClock.Current`) | `AgentController` (freezes brain and body outside Playing), player input, timers, HUD | Game clock and agent freezing implemented |

### 6.1 Cutscene contracts (S4, implemented)

All three live in `Scripts/Interfaces/`, so code in every scene can use them. They move to the `Journey` assembly once it exists.

**`CutsceneBindingId`**: a component with a unique string id, put on any object a cutscene animates in another scene.

- It registers itself in a static dictionary in `OnEnable` and removes itself in `OnDisable`, so only live objects are listed.
- The director finds a target with `CutsceneBindingId.TryFind(id, out target)`: an O(1) lookup, with no search through the loaded scenes.
- A duplicate id logs an error and does not replace the first object. An empty id logs a warning.

**`CutsceneEvents`**: a static hub, like `AgentEvents`.

| Event | Passes | Raised when |
| --- | --- | --- |
| `OnCutsceneStarted` | Cutscene id | A cutscene starts playing |
| `OnCutsceneEnded` | Cutscene id | A cutscene finishes or is skipped |
| `OnCriticalSignal` | A `CutsceneSignals` id | The Timeline reaches a Critical signal, or straight away on skip for every Critical signal not yet reached |

**`CutsceneSignals`**: constants for the signal ids (`CaptainWake`, `ControlRoomUnlock`, `CoreShieldsDown`, `FactoryShutdown`). Listeners compare against these, so a misspelt id fails to compile instead of silently never matching.

**Why a skip fires the missed signals:** if the player skips the Chapter 3 cutscene before the wake marker, the Captain would otherwise stay Dormant and the Control Room doors would stay locked. Firing every Critical signal not yet reached leaves the game in the same state as watching the whole cutscene.

**Why the static data is cleared on play:** domain reload is off in this project, so the binding registry and the event listeners would otherwise keep entries from the last play session. Both are cleared with `[RuntimeInitializeOnLoadMethod(SubsystemRegistration)]`, as in `AgentEvents`.

#### Cutscene director (S4, implemented)

`CutsceneDirector` sits in the `Agents` scene on the same object as a `PlayableDirector`. Its rules live in a plain C# `CutsceneRunner`, so they are tested without Unity.

| Cutscene | Plays when | Critical signals | Afterwards |
| --- | --- | --- | --- |
| `intro` | The director plays it by itself the first time the game is Playing after loading ("Play intro on start", on by default; straight away in scenes without a game clock). Once per run | none | Playing |
| `ch2` | Switch 1 is restored | none | Playing |
| `ch3` | Switch 2 is restored | `CaptainWake`, `ControlRoomUnlock` | Playing |
| `ch4` | Switch 3 is restored | `CoreShieldsDown` | Playing |
| `ending` | The console hold completes | `FactoryShutdown` | Results |

The ids `ch2` to `ch4` are the ones S1's chapter data waits for. The table is the director's default list and can be edited in the Inspector.

**One cutscene, step by step:**

1. A restored switch (or the console) queues its cutscene. The start delay (1.3 s) only counts while the game is Playing, so the pause menu cannot shorten it.
2. The director asks for the Cutscene state (`IGameClock.RequestState`), so agents and the game clock stop, then raises `OnCutsceneStarted`.
3. It plays the cutscene's Timeline. A `CriticalSignalMarker` on the Timeline raises its signal when the playhead passes it, once per cutscene.
4. The cutscene ends when the Timeline reaches its end, or when the player presses Escape. Every listed Critical signal not yet raised fires first. Then `OnCutsceneEnded` is raised, and the state returns to Playing (Results after the ending).

**Pause:** while the game is Paused, the Timeline is held and a skip is ignored. S2's `GameManager.Resume` returns to the Cutscene state, and the Timeline carries on.

**Before the real Timelines exist:** a cutscene with no Timeline holds for a placeholder time (2 s) and still fires its signals and ends. The journey can be played from chapter 1 to the ending today.

**Why ended comes before Playing:** the chapter manager starts the next chapter on `OnCutsceneEnded`. Raising it while still in the Cutscene state means the next chapter's objectives are on the blackboard before any agent unfreezes and plans.

**Why a missing cutscene still ends:** an unknown id logs a warning but still raises started and ended. A missing or misnamed cutscene then costs a cutscene, not the whole journey.

#### Cutscene dialogue (S4, implemented)

Every cutscene has a **dialogue script** (`Data/Dialogue/<cutscene id>.asset`), its lines grouped by shot, built from the Cutscenes section of `Docs/Story.md` by *Factory Reset → Cutscenes → Build Dialogue Scripts*. A line has a speaker (Factory OS, Pip, Unit 047, Captain), its text and an optional condition: the Chapter 3 keycard line has two versions, picked by whether Saboteur A has been scrapped (the director watches `AgentEvents.OnDestroyed`).

**Playing a line** (`DialogueRunner`, plain C#):

| Step | Rule |
| --- | --- |
| Typing | 38 characters per second |
| Hold | 1.3 s + 0.025 s per character after the line is typed, then the next line |
| Click, Space or E | Finishes the typing; on a typed line, goes straight to the next |
| Esc | Skips the whole cutscene; the subtitle clears |
| Pause menu | Holds the dialogue with the cutscene |

**With a Timeline:** a `DialogueMarker` on the marker track starts one shot's lines. With "Wait For Lines" on, the Timeline holds at the marker until they are said, so the camera never cuts away mid-line however fast or slow the player reads. The cutscene ends when the Timeline has ended *and* the last line is done.

**Without a Timeline (now):** every shot is said in order and the cutscene ends with its last line, so all five cutscenes are already watchable, with their real words.

**Subtitles:** the runner raises `DialogueEvents.OnLineShown(DialogueLineView)` (speaker tag, tag colour, whole line, characters typed so far) whenever more is typed, and `OnLineCleared()` at the end or on skip. S3's UI draws the subtitle bar from these; until it exists, `PlaceholderSubtitles` on the director draws a plain bar from the same events, and is switched off when the UI lands. The bar is a screen-space canvas (sorting order 50, above the letterbox's 40) that scales with the screen from a 1920 x 1080 reference, with a dynamic font, so the text is drawn at its size on screen and stays sharp at any resolution. The first version used IMGUI at a fixed 18 px, which looked pixelated once the Game view was scaled. It sits just above the letterbox's bottom bar, on a dark translucent panel with a drop shadow on the text.

**Voice blips:** built in code, no audio files. Each speaker has a short tone in their waveform and pitch from the cast table (Factory OS low square, Pip high triangle, Captain sawtooth, Unit 047 sine), played on every second typed letter and slightly detuned each time.

**Chapter card** (`ChapterCard`, on the director). When a chapter starts (`ChapterEvents.OnChapterStarted`), a card shows for 4.5 s without blocking play.
- **What it shows:** "CHAPTER n OF 4", the chapter's title and subtitle (from S1's chapter data, through `ChapterManager.Flow`), and the four-node journey route. Finished chapters are solid mint, the current one white with a mint ring, the ones ahead dark.
- **Never over a cutscene:** a chapter that starts during one gets its card 0.5 s after the cutscene ends, once the letterbox has opened. That way the cards follow the intro and each switch cutscene, as Story.md describes. A cutscene that starts while a card is up hides it, and the card comes back afterwards.
- **How it's drawn:** its own screen-space canvas, scaled from 1920 x 1080 with a dynamic font, at sorting order 45 (above the letterbox, below the subtitles). The route dots come from a circle generated at start-up, so it needs no assets. It fades and slides in over 0.35 s and fades out over the last 0.6 s.
- **Ownership:** the plan's cutscene schedule (S4) includes chapter cards. The UI scene (S3) holds the HUD and results screen; if S3's UI draws a card later, this one is switched off, like the placeholder subtitles.

**Cues: the alarm and comic words** (`CutsceneCuePlan`, `CutsceneCueMarker`, `CutsceneCuePlayer` on the director). The shot lists in Story.md call for an alarm and for comic pop-ups.

| Cutscene, shot | Cue |
| --- | --- |
| intro 1 | Alarm (the dark assembly line) |
| intro 2 | "?!" over the Tracker as it powers on |
| intro 3 | "ATTEN-HUT!" over the Guard at its post (from the prototype) |
| intro 5 | A red "DEFECTIVE!" over Unit 047, with the line "Product status: DEFECTIVE." |
| ch2 2, ch3 1, ch4 1 | Alarm |
| ending 1 | A mint "SHUTDOWN" over the console |

- **Markers:** each cue is a marker on its Timeline's marker track, placed by the Timeline builder at the start of the line it belongs to (a line's time is its typing plus its hold). The director hands it to the cue player. They are visible and movable in the Timeline window.
- **Words:** four pre-rendered sprites in the knock-out word's style (`Textures/FX/Comic_Alert`, `_AttenHut`, `_Defective`, `_Shutdown`), coloured to the moment: Factory OS's tomato for DEFECTIVE, mint for SHUTDOWN, the Guard's cyan for ATTEN-HUT. They pop to 115%, settle, face the camera, drift up and fade over 1.6 s. Words over agents use their spawn points, where the intro films them; "DEFECTIVE!" sits above the stand-in, wherever the player stands. Every word still up is cleared when the cutscene ends.
- **Alarm:** generated like the blips. Three rising whoops (520 to 920 Hz, an even rise in pitch, 0.4 s each), a square wave softened with a sine.
- **Show only:** a skipped cutscene does not play them, unlike Critical signals.

#### Cutscene camera, bindings and letterbox (S4, implemented)

**Camera hand-over** (`CutsceneCameraRig`, owned by the director). The gameplay camera sits on S2's player prefab and is driven by mouse look, so it carries no Cinemachine brain. When a cutscene with a Timeline starts, the rig:

1. records the camera's local position, rotation and lens;
2. adds a `CinemachineBrain` to it at runtime (or re-enables the one it added before), so the player prefab is never edited;
3. parks a "gameplay view" Cinemachine camera exactly at the player's eyes, at priority 100.

The Timeline's shot cameras override it while their clips play. A shot clip's ease-in can blend *from* the gameplay view and the last clip's ease-out back *to* it. The journey's five Timelines do not use this: they cut in and out, as the prototype does, because the Unit 047 stand-in stands where the player's eyes are and a blend back would pass through its body. When the cutscene ends (watched or skipped) the brain is switched off and the camera's local pose and lens are put back, so mouse look carries on exactly where it stopped. Skipping cuts straight back. Between cutscenes no brain and no Cinemachine camera run at all.

**Binding across scenes** (`CutsceneBindings`). Timelines live in the Agents scene and cannot keep a reference to an object in Env or Interactables. A track that drives such an object is left unbound and named after the object's `CutsceneBindingId` (a track named `AlarmDoor3` drives the object with that id). When the Timeline starts, every unbound track is looked up by its name once and bound to the right thing for its type: the GameObject for an activation track, the `Animator` (on the object or a child) for an animation track, the component otherwise. Cinemachine tracks are bound to the brain on the gameplay camera. Tracks already bound in the Agents scene (Unit 047, shot cameras) are left alone. A name with no matching object is logged once per cutscene and that track does nothing; the cutscene still plays and its Critical signals still fire. Only enabled objects register their id, so something a cutscene switches on must be bound through an active parent.

**Letterbox** (`Letterbox`, on the director). Two black bars, each 11% of the screen height, close in over 0.6 s (smoothstep) on `CutsceneEvents.OnCutsceneStarted` and open on `OnCutsceneEnded`, so a skip opens them too. It builds its own screen-space canvas at sorting order 40, with no assets. The subtitle view and the chapter card must sort above 40. The canvas is switched off once the bars are fully open, so gameplay pays nothing for it.

#### Cutscene Timelines and shots (S4, implemented)

**Built, not hand-edited.** `CutsceneTimelineBuilder` (menu *Factory Reset/Cutscenes/Build Timelines*, with `Agents.unity` open) writes the five Timelines to `Data/Cutscenes/` and their cameras under the director in the Agents scene, then sets each cutscene's `timeline`. Running it again rebuilds everything in place. The shots come from `CutsceneShotPlan` (Journey), the lines from the dialogue scripts, so a changed line or shot is one click away and the Timelines cannot drift from the script.

**One shot, two cameras.** Each shot is a slow camera move: a Cinemachine camera at its start pose and one at its end pose, on two clips that overlap for all but 0.1 s at each end, so the whole shot is one ease-in-out blend from the first pose to the second. A shot lasts as long as its lines take when nobody clicks (`DialogueRunner.ShotSeconds`: typing at 38 characters per second, then 1.3 s + 0.025 s per character; of the versions of the keycard line only the longest counts) plus 0.25 s. The cutscenes come to 47 s (intro), 26 s (ch2), 34 s (ch3), 15 s (ch4) and 19 s (ending), about as in the prototype.

**Shot pacing.** On the marker track each shot has a `DialogueMarker` at its start that does *not* wait, so the camera keeps moving while the lines are said, and a `ShotEndMarker` 0.05 s before the next shot. The director:

- **holds** the Timeline at a shot end while a line is still on screen (the player paused, or a line ran a few frames long: the runner loses up to a frame or two per line at low frame rates). A hold sets the graph's speed to 0 instead of calling `PlayableDirector.Pause()`, so the graph keeps being evaluated and the shot camera stays on screen; pausing stopped the Cinemachine track and showed another camera for the length of the hold. The pause menu holds the same way;
- **moves the playhead to the shot end** as soon as the shot's lines are over, if the player clicked through them, so the camera never lingers on an empty subtitle. Timeline does not notify markers it jumps over, so the director passes the skipped ones on itself, in order: a Critical signal inside the shot still fires (the runner ignores a signal it has already raised).

A shot's lines are queued once, even if a marker is passed twice. The first shot is evaluated in the frame the cutscene starts, so the gameplay view (inside the stand-in) is never drawn.

**The shots** (`CutsceneShotPlan`; subjects follow the prototype's shot list, positions are placed again for this level):

| Cutscene | Shot | Camera | Height | Signal |
| --- | --- | --- | --- | --- |
| intro | Wide high shot across the Assembly Floor | fixed | 4.6 to 4.3 m | |
| | Close-up on the Tracker at its spawn | fixed | 1.2 m | |
| | The Guard at its post, from in front | fixed | 1.7 to 1.6 m | |
| | Saboteur B by the pressure plate | fixed | 2.6 to 2.3 m | |
| | Push-in on Unit 047's face | follows 047 | 2.0 to 1.9 m | |
| | Beside 047, down the floor to the belts | follows 047 | 2.6 to 2.4 m | |
| ch2 | The restored Assembly switch | fixed | 2.2 to 2.4 m | |
| | High pan across the Painting Room to the Guard's ground | fixed | 4.6 to 4.3 m | |
| | Pan from a spinning target down to the terminal | fixed | 2.6 to 2.4 m | |
| ch3 | Looking up at the Captain through the server row | fixed | 1.2 to 1.3 m | `CaptainWake` |
| | The Storage-Control door unlocking, from Storage | fixed | 3.2 to 2.7 m | `ControlRoomUnlock` |
| | High pan over the shelves to the relay board | fixed | 4.6 to 4.8 m | |
| ch4 | Wide over the three power cores | fixed | 4.4 to 4.6 m | `CoreShieldsDown` |
| ending | Behind 047, looking at the console | follows 047 | 2.2 to 2.5 m | `FactoryShutdown` |
| | Crane back and up from 047, 6 m | follows 047 | 2.5 to 4.4 m | |

Fixed shots use world positions; agents in them are at their spawn (the intro) or frozen where they are. Follow shots ride along with the Unit 047 stand-in with no damping, so they work wherever the player stands; the console stands in the middle of the Control Room, 10 m from every wall, so the ending's crane ends 6 m behind 047 and stays inside the room wherever the player holds the console from. Follow cameras carry a Cinemachine Decollider (radius 0.3 m), so a shot that would land inside a server pillar is pushed out of it.

**Each toy is introduced on screen.** Factory OS's roll call in the intro is one shot per kind of toy (Tracker, Guard, Saboteur B), each filmed at its spawn, so every line names what is on screen. The keycard line in ch3 has three versions: Saboteur A still active, A scrapped with the card on the floor, or the card already picked up (the director hears the keycard task appear through `TaskEvents.OnTaskSpawned` and complete through its `OnCompleted`).

**Checked against the level** (`CutsceneShotPlanTests`): every camera is at least 1 m under the 6 m ceiling agreed with S1 (the highest is 4.6 m), fixed cameras are 0.5 m inside their room's walls and clear of every obstacle as built after the room rebuild (presses, paint tanks and targets, terminal, shelf runs, the 12 server pillars and cores, the console), follow cameras stay within 7 m of 047, every dialogue shot has a camera shot, and each cutscene's signals are exactly its Critical signals. `CutsceneTimelineAssetTests` checks the built Timelines the same way.

**The whole journey, end to end** (`JourneyFlowTests`). One PlayMode test loads `Bootstrap` (so Env, Interactables and Agents load as in the game) and completes every task of every chapter through `ChapterFlow.CompleteTask`, the call a prop's completion makes. It checks the five cutscenes play in order (intro, ch2, ch3, ch4, ending), that ch3 played through fires `CaptainWake` and `ControlRoomUnlock` from its Timeline (the Captain is awake on the blackboard and doors 3 and 4 become walkable on the grid), that ch4 fires `CoreShieldsDown`, that the ending fires `FactoryShutdown` and hands over to Results, and that each signal fires once. The other cutscenes are skipped, which fires their signals the way Escape does. It found one leak: `GameManager` publishes itself as `GameClock.Current` and never clears it, so after the game is unloaded a dead clock stuck in Results freezes every later agent; the test clears it in its teardown, and S2 is asked to clear it in `GameManager.OnDestroy`.

**Unit 047 stand-in** (`CutsceneActor`, "Unit 047 (cutscene)" under the director). The player is a first-person camera with no body, so while any cutscene plays the stand-in shows S3's Unit 047 prefab where the player stands, facing the player's way (`PlayerState.Current`), and hides it when the cutscene ends. It moves on `OnCutsceneStarted`, before the Timeline plays, so the follow cameras start in the right place. Its colliders are switched off: it is only seen.

**Unit 047's animation** (`Unit047Motion`, on the stand-in). Written in code on S3's frozen pivots, like the agents' wheels and kneel, because its beats follow the cutscene's shots rather than a clip's clock:

- **Always, while shown:**
  - the body sways 1.5° side to side (3.2 s) and rocks 1° (4.3 s) about the waist, with the feet planted, because the legs are not under `Body_Pivot`;
  - it breathes with a 6 mm bob (1.8 s), the arms swing loosely against the sway, and the wind-up key turns at 180°/s.
- **Head:** by default it drifts ±12° from side to side (7 s). A beat sets a new look (ahead, to its right, up), and the head settles on it with a critically damped spring (`Mathf.SmoothDampAngle`, 0.35 s), so it starts and stops softly whatever the distance.
- **Eyes:** one emissive material copied from the agents' `AgentLight`, in 047's cyan (#62D8FF). They flicker on and sputter out with the same patterns as the agents' lights.
- **Story beats** (a list on the component): the intro opens with the eyes off (the dead production line). They flicker on at the close-up, where the quality check runs, and the head turns to its right when Pip calls on the radio. In the ending it looks up as the camera cranes away. Every cutscene starts from eyes on and looking around.
- **Timing:** the director raises `ShotStarted(cutsceneId, shot)` as each shot's dialogue marker is reached, so a beat lands at the start of its shot however long the player takes to read. Cues for shot −1 fire as the cutscene starts.

### 6.2 Chapter contracts (S1, implemented)

The journey's rules live in the plain C# `ChapterFlow` (Journey assembly); `ChapterManager` in `Env.unity` only connects it to the scene. S2's scene loader calls `ChapterManager.Begin()` after `BuildGrid` and `SpawnAll`; nothing starts on its own.

- **Each chapter is a state machine** on the shared FSM framework: Locked → Active → TasksDone → SwitchRestored → Transition → Done. Tasks of the active chapter count in any order; pickups (fuses, keycard) count in any chapter; other later-chapter tasks are ignored until their chapter starts. TasksDone raises `OnSwitchUnsealed(n)`; the switch prop's completion (`ITask.Id` = `"switch.n"`) is ignored until then. The next chapter starts only when the cutscene after the switch ends (`CutsceneEvents.OnCutsceneEnded`, raised on skip too). Chapter 4 has no switch; its console task (`"console"`) is the ending.
- **Props** implement `ITask`; the manager finds them once in `Begin()`. A prop created during play (the keycard) announces itself with `TaskEvents.RaiseTaskSpawned`. Anchors are found through `ITaskAnchor` (implemented by `TaskAnchor`), because Journey cannot reference Runtime.
- **Objectives** (`ObjectiveEvents.RaiseTargetsChanged`, after every change): each incomplete task of the active chapter at 100 + objectiveId, each switch not yet restored (sealed or not) at 200 + n, and the console at 300 until it is used. Never batteries. A target on a moving prop is re-raised with the same id.
- **Relays:** the order is random per run, chosen in `Begin()` and announced with `ChapterEvents.OnSequenceChosen("ch3.relays", order)` for the relay prop and the HUD; the objective follows the next relay as the prop reports progress.
- **Data:** `Assets/_Project/Data/Chapters/` (four `ChapterDefinition`s, thirteen `TaskDefinition`s). The task ids there are the ids the props must use.
- **Story:** [Story.md](Story.md) has the chapter beats, each task's HUD text (the same as `TaskDefinition.displayName`), the chapter card text and the full cutscene script.

### 6.3 Objective beacon and lighting state (S1, implemented)

**Beacon target.** `ChapterManager.CurrentBeaconTarget` (a `BeaconTarget?`) is the one place the player should head for next:
- The first incomplete task of the active chapter, in the chapter's data order.
- Once all of the chapter's tasks are done, its unsealed switch.
- In Chapter 4, the cores and then the console.
- A task that cannot be placed yet is passed over. The keycard is the example: it is passed over until Saboteur A drops it.
- Null before `Begin()`, while a chapter cutscene plays, and after the ending.

| Field | Meaning |
| --- | --- |
| `Id` | The same id as in the objective list (100 + objectiveId, 200 + n, 300) |
| `Position` | The task's anchor, or the prop itself |
| `Kind` | Task, Switch or Console |
| `TaskId` | `"ch1.lever"`, `"switch.2"` or `"console"` |
| `Label` | The HUD line: the task's display name, or "Restore the {area} switch" |

`BeaconTargetChanged` fires when the target changes or moves; republishing the same target does not fire it. The rule is `ChapterFlow.TryGetBeaconTarget`, tested in EditMode. S3's HUD reads the same property for its arrow and distance.

**Beacon.** `ObjectiveBeacon` (`Prefabs/Environment/ObjectiveBeacon.prefab`, in `Env`) is built like the prototype:
- A 5 m beam, a ground ring that pulses, and a yellow arrow that bobs and spins at 4.2 m.
- It stands at the target's floor position. It glides after a target that moves, but jumps to a new target more than 3 m away.
- It shows only while the game clock is Playing and no cutscene runs. Because the intro is a cutscene, the beacon first appears when the intro ends, and it hides for every later cutscene, the pause menu and the results screen.
- The look is the `ToyFactory/ObjectiveBeacon` shader: URP Unlit, additive, double-sided. It has:
  - stripes that scroll up the beam
  - a soft silhouette
  - a fade towards the top of the beam
  - a depth fade where the beam meets the floor (uses the URP depth texture)
  - a near fade: the whole beacon dims to 25% within 2 m of the camera
- All material values are in `UnityPerMaterial`, so the three parts stay SRP Batcher compatible.

**Lighting state.** `LightingState` sits on `Env`'s `Lighting` root and listens only to `CutsceneEvents.OnCriticalSignal`, so a skipped cutscene leaves the lights right too. The mood only moves forward:

| Mode | When | Lights | Emissives |
| --- | --- | --- | --- |
| `Alarm` | From the start | The two alarm lights in the sealed Control Room doorways (3 and 4) blink red, about once a second | The four lintel beacons swap between `M_Env_GlowAlarm` and `M_Env_GlowAlarmOff` |
| `Unlocked` | `ControlRoomUnlock` (`ch3`) | Steady amber | `M_Env_GlowAmber` |
| `Shutdown` | `FactoryShutdown` (`ending`) | The Storage lamp and the alarms fade over 3 s to a warm glow at 35% intensity (the sun is off since the ceiling) | Screens, press strips and beacons dim to 15%; the wall gears wind down to a stop (`DressingSpinner`) and the server rack lights go dark (`DressingBlinker`) |

The beacons change by swapping shared materials, which keeps the SRP Batcher. Only the shutdown fade makes per-renderer material copies, once, at the end of the game.

**Cutscene binding ids in `Env`** (for S4's Timelines, through `CutsceneBindingId.TryFind`):

| Id | Object | Holds |
| --- | --- | --- |
| `LightingRig` | `Lighting` | `LightingState`, every light, the post-processing Volume |
| `Sun` | `Lighting/Sun` | Directional light, switched off since the ceiling (kept so the id still resolves) |
| `StorageLamp` | `Lighting/Accents_Realtime/Accent_Storage_Lamp` | Orange real-time point light |
| `AlarmDoor3` | `Lighting/ControlAlarms/AlarmDoor3` | Alarm light and two lintel beacons, Storage–Control door |
| `AlarmDoor4` | `Lighting/ControlAlarms/AlarmDoor4` | Alarm light and two lintel beacons, Control–Assembly door |
| `ControlScreens` | `Level/Dressing/ControlScreens` | The three Control Room screens |
| `ObjectiveBeacon` | `ObjectiveBeacon` | The beacon (it already hides itself during cutscenes) |

The alarm colours belong to `LightingState`. A Timeline can frame or activate these objects, but a light that both a Timeline and `LightingState` animate would fight, so leave the alarm colour to the signals.

### 6.4 HUD (S3, implemented)

`UI.unity` holds one object, `HUD`, with `HudPresenter`. The presenter builds its uGUI objects in code (like the cutscene letterbox), so the scene stays a single component and there is no YAML to merge. Text uses Unity's built-in font: TextMeshPro would need its essential resources imported into the project, and nothing here needs them.

| On screen | Reads | Notes |
| --- | --- | --- |
| Objectives panel: "CHAPTER n OF 4", title, one row per task with a tick, a "Restore switch n" row | `ChapterEvents` (`OnChapterStarted`, `OnTaskCompleted`) and the chapter manager's definitions | A HUD that wakes after the journey began catches up from the chapter flow |
| Switch lamps and "n of 3" | `ChapterEvents.OnSwitchUnsealed` / `OnSwitchRestored`, and each chapter's phase | Sealed, ready (sun), restored (mint) |
| Integrity, charge and overcharge bars | `PlayerState.Current` | Overcharge shows only while active; a missing player empties the bars |
| Damage vignette | Integrity falling | A flash for 0.5 s after a hit, and a steady glow below 30% integrity |
| Crosshair | - | Four ticks round an empty centre |
| Objective arrow with distance | The chapter manager's current beacon target and `Camera.main` | Shown only when the objective is off screen, on the screen edge, pointing at it (or the way to turn if it is behind) |
| Chapter card: tag, title, subtitle, four-stop route | `ChapterEvents.OnChapterStarted` | 4.5 s, fades, never blocks; waits for a cutscene to end; switches off the director's own `ChapterCard`, as that card's design says, so only one shows |
| Subtitle bar | `DialogueEvents` | Sorts above the letterbox; switches off the director's `PlaceholderSubtitles` the first time a line shows |

- **Visibility.** The HUD shows while the game is playing or paused and no cutscene is running (`GameClock` state, `CutsceneEvents`). It is hidden on the title screen, in cutscenes and on the results screen. The subtitle bar is not part of that: it shows in cutscenes.
- **Two canvases.** Panels and labels sit on the main canvas; the bars' fills, the arrow and the vignette sit on nested canvases, so a moving bar does not rebuild the text. The batch counts have not been measured yet.
- **Not here yet.** The title and results screens, the score panel and combo text, prompts and hold bar, hit flash, wind-up toy count and accuracy come with scoring and the Interfaces bridge from S2. The HUD reads no brain and computes no score.
- **Tests.** `HudTests` (EditMode) covers `HudMath` (bars, vignette, arrow placement), `HudModel` and the card's timing; `HudSceneTests` (PlayMode, real scenes from Bootstrap) covers the chapter text, ticking a task, the bars and vignette after a hit, hiding in a cutscene, the subtitles and a missing player. `ToyFactory.Tests.EditMode` and `ToyFactory.Tests.PlayMode` now reference `ToyFactory.UI` so they can test it.
### 6.4 The player's blaster, battery and effects (S2, implemented)

**Health** (`PlayerHealth`). 100 points. Agents reach it through `IPlayerState.TakeDamage` (the Guard hits for 10, the Captain for 15). Hits that arrive outside the Playing state are ignored. At zero the game goes to Results and `LastDamageSourceId` names the agent that did it.

**Battery** (`PlayerBattery`). 20 shots per battery. Reloading (R, or pulling the trigger on an empty battery) takes 1.5 s and uses one spare cell; the player starts with 3 and carries at most 5, so a run has 80 shots without pickups. A battery pickup adds a spare cell, the charger refills everything, and an overcharge cell makes shots free for 8 s. The agents read all of it through `IPlayerState` (`AmmoFraction`, `IsReloading`, `OverchargeTimeLeft`, `LastShotTime`).

**Firing** (`PlayerBlaster`). Holding Attack fires a hitscan ray from the camera pivot, 4 shots a second, 60 m range. The first thing it meets that implements `IDamageable` takes `TakeHit()`, and every shot is a `NoiseLoudness.BlasterShot` noise with source id -1. Overcharged, it fires 13.3 shots a second inside a 1 degree cone and costs nothing. `TryShoot()` is public so tests can fire without a mouse.

**What the player sees.** The hit is decided and applied when the shot is fired; everything below is the picture of it.

| Part | What it is |
| --- | --- |
| `ShotBolt` | A bright, nearly white core (about 0.16 m wide, 0.8 m long) with a soft glow about 0.9 m wide on its head, in the shot's colour (cyan, gold when overcharged). The glow is a quad turned to face the camera, so it still shows as a round blob when the bolt flies straight away; a line alone would shrink to nothing. It flies at 90 m/s from the gun's muzzle to the hit point and the glow goes out on arrival. The sizes follow the HTML prototype. |
| `ImpactBurst` | Six glowing sparks (0.18 to 0.34 m) thrown out from the hit point, with gravity and a linear fade, tinted by the shot. |
| `BlasterViewModel` | The gun in view: a greybox model from primitives (orange body, steel barrel, teal rings, a glowing cell that shrinks with the charge and turns gold on overcharge), walking bob, recoil kick, a muzzle flash, hidden outside Playing and Paused. It is a prefab with no physics, so S3 can replace the model by setting four references again. |
| `ObjectPool<T>` | `Runtime/Pooling`, not `Managers/`, so tests can reach it. The player's blaster pools 12 bolts and 8 bursts, wired once when they are made, so firing creates no objects. |

If the wall is closer than the barrel tip, the shot is only the sparks: a bolt from the muzzle would start behind the wall.

**The gun is drawn by its own camera.** It is on the `ViewModel` layer (user layer 16) and a second, overlay camera under the player's camera renders only that layer, after the first, which no longer draws it. So the gun is always on top and can never be inside a wall, however close the player stands. The player's camera also has a near plane of 0.1 m, because at 0.3 m the plane's corners poked through a wall the player stood against at an angle (the body keeps 0.4 m from walls) and showed the empty space behind it.

**Built, not hand-edited.** `BlasterAssetBuilder` (menu *Factory Reset/Blaster/Build Blaster Assets*) writes the glow textures, the additive and gun materials, the three prefabs and the wiring on `Player.prefab`, including the overlay camera and the layer. It rewrites the same assets in place, so it can be run again; only whoever changes the look needs to. Everything it writes is committed.

### 6.5 Scoring (S3, implemented)

`ScoreRules` (`Scripts/UI/`, plain C#) is the single source of every point value; `ScoreManager` (one object, `Score`, in `UI.unity`) only listens and hands each event to it with the game time. No view computes points and nothing reads a brain.

| Event | Points | Heard from |
| --- | --- | --- |
| Switch restored (3 per run) | 1000 | `ChapterEvents.OnSwitchRestored` |
| Checklist task or power core | 300 | `ChapterEvents.OnTaskCompleted` (switches are not tasks, so no double count) |
| Takedown: Tracker / Guard / Saboteur / Captain | 150 / 250 / 400 / 600, times the combo, times the repeat factor | `AgentEvents.OnDisabled` (knocked out) and `OnDestroyed` (scrapped) |
| Battery pickup | 50 | **Not wired**: no event exists yet; `ScoreManager.BatteryPickedUp()` is the entry point |
| All four Saboteurs destroyed | 1000, once, right after the fourth takedown | counted from the takedowns |
| Win bonus | 2000 + `max(0, 3000 - 3 x seconds)` + `10 x HP` + `15 x accuracy%` (accuracy only from 10 shots) | `CutsceneSignals.FactoryShutdown`, then the game state reaching Results |
| Grade | S from 14000, A 11000, B 8000, C 5000, otherwise D | - |

- **Combo.** The first takedown is x1. A takedown within 6 s (inclusive) of the previous one raises the multiplier by one, to a cap of x4; a longer gap starts again at x1. The window runs from the previous takedown, not from the first of the chain.
- **Repeat decay is linear.** The same agent's first takedown is worth 100%, its second 75%, its third 50%, every later one 25%. Counted per agent id, so only the agents that reboot (Tracker, Guard, Captain) can decay.
- **Order and rounding.** Combo and repeat factor multiply, so their order does not matter; the product is rounded once, halves away from zero (a Tracker taken down a second time as the third of a chain: 150 x 0.75 x 3 = 337.5, scores 338).
- **The run.** It starts when Chapter 1 starts (which clears any earlier score) and ends at Results. Run time is game time, which stops in cutscenes and while paused. A run that reaches Results after the factory shut down is won and gets the win bonus; any other is "Recalled", keeps the points earned and gets no bonus.
- **Not available yet.** Blaster shots fired and hit are not in `Interfaces`, so the accuracy bonus is 0 until `ScoreManager.ReportShots` is fed. The player's HP for the integrity bonus is the health fraction times 100 (the Player prefab's default maximum), because only the fraction is exposed.
- **Tests.** `ScoreRulesTests` (EditMode) covers every row of the table, the window and cap at their exact edges, the decay and its floor, the one-off bonuses, the win-bonus formulas and the grade thresholds. `ScoreManagerTests` (PlayMode) drives the manager with a fake game clock and agents, and checks in the real scenes that completing a task scores 300.

## 7. Decision log

| Date | Decision | Alternatives considered | Because | Owner |
| --- | --- | --- | --- | --- |
| 2026-09-24 | Agents move with a `CharacterController` driven by `AgentPathFollower` (manual gravity, turn cap 360°/s), not `NavMeshAgent` | `NavMeshAgent`; Rigidbody physics | The brains plan their own grid paths; a `NavMeshAgent` replans by itself and would fight them, and a Rigidbody would be pushed around by boxes and other agents | S4 |
| 2026-09-24 | Brains get their dependencies once, through `AgentController.Initialise` at spawn | Look up the player, grid and blackboard with `FindObjectOfType` / `GetComponent` | No per-frame searches or hidden null references, and brains can be tested without a scene | S4 |
| 2026-09-24 | `AgentIntent.Path`: `null` keeps the current route, an empty list stops, a non-empty list replaces the route | A separate "stop" flag; resend the full path every tick | Most ticks have no new route, so `null` costs nothing; stopping is an explicit answer | S4 |
| 2026-09-27 | `DijkstraField` uses the same step costs as A* and reuses its arrays (stamp trick) | Recompute with fresh arrays; a separate distance metric | Field costs match A* path costs exactly, so the Captain's arrival times agree with the path it walks; recomputes allocate nothing | S4 |
| 2026-09-29 | Extend the game into a four-chapter journey: each control switch is sealed until its chapter's tasks are done, a fixed area order (Assembly Floor, Painting Room, Storage Area, Control Room), task props, six cutscenes, a Saboteur squad of four that is destroyed permanently, scoring with a leaderboard, and a `UI` scene. Ownership: chapters S1, task props S2, models, UI and scoring S3, cutscenes and agent execution S4. The Unity version (6000.6.2f1), the `develop` branch, the +Z-forward export pipeline and the no-Issues workflow are unchanged | Keep three switches restorable in any order with one Saboteur | A story gives the agents a reason to matter: the Captain predicts which task the player is heading to, and the Saboteur squad coordinates on shared targets. Each new piece has exactly one owner, so every individual Git history stays clean | Team |
| 2026-09-29 | Character models face Unity +Z; authored facing Blender +Y; character left is -X; every contracted pivot has rotation 0 and scale 1 | Keep the first greybox's -Z facing; rotate prefab instances 180° | S4's movement turns agents with `LookRotation(velocity)`, which assumes +Z forward, and identity pivots give clean local rotation axes for clips | S3 |
| 2026-09-29 | Export FBX with Forward Y, Up Z, Apply Transform off, Apply Scalings "FBX Units Scale", and rely on Unity's Bake Axis Conversion | The previous CONTRIBUTING settings (Forward -Z / Up Y / Apply Transform); the same without Apply Transform | With Blender 5.2, the previous settings put +90° X rotations and 0.01 scales on nested nodes, and without Apply Transform every root imported at +90° X. The validated settings give identity transforms and Y-up meshes on all four models. The team agreed, and `CONTRIBUTING.md` now uses these settings | S3 (team agreed) |
| 2026-09-29 | Freeze the greybox hierarchy in §8 once S4 accepts it; later detail meshes may only be added beneath frozen nodes | Allow renames during final modelling | Animation clips bind to hierarchy paths | S3 (accepted by S4 on 2026-09-30) |
| 2026-09-29 | Guard treads stay rigid assemblies under their pivots | Road wheels with individual pivots | The reference draws the treads as boxes and lists no tread animation; a scrolling tread material can suggest rolling later | S3 (S4 informed on 2026-09-30) |
| 2026-09-30 | Accept the greybox model hierarchy as the animation contract, after checking every model node by node | Record clips first and fix broken paths later | A clip bound to a renamed or moved node silently stops animating it, so the paths must be fixed before the first clip. The check compared each `.blend` source and FBX export against the greybox model contract (see its acceptance check) | S4 |
| 2026-10-02 | Accept the Unit 047 blockout hierarchy for the cutscene clips, after the same node-by-node check as the four robots | Wait for the final model before accepting | The clips (idle sway, head turn, key spin, eyes on/off) bind to node paths, and S3 can only finish the model safely once the paths are frozen. Accepting the blockout now lets both sides work in parallel | S4 |
| 2026-10-03 | A Runtime writer owned by the spawner turns S1's objective events into blackboard cells | The chapter manager writes the blackboard itself; each brain listens to the events | The chapter code never needs the blackboard (it lives in the Agents scene), only Runtime writes it, and positions become cells once instead of once per brain. `ObjectivesVersion` lets every brain detect a change by comparing one integer | S4 |
| 2026-10-03 | Agents freeze in every state except Playing by pausing the follower component, and all agent time is game time | Block only Cutscene and Paused; stop the path while frozen; keep Unity time for stuns | "Only act while Playing" also covers states added later (a chapter card) without a code change. A stopped path would be lost on resume, because the brain only returns `null` (keep walking). Unity time keeps running in the pause menu, so a stun would expire while paused | S4 |
| 2026-10-03 | Hand brains their dependencies in one `BrainSetup` struct, with one `AStarSearch` shared by every brain | Add a `CreateBrain` parameter per dependency; one pathfinder per brain | New dependencies (the squad's shared claims next) stop changing every owner's `CreateBrain` case. A shared search keeps one set of per-cell arrays, and its fresh result lists make sharing safe | S4 |
| 2026-10-03 | Test scenes bake their NavMesh at runtime instead of committing the bake | Commit a test-scene NavMesh asset | Bake output goes through Git LFS and is S1's to commit; a 30 x 30 m test floor bakes in milliseconds and can never go stale | S4 |
| 2026-09-30 | Smooth paths in two stages: string pulling with a grid line check, then a centripetal Catmull-Rom spline that falls back to straight near walls | NavMesh raycasts for the line check; a uniform Catmull-Rom spline; Bézier corner rounding | The line check uses the same grid and corner rule as A*, so smoothing can never allow a move the brain's search forbade, and it is testable without a scene. Centripetal splines have no loops or cusps and pass through every waypoint; the straight fallback keeps the curve out of walls | S4 |
| 2026-10-01 | Keep the `ResponseCurve` library in `Scripts/AI/Agents/Saboteur/`, although the v2 responsibilities matrix lists it under AI Core | Move it to `AI/Core` | Only the Saboteur uses it, so keeping it beside its users avoids a shared dependency and any change to AI Core. It moves only if another agent needs it and the team agrees | S3 |
| 2026-10-01 | Propose, for S2's review, a Saboteur-owned `ICostModel` that returns infinity for a door's cells to cost a hypothetical closure, treating an infinite total as a lockout; the live grid is never mutated | Mutate and restore the live grid; clone the grid per door | `ICostModel` only requires at least the base cost, so infinity is allowed, and nothing shared changes. Status: built (`DoorClosureCostModel`, `DetourCache`); tested that `AStarSearch` needs no change: with another route it goes around, and when the only route crosses an infinite step it returns that route with an infinite summed cost, so a lockout is read from the cost, not from `Found`. S2's confirmation outstanding | S3 |
| 2026-10-02 | Unit 047 blockout: legs under the root, body, head, arms, blaster and key as rigid parts with pivots at joints; one `Eyes` mesh with its own material; decal carrier plates built in now | Legs under the body pivot; separate eye meshes; add decal plates later | Planted legs let the body sway without foot sliding; one eye material is one switch for eyes on/off; plates now mean the final decals never change the hierarchy S4 animates. Sized so its eyes meet the Guard's visor (1.91 m): the hero should read as an equal of the robots it fights, not a small prop. A 1.57 m first version read as smaller than the Saboteur, and a 2.20 m second version still sat below the Guard's eye line. Accepted by S4 on 2026-10-02 after a node-by-node check | S3 (accepted by S4 on 2026-10-02) |
| 2026-10-04 | Brains report items they leave behind through an AI.Core `IDropsItems` contract (a caller-owned buffer of `ItemDrop`: kind, id, cell), read by the runtime after `OnDestroyed`; Saboteur A reports the keycard on the nearest traversable cell and widens the search to the whole grid before giving up | The brain calls a spawn callback; pass a spawner into the brain; keep a fixed search radius and accept a lost keycard | A brain is pure C# and cannot spawn objects, and the keycard is the Chapter 3 task item, so a drop on a blocked cell or no drop at all would softlock the game. A caller-owned buffer allocates nothing. The shape was agreed with S4 on 2026-10-04; what `ItemId` means is still open with S4 and S2. Added after the 1 October interface freeze, so it needs all four reviewers | S3 (agreed with S4) |
| 2026-10-04 | Chapter contracts added after the 1 October interface freeze: `ITaskAnchor`, `TaskEvents.OnTaskSpawned`, and `ChapterEvents.OnSwitchUnsealed` / `OnSequenceChosen`. Fuses and power cores are one task each (one objective per prop) | Reference `TaskAnchor` from Journey; give the switch cage a direct reference to the chapter manager; one counted task for the three fuses and one for the three cores | Journey cannot reference Runtime, and the cage, relay prop and HUD live in other assemblies and scenes, so events are the only clean link. The keycard does not exist until Saboteur A drops it. One task per fuse or core gives the Captain and the Saboteurs a real position for every target instead of one point for three props. Needs all four reviewers | S1 |
| 2026-10-04 | The spawner owns the Runtime pieces that feed brains: a per-frame `PlayerStateWriter`, a `ChapterIndexWriter`, and `AgentHearing`, which propagates each noise once and hands every agent the level at its cell | Each brain reads `PlayerState` or listens to `NoiseEvents` itself; propagate a noise once per agent; the player writes the blackboard | Brains stay pure C# and never see Unity objects or events. One propagation gives every cell's level, so the cost does not grow with the number of agents. Only Runtime writes the blackboard, and the spawner already owns it. Running the spawner first each frame keeps the player snapshot one frame fresh for every brain | S4 |
| 2026-10-04 | Saboteur brains over one `TargetClaims` share one `SquadCoordinator`, found through `SquadCoordinator.For(claims)`, and get their sabotage and combat candidates from an `ICandidateSource`; the brain applies the squad layer to every candidate and staggers its decisions on a fixed phase per letter | A coordinator passed in by the spawner; each brain with its own coordinator; a static registry; the candidate list hard-coded in the brain; stagger by a fixed delay after each decision | The spawner's `CreateBrain` call keeps its current arguments (S4's file), the squad state still lives only in `TargetClaims`, and the weak table cannot outlive a level. A source keeps each action's scoring separate from squad rules, so the squad layer is testable now, before any action exists. A fixed phase cannot drift, so decisions never bunch into one frame | S3 |
| 2026-10-05 | The cutscene director keeps its rules in a plain C# runner behind a small playback interface; cutscenes without a Timeline hold for a placeholder time; every listed Critical signal fires at the end as well as on skip | Put the logic in the MonoBehaviour; block the journey until each Timeline exists; fire missed signals only on skip | The runner is tested without Unity (EditMode) and the PlayableDirector part with one real Timeline (PlayMode). Placeholders let the team play the whole journey now. Firing missed signals at the end too means a Timeline with a forgotten marker still wakes the Captain | S4 |
| 2026-10-05 | The Captain's prediction reaches the blackboard through an `IGoalPredictor` interface that the controller copies after each tick; the wake reaches the brain as a blackboard flag set by a Runtime writer; the Captain also wakes once Chapter 3 has started | Let the brain write `PredictedGoal` itself; let the brain listen to cutscene events; wake only on the signal | Only Runtime writes the blackboard, and brains cannot see the Interfaces events, so both crossings go through Runtime, like `IDropsItems`. The chapter fallback means a missed or reordered signal can never leave the boss asleep for the chapters it guards | S4 |
| 2026-10-05 | The four Saboteur colours come from one mesh and one material: `SaboteurTint` (`Scripts/Runtime/Visuals/`, `ToyFactory.Runtime.Visuals`) reads the squad slot in `Start` from the `IAgentState` above it and writes the Art Bible tint into a `MaterialPropertyBlock` on the body slot only (`SetPropertyBlock(block, materialIndex)`); it does nothing unless that agent is a Saboteur with a squad slot | `renderer.material` per instance; four material assets (`Saboteur_A` to `Saboteur_D`) assigned to the body slot; four prefab variants; a tint in the shader keyed by an id | The spawner uses one shared body for every agent type for now, so the component must be inert on any other agent, and `renderer.material` would make a copy per renderer that has to be destroyed. A property block copies nothing, runs nothing after `Start` and needs no change to S4's spawner or controller. It costs SRP Batcher compatibility on the tinted renderer: four Saboteurs drew with 19 SetPass calls against 9 for one shared material or for four materials (see the Optimisation Log), so four pre-made material assets are the cheaper alternative if SetPass calls ever matter | S3 (agreed with S4 on 2026-10-04 and 2026-10-05) |
| 2026-10-06 | Agent clips are generated from sine-wave specs by an editor builder; one 2D Blend Tree over Speed and TurnRate per agent; wheels and the wind-up key turn in code; bodies nest S3's model prefabs | Hand-key every clip in the Animation window; separate lean layers; keyed wheel spin; copy the models into new prefabs | Every number in a clip is written down and explainable, and a rebuild is one click, so tuning in the level is quick. One tree blends speed and lean together without layers fighting over the same pivot. Code-driven wheels match any speed exactly. Nesting keeps S3's prefabs and their colliders the single source of truth | S4 |
| 2026-10-06 | Add a `FactoryShutdown` Critical signal, fired by the ending cutscene | Let the lighting listen for `OnCutsceneStarted("ending")`; make the shutdown part of the results screen | A Critical signal is the agreed way for a cutscene to change the world, and it also fires when the ending is skipped, so the factory always goes dark before the results. Listeners compare against a constant, not a cutscene id that could be renamed. Adding a constant changes no existing contract | S4 |
| 2026-10-06 | Spawn points follow the level layout's room plan, with short patrol routes on open floor checked against the built grid | Spawn every agent near the player start; long patrols through doorways | Each chapter meets its own threat in its own room, as the level plan intends, and short routes inside one room keep agents from bunching at doors before the player arrives. Checking against the real grid catches a point placed inside a press or shelf before it fails at runtime | S4 |
| 2026-10-06 | Agents are hit through their capsule on the Agents layer; shots are a body-side 0.3 s telegraph then one hitscan, with no friendly fire; the brains only decide when to shoot | Per-part hitboxes; projectiles; each brain timing its own aim | The capsule is already there and is skipped by S2's cover check, so hits need no new colliders. Hitscan is cheap and deterministic, and the shared telegraph is what makes it fair: the player always gets 0.3 s to take cover. One implementation means every agent telegraphs the same way, and the brains stay pure decisions | S4 |
| 2026-10-06 | Agents fall apart by detaching S3's rigid mesh parts as Debris physics bodies and flying them back on a smoothstep in the last second of the knock-out; the "?"/"!" icon reads an optional `AlertLevel` on `AgentIntent`, with a state-name fallback | Pre-made broken prefabs or a fall-apart clip; icons only from state names | The parts and their colliders already exist, so nothing is spawned and every model works the same way; physics makes each fall different. An explicit level survives state renames, and the fallback means no brain owner is blocked. Adding a field with a default changes no existing brain | S4 |
| 2026-10-07 | A pluggable in-game debug overlay (F3): a base layer for every agent and one layer per brain, registered from each owner's own file; layers draw into a canvas that the overlay renders with GL lines and IMGUI labels | Scene-view gizmos only; one overlay class that knows every brain | The overlay must work in the Game view during play and in the demo video, where gizmos do not show. Registration keeps each owner's layer in their own file and history, so nobody edits the overlay to add an agent. Drawing into a canvas keeps layers testable without a camera | S4 |
| 2026-10-08 | The cutscene director plays the intro itself on the first Playing state, once per run | The scene loader calls `Play("intro")` | The director already owns every other cutscene trigger, so the intro needs no change in S2's loader. Waiting for Playing (not Awake) means it starts after loading and after a future title screen, and the beacon appears when it ends, as S1's lighting expects | S4 |
| 2026-10-08 | Cutscene dialogue is a per-cutscene script asset of shots, played by a plain C# runner; Timelines start shots with markers that can hold the Timeline until the lines are said; subtitles are events; blips are generated tones | Lines as Timeline text clips; fixed shot lengths; audio files per blip | The words live in one place, built from Story.md, so the script and the game cannot drift. Holding at a marker makes reading speed the player's choice instead of cutting lines off. Events keep S3's UI free of any reference to the director, and a placeholder bar lets the cutscenes be watched now. Generated tones need no assets and match the cast table exactly | S4 |
| 2026-10-08 | The cutscene director adds a Cinemachine brain to the gameplay camera only for the length of a cutscene, with a gameplay-view camera parked at the player's eyes; Timeline tracks for objects in other scenes are bound at start by their `CutsceneBindingId` name; the letterbox is its own runtime canvas | A brain and gameplay vcam on S2's player prefab, always running; dragging cross-scene references into the Timeline; letterbox bars keyframed in each Timeline | Adding the brain at runtime leaves S2's prefab untouched and costs nothing during gameplay, and restoring the local pose means mouse look never fights Cinemachine. Unity cannot save cross-scene references, so a name lookup once per cutscene is the only reliable binding. A letterbox driven by the cutscene events closes and opens the same way for every cutscene, skipped or watched, without repeating it in five Timelines | S4 |
| 2026-10-06 | The objective beacon shows while the game clock is Playing and no cutscene runs; its target rule lives in `ChapterFlow`, and `ChapterManager` exposes the result as `CurrentBeaconTarget` | Show it only after `OnCutsceneEnded("intro")`; let the beacon or the HUD each work out the target | Nothing plays the intro yet, so an intro-only gate would hide the beacon in every current build; the intro is a cutscene, so the game-state rule still hides it until the intro ends. One rule in the plain C# flow is tested without a scene, and the beacon and the HUD can never point at different things | S1 |
| 2026-10-06 | The Control Room alarm beacons sit on the lintels of the two sealed doors, each with a red real-time light, as in the prototype; the Painting accent light becomes baked | Keep the beacons on the Control Room's back wall; add the door alarms as a fourth and fifth real-time light | The alarm marks the locked doors from the rooms the player is in, and the unlock is visible on camera in `ch3`. The plan allows 2-3 real-time point lights: Storage lamp plus two alarms is three, and the pink Painting fill looks the same baked | S1 |
| 2026-10-06 | `LightingState` reacts to Critical signals only, swaps shared materials for the alarm, and never goes back to an earlier mood | Animate the lights in the Timelines; tint with a MaterialPropertyBlock | Signals also fire on skip, so the lights cannot be left red after a skipped `ch3`. A property block breaks the SRP Batcher (S3 measured +10 SetPass for four Saboteurs); two shared materials keep it. Moving forward only means a late or repeated signal cannot relock the doors' lights | S1 |
| 2026-10-08 | `IAgentState` gains `Position`, the body root's world position, read live and kept after the body is destroyed | Cast the agent to `Component` and read its transform; a registry of agent positions in `Interfaces`; publish positions on the blackboard | S1's Chapter 3 beacon must follow Saboteur A while it moves, and S2's keycard must drop where it fell. Both already receive the agent as an `IAgentState`, so one read-only property serves both without a new system. A cast to `Component` ties `Interfaces` users to Unity objects and fails for a test fake; the blackboard is for brains and only Runtime writes it. A frozen interface changes only with all four reviewers | S4 (agreed with S1 on 2026-10-08) |
| 2026-10-08 | The cutscene Timelines are built by an editor script from a shot plan in code and the dialogue scripts; each shot is two cameras blended over the shot, timed by its lines; a shot end marker holds the camera for a line still on screen and jumps to the shot end when the lines are clicked through | Hand-authored Timelines; one animated camera per shot; Timelines that stop at every dialogue marker | A plan in code can be checked by tests against the 6 m ceiling, the walls and the obstacles, and a rebuild keeps the Timelines in step with the script. Two cameras and a blend need no animation clips and give the ease-in-out move of the prototype. Stopping at each marker froze the camera while the lines were said; pacing the shot by its lines keeps it moving and still never cuts a line off | S4 |
| 2026-10-08 | Rooms get a ceiling at 6 m, as in the prototype; walls raised from 5 to 6 m, lintels fill 4-6 m. They are lit by nine ceiling panels per room, each with a baked rectangle area light, and the sun is switched off | Keep the open-top rooms lit by the sun; a ceiling lit by point fills only | The prototype is enclosed, and a ceiling makes each room read as an indoor space. It also lets occlusion culling hide the next room: the measured cut rose from 15% to 42% (Storage). Under a ceiling the sun cannot reach the floor, so baked area lights under the panels give the soft, even light the prototype has, at no runtime cost. The ceiling has no collider, so the NavMesh and grid are unchanged | S1 |
| 2026-10-08 | Room dressing from the prototype (stocked shelves, paint puddles, server racks, turning gears) is visual only: no colliders and nothing new on the floor | Build the prototype's denser shelf maze, paint mixers and extra server pillars as new obstacles | New obstacles change the grid, which would invalidate the NavMesh, S4's patrol routes and cutscene checks, and S1's measured GBFS, noise and probe tables a week before the deadline. Dressing that leaves the grid untouched gives each room its prototype character at no risk to the AI | S1 |
| 2026-10-08 | Prototype surface detail (hazard skirting, floor tiles, Control glow grid) comes from three small tiling textures; floors get their own tiled materials | Flat colours only (Art Bible's "no detail textures"); modelled stripes and tiles; reusing the floor tint materials | The prototype's look depends on these surfaces, and a 256 x 256 tile repeated by UV costs about 87 KB and one SetPass call, where modelled detail would cost thousands of triangles. Separate tiled floor materials keep the tile pattern off the toys and puddles, which share the floor tints | S1 |
| 2026-10-09 | Rebuild the four rooms' obstacles cell for cell from the HTML prototype (presses, paint tanks with the targets on poles, shelf maze, twelve server pillars round a central console); supersedes the 2026-10-08 "dressing only" decision | Keep the greybox obstacles and only dress them | The team agreed the game must play like the prototype: the targets on rotating poles, the maze and the console in the open are what make each chapter's task readable. The cost was accepted: a new grid (4,963 walkable cells), a NavMesh rebake, S1's search and noise evidence re-measured (same conclusions), and S2 and S4 told which props, routes and shots to recheck | S1 |
| 2026-10-09 | Replace the environment's chamfered boxes with true rounded boxes, rebuilt in place by an editor tool | Keep the 45 degree chamfer; model each obstacle in a DCC tool | The chamfer read as a hard edge from a few metres, so the level did not look like moulded plastic. Rounded boxes built in code keep every asset's GUID (no scene edits), keep the exact colliders (no grid change) and cost no extra SetPass calls; triangles roughly triple in the box-heavy rooms, still about 70k per view | S1 |
| 2026-10-09 | Bodies stop short of the player (`PlayerStandOff`) and the Tracker pounces with a keyframed whole-body hop added after the Animator; no knockback | Walking the brain's route onto the player; a knockback on the player (needs an `IPlayerState` change and S2's controller); a new bite clip on an override layer | The ramming came from the body following a route that ends inside the player, so the body is the place to stop it, for every agent at once. Curves added on top of the clips need no new layer that could fight the locomotion tree, and a whole-body hop reads as an attack where a head-only snap did not. Knockback was dropped: it changes the player's controls, which are S2's | S4 |
| 2026-10-09 | Debug chapter jump (F6-F8) drives the real chapter flow forward and skips each cutscene, then moves the player | A start-at-chapter hook in `ChapterManager`; setting the flow's state directly | Going through `CompleteTask` and the cutscene runner means every listener sees the same events as in play, so the jumped-to world is the real one (doors open, Captain awake). It needed no change to S1's chapter code | S4 |
| 2026-10-09 | Shooters turn to the target while aiming and fire only within 25 degrees of it, else drop the shot; each shot kicks the firing arm, torso and head | Firing from wherever the body faces; snapping the body round at the shot | The Captain was seen shooting out of the side of its cannon while walking across the player. Turning during the 0.3 s telegraph is visible and fair (the player sees it line up), and dropping a shot it cannot line up keeps every tracer leaving the barrel. The static aim pose did not read as firing; a kick per shot does | S4 |
| 2026-10-09 | The HUD builds its uGUI objects in code under one `HudPresenter` in `UI.unity`, draws text with Unity's built-in font, and its pure parts (`HudMath`, `HudModel`) are tested from the EditMode and PlayMode test assemblies, which now reference `ToyFactory.UI` | Author the canvas by hand in the scene; use TextMeshPro; test only in Play | The cutscene letterbox already builds itself this way, so the scene is one component and cannot conflict on merge. TextMeshPro needs its essential resources imported into the project (fonts, settings, shaders), which a plain HUD does not justify. The bar fractions, the vignette and the arrow placement are plain arithmetic, so they are tested without a scene; the test assemblies need the UI reference for that, a shared-file change recorded here | S3 |
| 2026-10-09 | The Captain's distance fields read a shared neighbour table, single-pair costs use a cost-only A*, and stale goal fields are repaired in place, one per frame | Recompute every stale field in the next decision; spread full recomputes over frames only; ask S2 to add a cost-only mode to `AStarSearch` | The stress test showed 67 frames over 1 ms (worst 15 ms) in normal play and, with a box being pushed, 32 ms at p99, because each grid change recomputed every goal field in one decision. Profiling put 84% of a field's time in neighbour gathering, which only changes with the grid. A field to answer one number wasted the whole level. Spreading full recomputes alone still left 128 frames over 1 ms. The repair touches only the cells behind a change, and a random-change test proves it equals a fresh field. Everything stays in S4's search code, so `GridGraph` and `AStarSearch` are untouched | S4 |
| 2026-10-09 | The Captain's fight has hysteresis: it starts at 10 m in view, keeps going while in line of sight within 14 m, lasts at least 2 s, and on losing contact pursues to the last-seen spot (new Pursue state) before predicting again | Keep one 10 m threshold with a 0.7 s grace; lock on until the player leaves the room | A player stepping in and out of 10 m flipped it between fighting and backing off, which looked irrational. Separate start and keep thresholds plus a minimum time stop the flipping, the way a thermostat does, and going to the last-seen spot reads as hunting, not retreating. A room lock needs room data the brain does not have, turns the Captain into a plain chaser, and can be beaten by standing in a doorway | S4 |
| 2026-10-09 | Downed agents tip over with a small explosion and one comic word, their lights go out and flicker back at the reboot; Saboteurs sink away. Replaces falling apart | Keep the fall-apart; copy the prototype's word on every hit | The team found the parts scattering hard to read and the word on every hit distracting. One explosion, one word and a body lying on its side say "down" at a glance, and the lights coming back say "it is back". Ordinary hits keep sparks only. Visor and goggle lenses are added on the body because S3's visors and goggles are frames | S4 |
| 2026-10-09 | The Captain kneels on one knee when knocked out and steps back up at the reboot, posed in code over the Animator; the other agents still tip over | Tip the Captain over like the rest; a kneel and stand-up clip on an override layer; a ragdoll | The boss falling on its side like a toy undercut it, and a kneel reads as "beaten for now", which suits an enemy that gets back up. Posing in code reuses the knock-down's timing, needs no new Animator layer that could fight the locomotion tree, and lets the floor contact be measured and tested. A ragdoll could not stand back up | S4 |
| 2026-10-09 | The dormant Captain kneels switched off and rises at the wake (visor first, then the stand-up); in the ending every agent powers down in a cascade (lights out, Animator winds down, body sags; the Captain kneels). Both are poses in `AgentKnockdown`, driven by the blackboard and the Critical signals | Animate the wake and the power-down in the Timelines; keep the Captain standing while dormant; switch every agent off at once | Poses driven by state work the same when a cutscene is skipped (the signals still fire), and the wake shot needs no animation track bound across scenes. A boss that rises on camera sells "it wakes" far better than a light. A cascade by spawn order reads as a factory shutting down, and costs nothing | S4 |
| 2026-10-09 | The chapter card is a runtime canvas on the director, shown on `OnChapterStarted` but held back until any cutscene has ended | Show it as soon as the chapter starts; put it in each Timeline; wait for S3's UI scene | A chapter starts as its cutscene ends, and Chapter 1 starts around the intro, so holding the card until no cutscene plays is the one rule that always puts it after the cutscene. Listening to the chapter event keeps it right for skipped cutscenes and the debug jump. A canvas built in code needs no assets and does not wait for the UI scene | S4 |
| 2026-10-09 | The Captain's line of sight ignores grid blockers (props and boxes) and is blocked only by walls and closed doors (`GridLineCheck.IsSightClear`); closing in, being held by the stand-off near the player is not stuck | Keep one grid check for walking and seeing; take props back out of the grid; physics raycasts for sight | Once props blocked their footprint (needed so agents walk round them), the shared walking check also hid the player behind a 1 m console from a 3.25 m boss, which then stood 2 m away and never fought. One trace with two rules keeps sight and walking on the same grid and testable without a scene; a raycast would tie the brain to physics. Shorter agents' sight is their owners' call | S4 |
| 2026-10-09 | Replans are joined with a Bézier curve from the current heading (25-120 degree turns, grid-checked); speed eases at 12 m/s²; a per-frame 2 ms budget spreads brain decisions over frames (at most 2 frames' wait) as the path request scheduler | An asynchronous path queue; a time-sliced A*; blending every route change; no speed easing | The brains plan synchronously and belong to four owners, so a queue would have meant rewriting all of them; budgeting decisions spreads the same searches with no brain change, and brains are time-based, so a frame's wait changes nothing. Blending only the turns that make a pivot, and only where the grid allows, keeps the brain's route and its timing; turns back stay turns on the spot | S4 |
| 2026-10-09 | Cutscene alarms and comic words are Timeline markers placed by the builder from a cue table, played by a cue player on the director; words are pre-rendered sprites, the alarm is generated | Cue by shot through `ShotStarted` (like Unit 047's beats); Signal Emitters with hand-placed receivers; TextMeshPro words; recorded alarm audio | Markers show the cues in the Timeline window and can be nudged there, and the builder places each one as its line starts, so "DEFECTIVE!" lands with the line that says it. A cue table in code keeps them in step with Story.md and is tested. Sprites match the existing knock-out word and need no font asset; a generated alarm needs no audio files, like the voice blips | S4 |
| 2026-10-09 | The Captain opens doors: a door-aware A* beside the shared one (a closed door costs 11 cells, about 1.2 s); it stops, emits a new `AgentAction.OpenDoor` and carries on; it shuts a door behind it when that door is on the player's predicted route | Treat doors as walls (a player could lock the boss out with one door); let the body open any door it walks into; add closed-door support to the shared A* and the distance fields; never shut doors behind it | One shut door kept the boss out of the fight. A brain action keeps "the brain decides, the body executes" and gets the same success/failure answer as the Saboteur's CloseDoor. Keeping the router in the Captain's code leaves S2's A* and the shared fields untouched. Opening without shutting would undo the Saboteurs' closed doors; shutting only doors on the player's predicted route makes the boss and the squad work together. Needs all four reviewers (shared enum) | S4 |
| 2026-10-09 | Captain robustness after a review with scripted runs and the real level: a goal it cannot reach is no plan; Intercept re-asks for a route while it has none; a target it does not move towards for 2 s is given up and its cells avoided for 10 s; a new Converge state closes in on a player busy at the only goal; the plan commits at 0.5 but is kept down to 0.4 and only switches goal on a 0.15 lead. The body now honours `LookTarget` while standing, and skips a first waypoint it has passed | Leave Observe as the answer to every doubt; a room lock; time out every state; make the body face its look target even while walking | Each change answers a measured failure: frozen for 9.5 s behind a shut door and after a skipped Chapter 3 cutscene, ambushing 133 degrees away from the player and noticing them at 2.5 m, watching from 23 m while the player held the console, and three stop-starts in 13 s at the 0.5 edge. Movement, not distance, measures progress, because a route round shelves can lead away from the target. Facing while walking would make bodies walk sideways | S4 |
| 2026-10-09 | Unit 047's cutscene animation (sway, breathing, key, head, eyes) is code on its pivots, with story beats cued by shot through a new `CutsceneDirector.ShotStarted` event | Sine-wave clips from the agent animation builder plus Timeline activation tracks for the eyes; markers per beat on each Timeline | The beats belong to shots, and a shot's length depends on the player's reading speed, so a clip or a fixed Timeline time could not stay in step. Cues by shot index survive a Timeline rebuild, live in one list, and need no cross-scene binding. Code on rest poses matches how the wheels, key and kneel already work | S4 |
| 2026-10-09 | Placeholder subtitles move from IMGUI to a scaled uGUI canvas with a dynamic font | Keep IMGUI with a bigger font; import TextMesh Pro's essentials | IMGUI draws a fixed-size bitmap font that pixelates when the view is scaled; a canvas scaler and a dynamic font draw the glyphs at their final size. TextMesh Pro would add a shared asset folder for a placeholder that S3's UI replaces | S4 |
| 2026-10-09 | The controller carries out door, trap and battery actions through an id registry in `Interfaces` (`SabotageTargets`), answers every request exactly once through an optional `IActionFeedback`, and tells a brain its health through an optional `IHealthAware`; melee moves deal their damage at contact (Saboteur 10, Tracker 0) | Brains find props themselves; `FindObjectOfType` by id; put health in `AgentContext`; damage when the move starts; give the Tracker's pounce damage | Brains are pure C# and the props live in another scene, so ids are the only link, and a registry filled by the props costs one dictionary read. One answer per request (including failures) means the Saboteur's claims never wait forever. Optional interfaces leave `AgentContext` and the other brains unchanged. Damage at contact makes the wind-up a telegraph. The Tracker's design has no contact damage, so its value stays 0 until S1 decides. Agreed with S3; new interfaces after the freeze, so all four reviewers | S4 (agreed with S3 on 2026-10-09) |
| 2026-10-09 | The player's gun is drawn by an overlay camera on its own layer, and the main camera's near plane is 0.1 m | Pull the gun back when a wall is near (it reaches 1.3 m ahead of the camera and cannot fit in front of a camera 0.4 m from a wall); keep the 0.3 m near plane (its corners cut through walls the player stands against at an angle) | The prototype draws its gun in a separate scene for the same reason. A second camera makes the gun always on top and never inside anything, and costs one layer (16, `ViewModel`) | S2 |
| 2026-10-09 | A blaster shot is a hitscan hit with a flying picture of it (core, camera-facing glow, spark burst) from pools, sized like the prototype's | Real projectiles like the prototype (damage would arrive late and miss moving agents); a thin line tracer (it shrank to nothing when flying straight away, and looked small) | The hit has to be instant and exact for the Guard's and the Captain's logic, and S4's agent hit sparks are instant. The bolt at 90 m/s is slow enough to be seen and the sparks appear where it lands | S2 |
| 2026-10-09 | `ObjectPool<T>` lives in `Runtime/Pooling`, not `Managers/` as the plan has it | `Managers/ObjectPool` | `Managers/` is the default assembly, which no test assembly can reference; in `Runtime` the pool is covered by PlayMode tests | S2 |
| 2026-10-10 | Unsure and out of contact with the player for 3 s, the Captain guards the most likely goal (`InterceptKind.Guard`) instead of watching; Observe's back-off step gets the 2 s stuck check | Keep watching (it stood showing "?" for good after the player fled into Storage in Chapter 4); hunt the player's live position (perfect knowledge, no prediction); predict only the console in Chapter 4 (one goal makes the confidence always 1, so there is nothing to infer) | Watching only means something while there is something to see. The likeliest goal is where the player must come in the end, so waiting there uses the prediction instead of cheating. Agents are not in the grid, so a body lying in an aisle pinned the back-off step: the same stuck check as the other states lets it go | S4 |
| 2026-10-10 | The Captain shows its prediction: a red flash on each goal it commits to, and a comic speech bubble above its head while the player can see it, rate-limited; a goal switch within 6 s gets "feint" lines | Keep the prediction invisible; a bottom-of-screen subtitle; plain text over its head; lines through walls or pinned to the screen edge when it is off screen; recolour S1's yellow beacon | Unseen, the prediction reads as luck or cheating, and the player has no counterplay. Seen, it becomes the boss's game: read, then outthink it. A bubble from the bot reads as the bot talking, in the same comic style as the cutscene pop-ups. Lines only in sight keep it from becoming a wallhack. The flash covers the off-screen case without clutter. The yellow beacon means "go here" and must not change meaning, so the Captain has its own red one (S1's meshes and shader) | S4 |
| 2026-10-10 | Animation speed is the speed the body really moved at (`AgentPathFollower.GroundSpeed`), capped at the commanded speed; the weapon releases the body's facing once instead of every idle frame | Commanded speed (a pinned agent ran on the spot); ease it per agent | `IAgentState.Speed` already promises ground speed. Capped, a push or a teleport never shows as walking. The weapon's every-frame release cancelled the controller's look target whenever it updated after the controller, so a Captain guarding the console stood facing the wall | S4 |
| 2026-10-10 | Captain evidence is measured on a snapshot of the real level (grid and goals taken in Play mode) with seeded scripted players, against two simple predictors and a plain chaser; sealed goals (a switch while its chapter's tasks remain, the console until the cores are down) get a 0.05 prior share instead of 0.25 / 0.15 / 0.50 | Measure in live Play mode (slow, not repeatable, needs a person); hand-made test rooms only; keep the plan's console share of 0.50 in Chapter 4 | A snapshot keeps the level's real geometry while making every run deterministic and seconds long, so it can sit in the suite as a regression check. The first runs showed the plan's console share made the Captain sure of a console the player could not use yet: confidently wrong on 53% of straight walks to a core. The sealed share took that to 0% and Chapter 4 accuracy from 30% to 71% | S4 |
| 2026-10-10 | When the Captain guards and the likeliest goals are tied, it guards the one nearest the player by walking distance, moving a held guard only for a goal 8 m nearer, and a knock-down drops its commitment; it keeps reading the player's position from the blackboard (Factory OS's cameras in the story) rather than only what the team can see | First goal in the list (it won every tie whatever the player did); a random one of the tied goals; a belief map of where the player could be, fed by the team's sightings | With nothing in the movement to choose between tied goals, the nearest is the one the player is likeliest to reach first. The prediction needs a continuous track and the intercept a single arrival time to race, which a belief map would not give; it would have replaced the tested design days before submission. The Captain still only fights what it sees, and shows its prediction through the call-outs. Found live: a knock-down left the commitment in place for the first decision after the reboot, so the old guard survived it | S4 |
| 2026-10-10 | Scoring is a pure `ScoreRules` class fed by a `ScoreManager` on game time; repeat takedowns decay linearly (100, 75, 50, then 25% of base), combo and decay multiply and are rounded once, and a run is won only if the factory-shutdown signal came before Results | Compounding decay; rounding each factor; deciding the win from the final cutscene's id or from the player's health at Results | The plan's "25% less per repeat, floor 25%" needs a floor only if the steps are linear, so linear is the reading that gives the floor a job. Multiplying then rounding once removes any order dependence. The shutdown signal is the one event only a won ending raises (a death goes straight to Results), so it separates the two without reading a cutscene id or a health value | S3 |
| 2026-10-10 | The Saboteur answers the controller's `IActionFeedback` (door cooldowns start on the runtime's success or failure, replacing the grid guess) and `IHealthAware` (own health is a second AttackPlayer consideration); the squad shares one `DetourCache` per blackboard, and CloseDoor is wired into the spawner | Keep reading success from the grid; leave own health out of the attack score; one cache per brain | The controller already answers each request exactly once, so a failure (out of reach, knocked out) now ends the plan at once instead of after a timeout. The design prefers a healthier Saboteur for attacks, and the health fact now exists. One cache per blackboard keeps the A* count the same for four Saboteurs as for one. Probing the real level found that every door is the only link between its two rooms, so the lockout rule rejects every closure: the behaviour needs a loop in the level (S1) to show | S3 |
| 2026-10-10 | The Tracker sees on a sight layer: `GridManager` marks the cells holding something at least 1.3 m tall when it builds the grid, and only those and closed doors block its sight (`GridLineCheck.HasLineOfSight`), skipping the cells at both ends; its cone becomes the prototype's 16 m and 72° each side, all round within 2.6 m | Keep tracing walkable cells with a 0.8 m inset; trace `IsSightClear` (walls only, as the Captain); physics raycasts from the brain | Tracing walkable cells blocked sight at every prop, however low, and at the 0.55 m clearance round it, so in the dressed rooms the Tracker missed a player in plain view. `IsSightClear` still uses walkability, so it keeps the clearance band and sees through nothing taller than a crate either way. A height probe at build time is the prototype's rule (solid height 1.3 m or more), costs one physics query per cell once, and keeps the brain pure C# | S1 |
| 2026-10-10 | The player's footsteps make noise (walking 25 every 0.45 s, sprinting 46 every 0.32 s); the player's source id is never a repeating lure | No footsteps (the Tracker only heard shots and toys); one loudness for both; a separate source id per step | The Tracker is designed to hunt by ear, and the prototype's footsteps (3.5 m walking, 9 m sprinting) are what make sneaking matter. With one source id for the player, steps 0.45 s apart would have counted as a repeating lure, so the player is excluded: a trail is followed through Investigate, not watched | S1 (in S2's `PlayerController`, agreed with the team lead) |
| 2026-10-10 | The Tracker's pounce deals 8; while a wind-up toy rewinds, each blaster hit costs 2 hit points | Keep the pounce cosmetic; 9 as in the prototype; double damage through a new `IDamageable` overload | Being caught by the Tracker cost nothing, so the chase had no stakes. 8 sits just under the Saboteur's swipe (10) because the Tracker pounces more often. The rewind's double damage was in the design and shown on the overlay but never applied; `AgentController.TakeHit` reads the brain's existing `IWindUpState.IsRewinding`, so no interface changes | S1 (in S4's `AgentController` and `AgentBite`, agreed with the team lead) |
| 2026-10-10 | `NoiseEvent` gets an optional `IsLure` flag (default false), carried to the brain as `SensorSnapshot.NoiseIsLure`; the thrown wind-up toy sets it, so the Tracker is Distracted by the toy's first tick | Keep detecting a lure only once it repeats; a noise kind enum; emit two ticks on landing | Waiting for the second tick made the Tracker react 0.6 s after the toy landed (Investigate first, or nothing at all while hunting), which read as slow. The prototype reacts to the first toy noise. An optional flag keeps every other emitter and test unchanged; a kind enum would need every emitter to choose one. Measured in play: Distracted one frame after landing | S1 (in S2's `WindUpToy` and S4's `AgentHearing`, one argument each, agreed with the team lead) |
| 2026-10-10 | The Assembly Floor gets its final dressing from a re-runnable editor builder: the Toy-O-Matic assembler replaces the stamping presses on the same footprint; doorways 1 and 4 rise to 5 m with framed openings and white lintels; 1 m floor tiles; windows, pipes, signs, posters, a workbench, lockers, a pallet, a parts shelf, pendant lamps and an overhead rail of hanging toys | Keep the presses and add props by hand in the scene; model the room in a DCC tool; leave the doors at 4 m | The team found the room empty and the presses and yellow lintels unattractive. A builder in code keeps every position reviewable and clear of the gameplay objects, spawns, patrol corners and cutscene cameras, and can be re-run as the layout is tuned; meshes and signs are generated, so no new art pipeline. The assembler keeps the presses' footprint and height class, so the Tracker's cover and the grid barely change. Cost measured: no extra SetPass calls; see the Optimisation Log | S1 (1 m tiles requested by S2; S2 to make the door panels 5 m) |

## 8. Greybox character model contract (S3)

Status: implemented, audited and **accepted by S4 on 2026-09-30**. The hierarchy is now frozen under the freeze rule below. Paths are relative to each model's FBX root and were read from the imported assets. The FBXs are in `Assets/_Project/Models/<Model>/`, and the prefab variants are in `Assets/_Project/Prefabs/Characters/`.

### 8.1 Conventions

- Unity forward is +Z. Models are authored facing Blender +Y; the export below maps that to Unity +Z.
- Character left is Unity -X: `_L` parts are on -X and `_R` parts on +X.
- Every `<Model>_Root` sits at the origin, and the lowest geometry is at y = 0. Wheeled and tracked models are centred on their ground contact, legged models between their feet.
- Every node has local rotation 0 and scale 1, so each pivot rotates about the model's own X, Y and Z axes.
- Scale is 1 unit = 1 metre. Prefab variants carry one disabled primitive collider per mesh part and no Rigidbody.

### 8.2 Freeze rule

Once S4 accepts this handoff, contracted nodes and pivots must not be renamed, reparented, repositioned or have their transforms changed without coordinating with S4, because animation clips depend on these paths. Final modelling may add non-animated detail meshes beneath existing nodes, as long as the existing paths and pivot transforms stay unchanged.

**Acceptance check (S4, 2026-09-30).** Each `Blender/<Model>.blend` was loaded read-only in Blender 5.2 and compared with the four model trees below:

- **Names and parents:** every node and parent matches the trees below (TrackerToy 30 nodes, SaboteurBot 33, GuardBot 22, CaptainBot 33).
- **Pivot positions:** every pivot sits at the position listed, converted to Unity axes (Unity x, y, z = Blender x, z, y).
- **Transforms:** every node has rotation 0, scale 1, no delta transforms and an identity parent-inverse matrix, so nothing carries a hidden offset.
- **Ground:** the lowest geometry of each model is at y = 0.
- **Exports:** each `Assets/_Project/Models/<Model>/<Model>.fbx` contains exactly the same node names as its `.blend`.
- **Facing:** in a front view of all four, the faces point forward and `_L` parts are on the character's left.

### 8.3 TrackerToy

```text
TrackerToy_Root                          origin, midway between the axles
├─ Body_Pivot                            (0, 0.16, 0)    body bob and tilt
│  ├─ Torso, Belly
│  ├─ Head_Pivot                         (0, 0.76, 0.30) neck
│  │  ├─ Head, Muzzle, Antenna_Stem, Antenna_Bulb
│  │  ├─ Eye_L, Eye_R                    non-animated detail meshes
│  │  ├─ Ear_L_Pivot → Ear_L             (-0.22, 1.10, 0.44)
│  │  └─ Ear_R_Pivot → Ear_R             (0.22, 1.10, 0.44)
│  ├─ Tail_Pivot → Tail                  (0, 0.43, -0.48) tail/body joint
│  └─ WindupKey_Pivot → Key_Shaft, Key_Bar   (0, 0.76, -0.533) key spins about Z
├─ Axle_Front, Axle_Rear                 static; connect the wheels to the body
└─ Wheel_{L,R}_{Front,Rear}_Pivot → Wheel_*   (±0.40, 0.16, ±0.355) spin about X
```

Final model (2026-10-02): detail was added inside the existing meshes only: pupils in `Eye_L`/`Eye_R`, the nose in `Muzzle`, inner ear discs in `Ear_L`/`Ear_R` (second material slots) and round end loops in `Key_Bar`. No node was added, renamed, reparented or moved, so the tree above and the prefab-variant colliders are unchanged. 1,816 triangles after import; one shared UV layout.

### 8.4 SaboteurBot

```text
SaboteurBot_Root                         origin, wheel contact point
├─ Wheel_Pivot                           (0, 0.365, 0) spin about X
│  └─ Wheel, Wheel_Hub_L, Wheel_Hub_R
└─ Body_Pivot                            (0, 0.365, 0) on the axle, so a lean keeps the body on the wheel
   ├─ Body, Pink_Belt, Neck_Post, Goggle_Rim_L, Goggle_Rim_R
   ├─ Battery_Pivot → Battery            (0, 1.23, -0.482) Battery GameObject inactive in the prefab
   └─ Shoulder_{L,R}_Pivot               (∓0.53, 1.17, 0.02)
      ├─ ShoulderBall_*, UpperArm_*
      └─ Elbow_{L,R}_Pivot               (∓0.53, 1.17, 0.52)
         ├─ ElbowBall_*, Forearm_*
         ├─ Claw_{L,R}_Top_Pivot → Claw_*_Top          (∓0.53, 1.20, 0.95) hinge about X
         └─ Claw_{L,R}_Bottom_Pivot → Claw_*_Bottom    (∓0.53, 1.14, 0.95) hinge about X
```

The claws are 2.5 cm apart at rest and meet after about 4° of closing each.

Final model (2026-10-02): detail was added inside the existing meshes only: a rounder body with the goggle strap in `Body`, recessed lenses in `Goggle_Rim_L`/`Goggle_Rim_R`, wrist cuffs in `Forearm_L`/`Forearm_R`, axle caps on the octagonal `Wheel_Hub_L`/`Wheel_Hub_R` and a label band and terminals on `Battery` (second material slots), plus a rounded tyre in `Wheel` and hinge knuckles and tips on the four claws. No node was added, renamed, reparented or moved, and every part stays inside its greybox bounds, so the tree above, the pivots and the prefab-variant colliders are unchanged. 2,488 triangles after import; one shared UV layout. Colours and the A-D tints are B5.

### 8.5 GuardBot

```text
GuardBot_Root                            origin, centre of the tread footprint
├─ Hip
├─ Tread_L_Pivot → Tread_L               (-0.335, 0.175, 0) rigid tread assembly
├─ Tread_R_Pivot → Tread_R               (0.335, 0.175, 0)
└─ Torso_Pivot                           (0, 0.69, 0) waist
   ├─ Torso
   ├─ Head_Pivot                         (0, 1.47, 0) neck base
   │  └─ Neck, Head, Visor, Antenna_Stem, Antenna_Bulb
   ├─ ShieldArm_L_Pivot                  (-0.54, 1.32, 0) character left
   │  └─ ShoulderBall_L, Shield_Arm, Shield
   └─ CannonArm_R_Pivot                  (0.54, 1.32, 0) character right
      └─ ShoulderBall_R, Cannon_Arm, Cannon_Barrel
```

The treads are deliberately rigid; there are no road wheels.

Final model (2026-10-04): detail was added inside the existing meshes only, using the same shared parts as the other robots: a bevelled `Torso` with a recessed chest plate, back vent and shoulder sockets, a bevelled `Head` with a visor frame and ear plates, a recessed lens in `Visor`, grousers, hub caps and a trim block with a recessed top on each tread, banded `Neck`, `Cannon_Arm`, `Shield_Arm` and `Antenna_Stem`, an equator band on the shoulder balls and the antenna bulb, ringed `Cannon_Barrel` with a recessed muzzle, and a `Shield` with a recessed face and four rivets. No node was added, renamed, reparented or moved, and every part stays inside its greybox bounds (the barrel and neck are slightly narrower than before), so the tree above, the pivots, the tread footprint and the 15 prefab-variant colliders are unchanged. 2,812 triangles.

### 8.6 CaptainBot

```text
CaptainBot_Root                          origin, between the boots
├─ Leg_{L,R}_Pivot                       (∓0.285, 1.08, 0) hips
│  ├─ UpperLeg_*
│  └─ Knee_{L,R}_Pivot                   (∓0.285, 0.64, 0)
│     ├─ LowerLeg_*                      includes the knee hinge
│     └─ Ankle_{L,R}_Pivot → Boot_*      (∓0.285, 0.24, 0)
└─ Torso_Pivot                           (0, 1.04, 0) waist
   ├─ Torso, Epaulette_L, Epaulette_R
   ├─ Head_Pivot                         (0, 2.12, 0) neck base
   │  ├─ Neck, Head, Visor
   │  └─ Hat_Pivot → Hat_Band, Hat_Brim, Hat_Crown   (0, 2.82, 0) head top
   └─ CannonArm_{L,R}_Pivot              (∓0.72, 1.95, 0)
      └─ ShoulderBall_*, Arm_*, Cannon_*
```

The `UpperLeg_*` and `LowerLeg_*` names replace the earlier `Leg_L`/`Leg_R` meshes. S4 acknowledged these names on 2026-09-30. Positive local X rotation on a knee pivot bends the knee (boot moves backward).

Final model (2026-10-04): detail was added inside the existing meshes only, using the same shared parts as the other robots: `Torso` with a separate gold belt band, a star badge, two buttons and a recessed back panel, `Head` with a visor frame and ear plates, a recessed lens in `Visor`, a badge on `Hat_Band`, a recessed top on `Hat_Crown`, five fringe tassels on each `Epaulette_*`, banded arms and neck, ringed `Cannon_*` with a recessed muzzle, `Boot_*` as sole plus upper with toe and instep plates, side stripes on `UpperLeg_*`, and end caps on the knee hinge in `LowerLeg_*`. No node was added, renamed, reparented or moved, and every part stays inside its greybox bounds (the cannons and neck are slightly narrower than before), so the tree above, the pivots, the boot footprint and the 21 prefab-variant colliders are unchanged. 3,188 triangles. The chest buttons share the dark slot with the hat band, so they stay dark until the palette work gives them a colour.

### 8.7 Approximate greybox motion limits

These are geometric limits to keep in mind when authoring clips, not defects:

- Guard shield swung across the body (about Z) starts touching the torso beyond about 60°.
- Captain cannon arm raised forward (about X) starts meeting the epaulette beyond about 105°.

### 8.8 Blender → FBX → Unity export

Validated on all four models with Blender 5.2 and Unity 6000.6.2f1:

- **Blender export:** Forward Y, Up Z, Apply Transform off, Apply Scalings "FBX Units Scale", Add Leaf Bones off, Object Types Empty and Mesh, Bake Animation off.
- **FBX files:** overwritten in place so `.meta` GUIDs stay the same.
- **Unity importer:** Bake Axis Conversion stays enabled, with the other import settings unchanged.
- **Result:** every contracted node imports with identity rotation and scale 1, meshes are Y-up, and the models face +Z.

`CONTRIBUTING.md` uses the same settings (team agreed; see the §7 decision log).

### 8.9 ModelShowcase

- The models stay at neutral rotation, and the orthographic camera views them from the +Z side.
- Framing is intended for 16:9. The visual order, left to right, is TrackerToy, SaboteurBot, GuardBot, CaptainBot, Unit047, at x = 4.85, 2.65, 0.2, -2.6 and -5.0 so all five fit the orthographic frame (size 3.3).
- The keycard (`Keycard_Root` prefab, 0.30 x 0.19 x 0.02 m) hangs at x = -1.11, y = 1.0, in the gap between GuardBot and CaptainBot, so the whole line-up still fits the frame. Props are small, so they go in gaps like this one instead of in the line.
- The scene is excluded from the build settings.

### 8.10 Open handoff items

1. ~~S4 accepts the §8 hierarchy, explicitly including the Captain `UpperLeg_*`/`LowerLeg_*` paths, before recording clips.~~ Done: accepted by S4 on 2026-09-30 after the acceptance check above.
2. ~~S4 is told the Guard uses rigid tread assemblies rather than road-wheel articulation.~~ Done: acknowledged by S4. Tread motion will be a scrolling material, not animated pivots.
3. ~~S4 is told the §8.7 motion limits.~~ Done: acknowledged by S4. Clips stay within these limits.
4. ~~Someone opens ModelShowcase fresh in Unity at 16:9 for a clean lit check.~~ Done 2026-10-02: the scene was opened on its own and its camera rendered at 1600 x 900; all five models are inside the frame.
5. Before the PR: EditMode tests pass, the game plays from `Bootstrap` without console errors, and Git LFS tracks the `.blend` and `.fbx` files.
6. ~~S4 accepts the §8.11 Unit 047 hierarchy before recording any Unit 047 clip. Until then it may still change.~~ Done: accepted by S4 on 2026-10-02 after the Unit 047 acceptance check.
7. S2 confirms Unit 047's eye height (1.91 m, level with the Guard's visor) against the first-person camera height, so cutscene cuts to and from gameplay line up.

### 8.11 Unit047 (blockout, accepted)

Status: blockout delivered 2026-10-02 and **accepted by S4 on 2026-10-02**; the hierarchy is now frozen under the same freeze rule as the other characters. Cutscene-only hero model (Full Plan v5 §10.3, §11.3): S4 animates idle sway, head turn, key spin and eyes on/off. Budget < 2,500 triangles; the blockout is 760. Same conventions as §8.1, exported with §8.8.

```text
Unit047_Root                             origin, between the feet
├─ Leg_{L,R}_Pivot → Leg_*, Foot_*       (∓0.20, 0.77, 0) hips; legs stay planted while the body sways
└─ Body_Pivot                            (0, 0.77, 0) waist: idle sway and bob
   ├─ Pelvis, Torso
   ├─ Chest_Tag, Sticker                 non-animated decal carriers (047 tag, DEFECTIVE sticker)
   ├─ Head_Pivot                         (0, 1.571, 0) neck base: head turn
   │  └─ Neck, Head, Visor, Eyes, Antenna_Stem, Antenna_Bulb
   ├─ Arm_L_Pivot                        (-0.431, 1.478, 0) shoulder
   │  └─ ShoulderBall_L, Arm_L, Hand_L
   ├─ Arm_R_Pivot                        (0.431, 1.478, 0) shoulder
   │  ├─ ShoulderBall_R, Arm_R, Hand_R
   │  └─ Blaster_Pivot → Blaster_Body, Blaster_Barrel   (0.431, 0.878, 0) grip, in the right hand
   └─ WindupKey_Pivot → Key_Shaft, Key_Bar   (0, 1.309, -0.216) key spins about Z
```

- `Eyes` holds both eyes in one mesh with its own material (`Greybox_Eyes`), so one material property switches them on and off.
- `Chest_Tag` and `Sticker` are flat plates already in place, so the final decals from the prop atlas need no new nodes.
- Size 1.05 x 2.42 x 0.94 m (W x H x D). The hero is sized from the Guard's eye line: its eye centre is at 1.91 m and its visor spans 1.83-1.99 m, against the Guard's visor at 1.85-1.96 m (measured from `GuardBot.fbx`). That puts it eye to eye with the Guard, with its head top at 2.13 m (Guard 2.08) and antenna top at 2.42 m (Guard 2.44), while the Captain (3.23 m) stays the tallest and the Tracker and Saboteur stay smaller. The model is one uniform scale (1.54) of the reviewed blockout proportions, so no part changed shape. The prefab variant `Prefabs/Characters/Unit047.prefab` has 24 disabled primitive colliders (spheres on the shoulder balls and antenna bulb, boxes elsewhere) fitted to the meshes and no Rigidbody, like the other characters.
- Clearance: at rest the inner face of each arm is 3.9 cm from the torso side, so arms swing forward and back (about X) without touching it. Other motion limits have not been measured yet; S4 should report any clipping found while authoring clips.

**Acceptance check (S4, 2026-10-02).** `Blender/Unit047.blend` was loaded read-only in Blender 5.2 and compared with the tree above:

- **Names and parents:** all 33 nodes and their parents match the tree.
- **Pivot positions:** every pivot sits at the listed position, converted to Unity axes (Unity x, y, z = Blender x, z, y): legs (∓0.20, 0.77, 0), body (0, 0.77, 0), head (0, 1.571, 0), arms (∓0.431, 1.478, 0), blaster (0.431, 0.878, 0), wind-up key (0, 1.309, −0.216).
- **Transforms:** every node has rotation 0, scale 1, no delta transforms and an identity parent-inverse matrix.
- **Size and budget:** 1.05 × 2.42 × 0.94 m, lowest geometry at y = 0, 760 triangles.
- **Details the clips rely on:** the eye centre is at 1.91 m; `Eyes` is one mesh on its own `Greybox_Eyes` material; each arm's inner face is 3.9 cm from the torso.
- **Export:** `Assets/_Project/Models/Unit047/Unit047.fbx` contains exactly the same 33 node names.
- **Facing:** in a front view the visor and eyes face forward, and the blaster is in the character's right hand.

### 8.12 Keycard (task-prop kit, first prop)

Status: delivered 2026-10-04. The keycard Saboteur A carries and drops when destroyed (see `Docs/AI/SaboteurBot.md`), and the mesh of S2's floor pickup. It has no moving parts, so it does not wait for the rest of the prop kit. Same conventions as section 8.1, exported with section 8.8.

```text
Keycard_Root                             prefab root, origin = the centre of the card
└─ Keycard                               the mesh, three material slots
```

- Size 0.30 x 0.19 x 0.02 m (W x H x D, credit-card ratio), measured on the imported prefab at identity; the bounds are centred on the origin, so the pivot is the centre of the card.
- The card face (chip, header band and three lines of text) faces +Z. The back carries the magnetic stripe and a signature panel. S4 spins it about Y above Saboteur A (1.61 m tall).
- Slots are the greybox ones: `Greybox_Light` for the card, `Greybox_Dark` for the header, text lines and stripe, `Greybox_Mid` for the chip and signature panel. They stay a placeholder until the prop atlas (B8) and the palette work (B6).
- Rounded corners and a small chamfer on the card, details raised 2 mm. 320 triangles after import (budget under 600). One UV layout, laid out so it can be repacked onto the shared prop atlas.
- The FBX holds just the `Keycard` mesh (Unity wraps it in a root named after the file, `Keycard`). Unity names a prefab's root after the prefab file, so the prefab is `Prefabs/Props/Keycard_Root.prefab`: a prefab variant of the FBX whose root is `Keycard_Root` with the `Keycard` mesh as its only child, so no parent and child share a name. It has no collider and no Rigidbody. S2 adds the pickup trigger once S2 answers the collider question S4 asked. S4 parents the prefab to a socket on Saboteur A and spins it about Y in code; no attachment point is needed.
- Rig is None, because the keycard plays no clip (S4 spins it in code) and a rig would only add an unused Avatar and Animator. The rest of the prop kit uses None too; only the characters need Generic. Every other importer setting is the same as the character models (the `.meta` differs from `GuardBot.fbx.meta` only in its GUID and the Rig line).
