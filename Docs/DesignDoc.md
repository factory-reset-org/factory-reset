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
| `World` | Shared `WorldBlackboard` (read-only for brains): the player (`Player`), the active `ObjectiveTargets` and their `ObjectivesVersion`, and the current `ChapterIndex` | One instance created by `AgentSpawner`, filled by its writers: `PlayerStateWriter` (every frame), `ObjectiveTargetWriter`, `ChapterIndexWriter` |
| `Senses` | What this agent can see and hear. Hearing: the loudest noise that reached it since its last tick (source position, level at its cell, source id, game time); each noise is delivered once | `AgentHearing` (owned by `AgentSpawner`) fills it through `AgentController.Hear`; vision is still empty |

### 2.2 What the brain returns: `AgentIntent`

| Field | Meaning |
| --- | --- |
| `Path` | World positions to walk through. **`null` = keep following the current path, empty list = stop, non-empty = replace the route.** |
| `DesiredSpeed` | Walking speed in m/s |
| `LookTarget` | Optional point to face |
| `Action` + `ActionTargetId` | `None`, `Shoot`, `CloseDoor`, `ArmTrap`, `StealBattery` or `Rewind`, plus the id of the door, trap or battery |
| `DebugState` | State name for the debug overlay and the "!"/"?" icons |

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
<!-- Cell size, connectivity, GridChange events, Version -->

## 5. Scenes and loading order

| Scene | Owner | Contents | Status |
| --- | --- | --- | --- |
| `Bootstrap` | S2 | Scene loader and game manager. Build index 0 | Empty scene (light and camera); scene loader and game manager not written yet |
| `Env` | S1 | Static geometry, lighting, NavMesh, grid, chapter manager | Empty scene (light and camera) |
| `Interactables` | S2 | Doors, boxes, belts, switches, task props, pickups. All non-static | Empty scene (light and camera) |
| `Agents` | S4 | Agent spawner and spawn points, debug overlays, cutscene director, Timelines, cutscene cameras | Spawner in place; the rest planned |
| `UI` | S3 | HUD, subtitles, chapter card, results screen, leaderboard | Planned |
| `ModelShowcase` | S3 | Model turntable. Not in the build | In use |

- Playing starts in `Bootstrap`, which loads the other scenes additively. `Env` is the active scene, so lighting is baked with only `Env` loaded and only `Env` holds static geometry.
- Agents are parented under the spawner, so they stay in the `Agents` scene when scenes load.
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
| Switch unsealed, switch restored, chapter started (`ChapterEvents`) | Chapter manager | Switch cage (opens on unseal), cutscene director (next cutscene 1.3 s after a restore), blackboard writer (`ChapterIndex`), HUD, scoring | Raised by the chapter manager; `ChapterIndex` writer implemented, other listeners planned |
| Cutscene started, ended (`CutsceneEvents`) | Cutscene director | Game manager (Cutscene state), HUD | Events implemented; the director is planned |
| Critical cutscene signal (`CutsceneEvents.OnCriticalSignal`) | Cutscene Timeline | Captain wake, Control Room door unlock, core shields drop. Also fired when a cutscene is skipped | Events and signal ids implemented; the director is planned |
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

**`CutsceneSignals`**: constants for the signal ids (`CaptainWake`, `ControlRoomUnlock`, `CoreShieldsDown`). Listeners compare against these, so a misspelt id fails to compile instead of silently never matching.

**Why a skip fires the missed signals:** if the player skips the Chapter 3 cutscene before the wake marker, the Captain would otherwise stay Dormant and the Control Room doors would stay locked. Firing every Critical signal not yet reached leaves the game in the same state as watching the whole cutscene.

**Why the static data is cleared on play:** domain reload is off in this project, so the binding registry and the event listeners would otherwise keep entries from the last play session. Both are cleared with `[RuntimeInitializeOnLoadMethod(SubsystemRegistration)]`, as in `AgentEvents`.

### 6.2 Chapter contracts (S1, implemented)

The journey's rules live in the plain C# `ChapterFlow` (Journey assembly); `ChapterManager` in `Env.unity` only connects it to the scene. S2's scene loader calls `ChapterManager.Begin()` after `BuildGrid` and `SpawnAll`; nothing starts on its own.

- **Each chapter is a state machine** on the shared FSM framework: Locked → Active → TasksDone → SwitchRestored → Transition → Done. Tasks of the active chapter count in any order; pickups (fuses, keycard) count in any chapter; other later-chapter tasks are ignored until their chapter starts. TasksDone raises `OnSwitchUnsealed(n)`; the switch prop's completion (`ITask.Id` = `"switch.n"`) is ignored until then. The next chapter starts only when the cutscene after the switch ends (`CutsceneEvents.OnCutsceneEnded`, raised on skip too). Chapter 4 has no switch; its console task (`"console"`) is the ending.
- **Props** implement `ITask`; the manager finds them once in `Begin()`. A prop created during play (the keycard) announces itself with `TaskEvents.RaiseTaskSpawned`. Anchors are found through `ITaskAnchor` (implemented by `TaskAnchor`), because Journey cannot reference Runtime.
- **Objectives** (`ObjectiveEvents.RaiseTargetsChanged`, after every change): each incomplete task of the active chapter at 100 + objectiveId, each switch not yet restored (sealed or not) at 200 + n, and the console at 300 until it is used. Never batteries. A target on a moving prop is re-raised with the same id.
- **Relays:** the order is random per run, chosen in `Begin()` and announced with `ChapterEvents.OnSequenceChosen("ch3.relays", order)` for the relay prop and the HUD; the objective follows the next relay as the prop reports progress.
- **Data:** `Assets/_Project/Data/Chapters/` (four `ChapterDefinition`s, thirteen `TaskDefinition`s). The task ids there are the ids the props must use.

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
