# Factory Reset

A cartoon first-person shooter in glossy toy plastic, set in a toy factory at night. Factory OS has switched on every unfinished toy and stamped the player, Unit 047, **DEFECTIVE**. Guided over the radio by Pip, 047 fights through four rooms against four kinds of autonomous enemy, restores the three control switches and shuts the factory down.

Built in Unity 6 (URP) by a team of four for **SE3032 Graphics & Visualization** and **SE3062 Intelligent Systems**. Each member owns one part of the graphics and one enemy agent with its own AI architecture.

<p>
  <img src="Docs/Evidence/Gallery/room_assembly.png" width="49%" alt="Assembly Floor">
  <img src="Docs/Evidence/Gallery/room_painting.png" width="49%" alt="Painting Room">
  <img src="Docs/Evidence/Gallery/room_storage.png" width="49%" alt="Storage Area">
  <img src="Docs/Evidence/Gallery/room_control.png" width="49%" alt="Control Room">
</p>

## Team

| | Member | Commits as | Graphics & Visualization | Intelligent Systems |
| --- | --- | --- | --- | --- |
| S1 | Onel De Silva | `OnelDJ` | World Builder | Tracker Toy: Greedy Best-First Search + hierarchical FSM |
| S2 | Dhananjaya Kularatne | `dhananjaya-kularatne` | Systems Engineer | Guard Bot: tactical A* + cover evaluation |
| S3 | Gayan Rajasinghe | `CYBER-G03` | Core Developer | Saboteur Squad: utility AI, four instances |
| S4 | Omira Kularatne | `IT24103437` | Agent Controller | Captain Bot: goal prediction + Dijkstra fields + A* intercept |

## Play it

You need Unity **6000.6.2f1** and Git LFS (`git lfs install` before cloning).

1. Open the project in Unity Hub.
2. Open `Assets/_Project/Scenes/Bootstrap.unity` and press **Play**. It loads the other scenes and waits on the title screen.
3. Press **Start**.

