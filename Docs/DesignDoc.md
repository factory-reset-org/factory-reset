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
| `LookTarget` | Optional point to face |
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
- **`AgentController.Update()`**: builds the context (with the real grid cell under the agent), calls `Tick`, applies the path semantics above, and stores `DebugState`. With no brain it logs one warning and does nothing, instead of throwing every frame.
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
- Exposes `CurrentSpeed` (m/s) and `TurnRate` (°/s, positive = turning right) for the animation Blend Tree.
- Reuses one waypoint list, so a new route allocates no memory.

**Why not `NavMeshAgent`:** the brains plan their own paths on the grid (GBFS, tactical A*, intercepts). A `NavMeshAgent` would replan on its own and fight those decisions, and the Captain's timing maths needs the agent to walk exactly the route it was given.

**Not built yet:** blending into a new path on replan, and the path request scheduler.

**Tests:** `MockPathProviderTests` (8 EditMode tests), and two test scenes:
- `Scenes/Test/Test_PathFollower`: step, ramp and drop.
- `Scenes/Test/Test_AgentSpawner`: all four types patrolling, with the Saboteur squad A-D and a real level grid. `RuntimeNavMeshBake` bakes the floor's NavMesh in `Awake`, so no bake output is committed (bake commits are S1's), then "Spawn On Start" builds the grid. Checked in Play mode: a 60 x 60 grid with 3,364 walkable cells (the blocked 236 are the one-cell border the NavMesh leaves at the floor's edge), all seven agents receive it, and the console stays empty.

### 2.6 What other systems read: `IAgentState` (implemented)

A read-only view of an agent's body, in `Scripts/Interfaces/`. `AgentController` implements it.

| Property | Meaning | Source |
| --- | --- | --- |
| `Type` | Tracker, Guard, Saboteur or Captain (`AgentType`, also in `Interfaces`) | `Identity.Type` |
| `Identity` | `AgentIdentity`: type, unique `Id` and `SquadIndex` (`SquadLetter` gives A-D). Fixed for the agent's life | The spawner, through `Initialise` |
| `Speed` | Ground speed in m/s | `AgentPathFollower.CurrentSpeed` |
| `TurnRate` | Degrees per second, positive = turning right | `AgentPathFollower.TurnRate` |
| `IsAttacking` | True while the brain's action is `Shoot` | The latest `AgentIntent` |
| `IsDead` | True once the agent is scrapped; never becomes false again | Set by `AgentController.Scrap` |

**Why it lives in `Interfaces`:** that assembly references nothing, so animation, the HUD, scoring and the journey can read an agent without being able to see its brain. Readers get it once with `GetComponent<IAgentState>()` when they set up, never in `Update`.

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

**Not done yet:** the Guard's tread scroll and Unit 047's cutscene clips. The aim pose is in 2.9, falling apart in 2.10.

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

**Why the telegraph is in the body, not the brain:** hitscan cannot be dodged once fired, so the 0.3 s aim is the player's chance to step behind cover. Putting it in one place makes it identical for every agent; the brains keep only their own decision (the Guard's peek timing, the Captain's 1.2 s interval).

**No friendly fire:** the Agents layer is not in the shot's hit mask, so another agent in the line of fire neither blocks nor takes the shot. Because agents are hit through their capsules (not S3's per-part colliders, which stay disabled), S2's cover check, which skips capsules, is unaffected.

**Cancelled:** a knock-out or scrap during the aim cancels the shot. A cutscene or the pause menu holds it where it is (game time).

**Not done yet:** the Saboteur's door, trap and battery actions (`CloseDoor`, `ArmTrap`, `StealBattery`). Its brain does not output them yet; S2's `Door` already implements `ISabotageable`, so the controller will call `Execute()` on the target once it does. Player health is S2's (`TakeDamage` is still a no-op), so hits are wired but do not hurt yet.

### 2.10 Falling apart and the "?"/"!" icons (S4, implemented)

