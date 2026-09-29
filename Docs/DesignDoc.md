# Design Document

Architecture, decisions and justifications. Every decision needs a "because" you can say out loud in the viva.

## 1. Architecture overview

The AI is separated from everything Unity-specific, so it can be tested without a scene and shown to be decoupled from the visuals and the story.

```text
Journey + UI       chapters, cutscenes, HUD, scoring      events and blackboard only (planned)
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
| `Cell` | Agent's grid cell | Always `(0, 0)` until the controller is connected to the grid |
| `Position`, `Forward` | Agent's world position and facing | The agent's transform |
| `Time` | Seconds since the game started | `Time.time` |
| `World` | Shared `WorldBlackboard` (read-only for brains) | One instance created by `AgentSpawner` |
| `Senses` | What this agent can see and hear | Empty `SensorSnapshot` for now |

### 2.2 What the brain returns: `AgentIntent`

| Field | Meaning |
| --- | --- |
| `Path` | World positions to walk through. **`null` = keep following the current path, empty list = stop, non-empty = replace the route.** |
| `DesiredSpeed` | Walking speed in m/s |
| `LookTarget` | Optional point to face |
| `Action` + `ActionTargetId` | `None`, `Shoot`, `CloseDoor`, `ArmTrap`, `StealBattery` or `Rewind`, plus the id of the door, trap or battery |
| `DebugState` | State name for the debug overlay and the "!"/"?" icons |

**Why `null` and an empty list mean different things:** most ticks a brain has no new route, so returning `null` costs nothing and lets the body keep walking. Stopping on purpose is a separate, explicit answer.

### 2.3 The brain interface: `IAgentBrain`

| Method | When it is called |
| --- | --- |
| `AgentIntent Tick(in AgentContext ctx)` | Every frame, by `AgentController` |
| `OnGraphChanged(IReadOnlyList<Vector2Int> changedCells)` | When a door or box changes the grid. Brains replan only if a changed cell affects them |
| `OnStunned(float duration)` | When the agent is stunned. Declared, not yet called by the runtime |
| `OnDestroyed()` | Once, when the agent leaves the game for good (a Saboteur is scrapped, or the scene unloads). The brain releases anything it holds on the blackboard, such as target claims or cover reservations. `Tick` is never called afterwards. Called from `AgentController.OnDestroy` |

### 2.4 Runtime wiring (implemented)

- **`SpawnPoint`** (in `Agents.unity`): marks where an agent's feet go, which way it faces, its `AgentType` and optional patrol waypoints. Each type has its own gizmo colour: Tracker yellow, Guard blue, Saboteur green, Captain red.
- **`AgentSpawner.SpawnAll()`**:
  - Instantiates one body per child spawn point, raised by the CharacterController's feet-to-pivot height so it stands on the floor.
  - Parents each body under the spawner, so agents stay in the Agents scene when scenes load additively.
  - Builds the brain for that type in `CreateBrain` and passes it with the shared blackboard to `AgentController.Initialise`.
  - Can only run once. The scene loader calls it after the level exists; test scenes can tick "Spawn On Start".
- **`CreateBrain`**: every type currently gets `MockPathProvider`, a fake brain that loops a patrol route. Each owner replaces only their own case when their brain is ready.
- **`AgentController.Update()`**: builds the context, calls `Tick`, applies the path semantics above, and stores `DebugState`. With no brain it logs one warning and does nothing, instead of throwing every frame.

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

**Not built yet:** path smoothing (string pulling, then Catmull-Rom), blending into a new path on replan, the path request scheduler, grid cells in the context, and calls to `OnStunned`.

**Tests:** `MockPathProviderTests` (7 EditMode tests), and the `Scenes/Test/Test_PathFollower` scene (step, ramp and drop) and `Scenes/Test/Test_AgentSpawner` scene (all four types patrolling).

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
- Timelines live in `Agents` but animate objects in other scenes (doors, lamps, cores). They find those objects at runtime by a stable `CutsceneBindingId` on the target, never by a serialised cross-scene reference.
- Test scenes live under `Scenes/Test/` and are not in the build.

## 6. Event flow

Systems talk through events and the blackboard, not direct references. The journey and UI layers never reference a brain.

| Event | Raised by | Consumed by | Status |
| --- | --- | --- | --- |
| Grid changed (`GridGraph.Version`, `IAgentBrain.OnGraphChanged`) | Grid, when a door opens or closes or a box settles | Every brain replans only if its path is affected | Grid and brain hook exist |
| Noise (position, loudness, source) | Gunshots, thrown toys, doors, boxes, task props | Noise propagation, then the Tracker | Planned |
| Player state | Player | Blackboard, then Guard, Captain and Saboteurs | Planned |
| Task progress and completion | Task props | Chapter manager, HUD, scoring | Planned |
| Objective changed | Chapter manager | Blackboard `ObjectiveTargets`, then Captain, Saboteurs, beacon and HUD | Planned |
| Switch restored | Chapter manager | Cutscene director, which plays the next cutscene after 1.3 s | Planned |
| Critical cutscene signal | Cutscene Timeline | Captain wake, Control Room door unlock, core shields drop. Also fired when a cutscene is skipped | Planned |
| Agent disabled, destroyed, rebooted | `AgentController` | Scoring, Saboteur squad, HUD | Planned |
| Game state changed (Title, Playing, Cutscene, Paused, Results) | Game manager | `AgentController` (stops ticking brains), player input, timers, HUD | Planned |

## 7. Decision log

| Date | Decision | Alternatives considered | Because | Owner |
| --- | --- | --- | --- | --- |
| 2026-09-24 | Agents move with a `CharacterController` driven by `AgentPathFollower` (manual gravity, turn cap 360°/s), not `NavMeshAgent` | `NavMeshAgent`; Rigidbody physics | The brains plan their own grid paths; a `NavMeshAgent` replans by itself and would fight them, and a Rigidbody would be pushed around by boxes and other agents | S4 |
| 2026-09-24 | Brains get their dependencies once, through `AgentController.Initialise` at spawn | Look up the player, grid and blackboard with `FindObjectOfType` / `GetComponent` | No per-frame searches or hidden null references, and brains can be tested without a scene | S4 |
| 2026-09-24 | `AgentIntent.Path`: `null` keeps the current route, an empty list stops, a non-empty list replaces the route | A separate "stop" flag; resend the full path every tick | Most ticks have no new route, so `null` costs nothing; stopping is an explicit answer | S4 |
| 2026-09-27 | `DijkstraField` uses the same step costs as A* and reuses its arrays (stamp trick) | Recompute with fresh arrays; a separate distance metric | Field costs match A* path costs exactly, so the Captain's arrival times agree with the path it walks; recomputes allocate nothing | S4 |
| 2026-09-29 | Extend the game into a four-chapter journey: each control switch is sealed until its chapter's tasks are done, a fixed area order (Assembly Floor, Painting Room, Storage Area, Control Room), task props, six cutscenes, a Saboteur squad of four that is destroyed permanently, scoring with a leaderboard, and a `UI` scene. Ownership: chapters S1, task props S2, models, UI and scoring S3, cutscenes and agent execution S4. The Unity version (6000.6.2f1), the `develop` branch, the +Z-forward pipeline in §8.8 and the no-Issues workflow are unchanged | Keep three switches restorable in any order with one Saboteur | A story gives the agents a reason to matter: the Captain predicts which task the player is heading to, and the Saboteur squad coordinates on shared targets. Each new piece has exactly one owner, so every individual Git history stays clean | Team |
| 2026-09-29 | Character models face Unity +Z; authored facing Blender +Y; character left is -X; every contracted pivot has rotation 0 and scale 1 | Keep the first greybox's -Z facing; rotate prefab instances 180° | S4's movement turns agents with `LookRotation(velocity)`, which assumes +Z forward, and identity pivots give clean local rotation axes for clips | S3 |
| 2026-09-29 | Export FBX with Forward Y, Up Z, Apply Transform off, Apply Scalings "FBX Units Scale", and rely on Unity's Bake Axis Conversion | The previous CONTRIBUTING settings (Forward -Z / Up Y / Apply Transform); the same without Apply Transform | With Blender 5.2, the previous settings put +90° X rotations and 0.01 scales on nested nodes, and without Apply Transform every root imported at +90° X. The validated settings give identity transforms and Y-up meshes on all four models. The team agreed, and `CONTRIBUTING.md` now uses these settings | S3 (team agreed) |
| 2026-09-29 | Freeze the greybox hierarchy in §8 once S4 accepts it; later detail meshes may only be added beneath frozen nodes | Allow renames during final modelling | Animation clips bind to hierarchy paths | S3 (pending S4 acceptance) |
| 2026-09-29 | Guard treads stay rigid assemblies under their pivots | Road wheels with individual pivots | The reference draws the treads as boxes and lists no tread animation; a scrolling tread material can suggest rolling later | S3 (S4 to be informed) |

## 8. Greybox character model contract (S3)

Status: implemented and audited; **S4 acceptance pending**. Paths are relative to each model's FBX root and were read from the imported assets. The FBXs are in `Assets/_Project/Models/<Model>/`, and the prefab variants are in `Assets/_Project/Prefabs/Characters/`.

### 8.1 Conventions

- Unity forward is +Z. Models are authored facing Blender +Y; the export below maps that to Unity +Z.
- Character left is Unity -X: `_L` parts are on -X and `_R` parts on +X.
- Every `<Model>_Root` sits at the origin, and the lowest geometry is at y = 0. Wheeled and tracked models are centred on their ground contact, legged models between their feet.
- Every node has local rotation 0 and scale 1, so each pivot rotates about the model's own X, Y and Z axes.
- Scale is 1 unit = 1 metre. Prefab variants carry one disabled primitive collider per mesh part and no Rigidbody.

### 8.2 Freeze rule

Once S4 accepts this handoff, contracted nodes and pivots must not be renamed, reparented, repositioned or have their transforms changed without coordinating with S4, because animation clips depend on these paths. Final modelling may add non-animated detail meshes beneath existing nodes, as long as the existing paths and pivot transforms stay unchanged.

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

The `UpperLeg_*` and `LowerLeg_*` names replace the earlier `Leg_L`/`Leg_R` meshes. **S4 must acknowledge these names before recording clips.** Positive local X rotation on a knee pivot bends the knee (boot moves backward).

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
- Framing is intended for 16:9. The visual order, left to right, is TrackerToy, SaboteurBot, GuardBot, CaptainBot.
- The scene is excluded from the build settings.

### 8.10 Open handoff items

1. S4 accepts the §8 hierarchy, explicitly including the Captain `UpperLeg_*`/`LowerLeg_*` paths, before recording clips.
2. S4 is told the Guard uses rigid tread assemblies rather than road-wheel articulation.
3. S4 is told the §8.7 motion limits.
4. Someone opens ModelShowcase fresh in Unity at 16:9 for a clean lit check. The automated render picked up stale renderer objects from another open scene.
5. Before the PR: EditMode tests pass, the game plays from `Bootstrap` without console errors, and Git LFS tracks the `.blend` and `.fbx` files.