**F3** shows the AI debug overlay (each agent's route, state and reasoning). **F6 / F7 / F8** jump to Chapter 2 / 3 / 4 in the Editor.

## The game

| # | Room | Tasks | Enemies |
| --- | --- | --- | --- |
| 1 | Assembly Floor | Pull the conveyor lever, ride a crate onto the pressure plate, collect three fuses | Tracker Toy, a Saboteur |
| 2 | Painting Room | Hit four spinning targets in 12 s, hack the terminal | Guard Bot, a Saboteur |
| 3 | Storage Area | Press the three relays in the board's order, take the keycard off Saboteur A | Two Saboteurs; the Captain wakes |
| 4 | Control Room | Destroy the three power cores, hold the console to shut the factory down | The Captain and anything still standing |

Each room's control switch is sealed until its tasks are done; restoring it plays a cutscene and opens the next chapter. Each chapter start is a checkpoint, with three restarts per game. The score counts switches, tasks, takedowns and a win bonus, graded D to S. The story and the five cutscenes are in [Docs/Story.md](Docs/Story.md).

## Controls

| Input | Action |
| --- | --- |
| W A S D, mouse | Move, look |
| Left Shift | Sprint (agents hear it) |
| Left mouse button | Shoot |
| R | Reload |
| E | Use; hold on the terminal and the console |
| Q or right mouse button | Throw a wind-up toy (lures the Tracker) |
| Tab / H / M | Fold the task list / hide key hints / mute |
| Esc | Pause (restart from checkpoint, restart game, volume); skip a cutscene |

## Who built what

### S1 · Onel De Silva: World Builder and the Tracker Toy

**Graphics.** The level: four 20 x 20 m rooms in a ring, each with its own colour and dressing (presses and belts, paint tanks and targets, a shelf maze, server racks), built by editor scripts from shared bevelled meshes. The lighting: baked global illumination with three real-time lights, light probes along the routes, occlusion culling, and lighting states that change with the story; plus the objective beacon and the chapter flow. Baking the room lights cut the Storage view from 50 to 30 SetPass calls and from 253 to 82 shadow casters.

**AI.** The Tracker hunts by ear. Sound spreads through the grid as a Dijkstra propagation, round corners and through closed doors, and the Tracker heads for what it heard with Greedy Best-First Search inside an eight-state hierarchical FSM. A wind-up spring drains while it chases; when it runs out, the Tracker stops to rewind and is easy to hit. Against A* on 20 level routes, GBFS expanded 77% fewer nodes and ran 4.4x faster for paths 16.3% longer.

Read more: [TrackerToy.md](Docs/AI/TrackerToy.md), [LevelLayout.md](Docs/LevelLayout.md), [ArtBible.md](Docs/ArtBible.md).

### S2 · Dhananjaya Kularatne: Systems Engineer and the Guard Bot

**Graphics.** How the game plays and feels: the first-person player and blaster, every task prop and its physics (pushable crates, conveyor belts that carry them, the latching pressure plate, doors, switches, relays, cores), the physics layers, effects and procedural sound, scene loading and the Windows build. Pooling the shot bolts and impact bursts turned 600 copies made during play (113.5 ms) into 20 made up front (3.5 ms).

**AI.** The Guard holds cover. It scores cover cells with physics line-of-sight checks at knee and chest height, reaches them by tactical A* where a step the player can see costs four times as much, treats a crate the player pushes as new cover, and fights closer as the player's battery runs low. Its A* matched Dijkstra's cost on 20 level routes while expanding 19% to 42% of the cells, and once it held fire on a blocked shot line and moved on from cover that failed it, it landed 17 of 17 shots (11 of 26 before).

Read more: [GuardBot.md](Docs/AI/GuardBot.md), [LayerMatrix.md](Docs/LayerMatrix.md), [PlaythroughLog.md](Docs/PlaythroughLog.md).

### S3 · Gayan Rajasinghe: Core Developer and the Saboteur Squad

**Graphics.** Every character (Tracker, Saboteur, Guard, Captain, Unit 047) and the keycard, modelled in Blender within their triangle budgets, and nineteen task props redesigned as palette plastic under 800 triangles each; shared plastic materials (20, where one per slot would need 199), one BC7 texture preset, an LOD on the charger, and the `ModelShowcase` scene with wireframes. Also the HUD, title, results and credits screens, scoring and the leaderboard; splitting the HUD canvas cut its canvas work from 270 to 158 µs a frame.

**AI.** Four Saboteurs run one utility-AI brain. Every action (close a door, arm a trap, steal a battery, attack, flee, patrol) is scored with response curves and gated by logical rules, so a Saboteur closes the door that lengthens the player's route most. A squad layer of target claims, attack saturation and one shared detour cache keeps the four from piling onto one target: four of them cost the same 120 detour searches in 20 s as one. Saboteur A carries the keycard.

Read more: [SaboteurBot.md](Docs/AI/SaboteurBot.md), [OptimisationLog.md](Docs/OptimisationLog.md), [UI evidence](Docs/Evidence/UI/index.md).

### S4 · Omira Kularatne: Agent Controller and the Captain Bot

**Graphics.** How the agents move and act: route following with eased turns and curved route changes, walking and running clips generated from numbers (the Captain's legs solved with IK), aim, recoil, knock-out and slump poses, hit effects and alert icons. Also the five cutscenes (Timelines, Cinemachine cameras, dialogue), the music, checkpoints and the pause menu. All agent motion together costs about 1.1 ms of a 16.7 ms frame.

**AI.** The Captain predicts where the player is going instead of chasing. It compares the player's last 5 s of movement with Dijkstra distance fields to every goal, works out which goal is likeliest, and plans an A* intercept to a chokepoint it reaches at least 1 s first. On straight walks in Chapter 4 it named the right goal 71% of the time against 22% for "nearest goal", and it reached the player before their goal in 88% of runs against 75% for a chaser.

Read more: [CaptainBot.md](Docs/AI/CaptainBot.md), [AnimationLog.md](Docs/AnimationLog.md).

## How it fits together

```text
Journey + UI       chapters, cutscenes, HUD, scoring          events and blackboard only
Runtime            agent bodies, movement, animation, props   MonoBehaviours
Agent brains       Tracker, Guard, Saboteur x4, Captain       pure C#
AI Core            grid, heap, A*, Dijkstra, FSM, blackboard   pure C#
```

Every brain is plain C# that never touches a GameObject: each tick it gets what it knows and returns an intent (a route, where to look, an action), and the body carries it out. Assemblies enforce the layers, so the four AI architectures sit side by side and can be tested without a scene. The shared pieces are split across the team: the grid and FSM framework (S1), A*, the binary heap and the blackboard (S2), the Dijkstra fields and the runtime that runs every brain (S4). All seven agents together average about 0.06 ms a frame ([AIPerformanceLog.md](Docs/AIPerformanceLog.md)).

The scenes load additively from `Bootstrap`: `Env` (S1), `Interactables` (S2), `Agents` (S4) and `UI` (S3), each edited only by its owner. The full design and the decision log are in [Docs/DesignDoc.md](Docs/DesignDoc.md).

## Tests

1,518 EditMode and 483 PlayMode tests, plus 14 explicit evidence runs that write the measurements and screenshots in `Docs/`. Run them in **Window → General → Test Runner**.

## Documentation

- [Docs/DesignDoc.md](Docs/DesignDoc.md): architecture, contracts and the decision log
- [Docs/AI/](Docs/AI/): one design document per agent
- [Docs/AIPerformanceLog.md](Docs/AIPerformanceLog.md) and [Docs/OptimisationLog.md](Docs/OptimisationLog.md): measured IS and GV evidence
- [Docs/AnimationLog.md](Docs/AnimationLog.md), [Docs/ArtBible.md](Docs/ArtBible.md), [Docs/LevelLayout.md](Docs/LevelLayout.md), [Docs/Story.md](Docs/Story.md), [Docs/LayerMatrix.md](Docs/LayerMatrix.md), [Docs/PlaythroughLog.md](Docs/PlaythroughLog.md), [Docs/BuildLog.md](Docs/BuildLog.md)
- [CONTRIBUTING.md](CONTRIBUTING.md): branches, commits, scene ownership, the model pipeline
- `Docs/Evidence/`: screenshots, clips and logs behind the numbers above

## Building

**Factory Reset → Build → Windows Player** builds the five scenes to `Builds/Windows/FactoryReset.exe` and logs the build in [Docs/BuildLog.md](Docs/BuildLog.md).

## Third-party credits

Some props and the music are not the team's work. Each pack's author, licence and changes are in its README under `Assets/_ThirdParty/`, and the in-game credits show what the licences ask for.

| Pack | Used for | Licence |
| --- | --- | --- |
| Kenney (Factory Kit, Blaster Kit, Platformer Kit) | Starting shapes of some task props, redesigned since | CC0 1.0 |
| Poly Pizza: iPoly3D (Cage), Quaternius (Pickup Sphere) | Switch cage, power core | CC0 1.0 |
| Poly Pizza: Tiff Eidmann (Toy robot), J-Toastie (Concrete Barrier), dook (Vending Machine), Anastasiia Ku (Low poly vending machine) | Wind-up toy, plate stop, crate dispenser, charger | CC BY 3.0 |
| Music: epb9000, MintoDog, cynicmusic, Kistol | Title, Control Room and Results music | CC0 1.0 |
| Music: Eric Matyas, www.soundimage.org | Assembly Floor, Painting Room and Storage Area music | Free with credit |