**Falling apart:** `AgentFallApart` on every body. S3's models are rigid parts under pivots, each with a disabled collider, so falling apart needs nothing spawned:

| When | What happens |
| --- | --- |
| Knocked out | Every mesh part leaves the body as a physics body on the **Debris** layer, with a small outward burst; the Animator stops |
| Last 1 s of the knock-out | The parts lose their physics and fly back to their pose under their pivots on a smoothstep, all arriving together |
| Reboot | Parts re-attached exactly as they were (position, rotation, scale, layer, collider off), Animator back on |
| Scrapped (Saboteurs) | Falls apart the same way, lies there for 2 s, shrinks away over 1 s, and the agent is switched off (not destroyed, so the spawner's list stays valid) |

The Debris layer collides with the floor, walls and other debris but not with the player or the agents, so a heap of parts never blocks anyone. `AgentController.KnockOutTimeLeft` tells the component when to start reassembling.

**Alert icons:** `AlertIcon` on every body shows a yellow **"?"** (suspicious) or a red **"!"** (has the player) above the agent's head, facing the camera, with a short pop when the level rises. It hides while the agent is down, scrapped or frozen. The level comes from `AgentController.Alert`:

- **From the brain:** `AgentIntent.Alert` (`AlertLevel.None`, `Suspicious`, `Alert`). The Captain sets it: "!" in Intercept, Ambush and Engage, "?" in Observe and Reassess.
- **Fallback:** a brain that leaves it at `None` gets a level worked out from its state name by `AlertFromState` (for example Tracker Chase, Guard PeekAndShoot → "!"; Tracker Investigate and Search → "?"). So the Tracker, Guard and Saboteurs show icons before their owners add the one line.

**Why an explicit level instead of only state names:** a renamed state would silently lose its icon; a brain that sets `Alert` keeps it whatever its states are called. The fallback is there so nothing waits on that change.

### 2.11 Debug overlay (S4, implemented)

Press **F3** in play to see what every agent is doing and thinking, drawn over the level (`AgentDebugOverlay`, in `Agents.unity`; `Test_AgentSpawner` shows it from the start). It works only in the Editor and development builds, so it can never appear in the submitted build, and it costs nothing while hidden.

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
- **No allocation:** per-cell arrays are allocated once per grid size, and each compute bumps a stamp instead of clearing them, so recomputing allocates nothing.
- **Evidence hooks:** `NodesExpanded` and `ElapsedMs` for `AIPerformanceLog.md`.
- **Used by the Captain:** one field per candidate goal, plus one rooted at the Captain for its own arrival times. See `Docs/AI/CaptainBot.md`.

**Tests:** `DijkstraFieldTests` (20 EditMode tests), including:
- field costs equal A* path costs on random grids;
- blocked cells and corner-cutting are respected;
- the bound stops the search;
- a recompute allocates 0 bytes.

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
| `UI` | S3 | HUD, subtitles, chapter card, results screen, leaderboard | Planned |
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

**Subtitles:** the runner raises `DialogueEvents.OnLineShown(DialogueLineView)` (speaker tag, tag colour, whole line, characters typed so far) whenever more is typed, and `OnLineCleared()` at the end or on skip. S3's UI draws the subtitle bar from these; until it exists, `PlaceholderSubtitles` on the director draws a plain bar from the same events, and is switched off when the UI lands.

**Voice blips:** built in code, no audio files. Each speaker has a short tone in their waveform and pitch from the cast table (Factory OS low square, Pip high triangle, Captain sawtooth, Unit 047 sine), played on every second typed letter and slightly detuned each time.

#### Cutscene camera, bindings and letterbox (S4, implemented)

**Camera hand-over** (`CutsceneCameraRig`, owned by the director). The gameplay camera sits on S2's player prefab and is driven by mouse look, so it carries no Cinemachine brain. When a cutscene with a Timeline starts, the rig:

1. records the camera's local position, rotation and lens;
2. adds a `CinemachineBrain` to it at runtime (or re-enables the one it added before), so the player prefab is never edited;
3. parks a "gameplay view" Cinemachine camera exactly at the player's eyes, at priority 100.

The Timeline's shot cameras override it while their clips play. A shot clip's ease-in blends *from* the gameplay view and the last clip's ease-out blends back *to* it, so entering and leaving a cutscene is a blend, not a cut. When the cutscene ends (watched or skipped) the brain is switched off and the camera's local pose and lens are put back, so mouse look carries on exactly where it stopped. Skipping cuts straight back. Between cutscenes no brain and no Cinemachine camera run at all.

**Binding across scenes** (`CutsceneBindings`). Timelines live in the Agents scene and cannot keep a reference to an object in Env or Interactables. A track that drives such an object is left unbound and named after the object's `CutsceneBindingId` (a track named `AlarmDoor3` drives the object with that id). When the Timeline starts, every unbound track is looked up by its name once and bound to the right thing for its type: the GameObject for an activation track, the `Animator` (on the object or a child) for an animation track, the component otherwise. Cinemachine tracks are bound to the brain on the gameplay camera. Tracks already bound in the Agents scene (Unit 047, shot cameras) are left alone. A name with no matching object is logged once per cutscene and that track does nothing; the cutscene still plays and its Critical signals still fire. Only enabled objects register their id, so something a cutscene switches on must be bound through an active parent.

**Letterbox** (`Letterbox`, on the director). Two black bars, each 11% of the screen height, close in over 0.6 s (smoothstep) on `CutsceneEvents.OnCutsceneStarted` and open on `OnCutsceneEnded`, so a skip opens them too. It builds its own screen-space canvas at sorting order 40, with no assets. The subtitle view and the chapter card must sort above 40. The canvas is switched off once the bars are fully open, so gameplay pays nothing for it.

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
| `Shutdown` | `FactoryShutdown` (`ending`) | The sun, the Storage lamp and the alarms fade over 3 s to a warm glow at 35% intensity | Screens, press strips and beacons dim to 15% |

The beacons change by swapping shared materials, which keeps the SRP Batcher. Only the shutdown fade makes per-renderer material copies, once, at the end of the game.

**Cutscene binding ids in `Env`** (for S4's Timelines, through `CutsceneBindingId.TryFind`):

| Id | Object | Holds |
| --- | --- | --- |
| `LightingRig` | `Lighting` | `LightingState`, every light, the post-processing Volume |
| `Sun` | `Lighting/Sun` | Directional light (Mixed) |
| `StorageLamp` | `Lighting/Accents_Realtime/Accent_Storage_Lamp` | Orange real-time point light |
| `AlarmDoor3` | `Lighting/ControlAlarms/AlarmDoor3` | Alarm light and two lintel beacons, Storage–Control door |
| `AlarmDoor4` | `Lighting/ControlAlarms/AlarmDoor4` | Alarm light and two lintel beacons, Control–Assembly door |
| `ControlScreens` | `Level/Dressing/ControlScreens` | The three Control Room screens |
| `ObjectiveBeacon` | `ObjectiveBeacon` | The beacon (it already hides itself during cutscenes) |

The alarm colours belong to `LightingState`. A Timeline can frame or activate these objects, but a light that both a Timeline and `LightingState` animate would fight, so leave the alarm colour to the signals.

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
| 2026-10-01 | Propose, for S2's review, a Saboteur-owned `ICostModel` that returns infinity for a door's cells to cost a hypothetical closure, treating an infinite total as a lockout; the live grid is never mutated | Mutate and restore the live grid; clone the grid per door | `ICostModel` only requires at least the base cost, so infinity is allowed, and nothing shared changes. Status: proposed, awaiting S2 (including how `AStarSearch` handles an infinite step) | S3 (proposed) |
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
