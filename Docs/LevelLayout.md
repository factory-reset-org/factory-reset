# Level layout (greybox)

Owner: S1 (World Builder). Scene: `Assets/_Project/Scenes/Env.unity`.

The scene file cannot be reviewed in a diff, so this page records what is in it and why. Positions are world metres; the level's south-west outer corner is the origin and the floor is flat at `y = 0`.

## Layout

Four 20 x 20 m rooms in a 2 x 2 ring, 41.5 x 41.5 m including walls. The journey runs Assembly -> Painting -> Storage -> Control.

```text
 z
 41.5 +-------------------+-------------------+
      |  4 CONTROL ROOM   |  3 STORAGE AREA   |
      |  pillars + cores  3  shelf maze        |
      |  12 servers,      |  relays, board     |
      |  console (centre) |                    |
 21   +--------4----------+---------2---------+
      |  1 ASSEMBLY FLOOR |  2 PAINTING ROOM  |
      |  presses, belts   1  paint tanks with  |
      |                   |  targets on poles  |
      |  start (south)    |  terminal (centre) |
 0    +-------------------+-------------------+
      0                 20.75                41.5  x
```

| Door | Between | Opening | Starts |
| --- | --- | --- | --- |
| 1 | Assembly - Painting | x 20.5-21, z 9-12 | open |
| 2 | Painting - Storage | x 29.5-32.5, z 20.5-21 | open |
| 3 | Storage - Control | x 20.5-21, z 29.5-32.5 | closed (Control Room locked) |
| 4 | Control - Assembly | x 9-12, z 20.5-21 | closed (Control Room locked) |

**Why a ring:** the player's route through the first three chapters is linear, but once the Chapter 3 cutscene unlocks doors 3 and 4, the Control Room has two entrances. The Captain then has two chokepoints to choose between when it predicts the player's goal, and the Tracker can loop round instead of meeting a dead end.

**Why doors 3 and 4 start closed:** the plan locks the Control Room until the Chapter 3 cutscene. The grid's doorway markers start them closed, so no agent plans a route through them. S2's door listens to `CutsceneSignals.ControlRoomUnlock` and will open them through `GridManager.SetDoorClosed`.

## Size rules (from the measured agents)

| Rule | Minimum | Used |
| --- | --- | --- |
| Doorway width | 2.5 m | 3.0 m |
| Doorway height | 3.5 m (Captain is 3.23 m) | 4.0 m, lintel 4-6 m |
| Passage width | 2.2 m | narrowest gap 4.5 m (Storage aisles) |
| Wall thickness | 0.5 m | 0.5 m |
| Wall height | 4 m | 6 m, closed by the ceiling |
| Grid snap | 0.5 m | every wall and door edge; room obstacles follow the prototype's grid instead (see Rooms as in the prototype) |

Obstacles follow the prototype's rooms (next section): a pair of 2.8 m stamping presses (Assembly), five 2.4 m paint tanks (Painting), eight 3.2 m shelf runs (Storage; occluders for Occlusion Culling) and twelve 2.8 m server pillars (Control).

Only the `Level` hierarchy is Static (GI, occluder, occludee, batching, reflection probe). Doorway markers, area volumes and anchors are not geometry and are not static.

**Smooth plastic style:** walls, lintels and obstacles use chamfered meshes from `BevelledBoxMesh` (0.06 m chamfer on walls, 0.08 m on obstacles; 44 triangles per box), saved per size in `Prefabs/Environment/Meshes` with lightmap UVs. Their box colliders keep the exact original size, so the NavMesh and grid are unchanged (still 5,318 walkable cells). Floors stay flat. One baked reflection probe per room (box projection, 128 px) gives the glossy materials something to reflect; it is re-baked after the lighting pass.

## Ceiling

Every room has a ceiling at 6 m, as in the prototype. The walls were raised from 5 m to 6 m to meet it, and the door lintels now fill 4-6 m, so the doorway openings are unchanged. S4's cutscene cameras are checked against this height (`CutsceneShotPlan.CeilingHeight`, at least 1 m under it).

- One slab per room (`Level/Ceilings/Ceiling_<Room>`), 20.75 x 0.2 x 20.75 m like the floors, in dark plum (`M_Env_Ceiling`).
- **No collider.** Nothing can reach 6 m, and a collider would put the slab's top into the NavMesh bake as a walkable roof. The NavMesh and grid are unchanged: no doorway, wall foot or obstacle under 4 m moved.
- Nine light panels per room (`Panels_<Room>`), 2.4 x 0.8 m in a 3 x 3 grid 6.5 m apart, with a baked rectangle area light under each (`Lighting/CeilingLights_Baked`). See the Art Bible for the lighting.
- Static like the rest of `Level`. The ceiling casts shadows on purpose: in the bake, an object that casts no shadows lets light through, so the sky would leak into the rooms.

## Rooms as in the prototype

Each room's obstacles are laid out cell for cell from the HTML prototype (`Factory-Reset.html`), where every room is 14 x 10 cells of 2 m (28 x 20 m). Ours are 20 x 20 m, so a prototype row keeps its 2 m and the 14 columns squeeze into 20 m (1.43 m each). Storage and Control are mirrored left to right, because our Control Room is west of Storage where the prototype has it east; after mirroring, every prototype door lines up with one of ours.

| Room | Prototype layout | Built |
| --- | --- | --- |
| Assembly | Two stamping presses side by side between the two belts, on the west side | The press pair at (3.5, 11.5) and (5.6, 11.5): base, blue pillars, red head, yellow anvil and a chrome piston that pumps (`DressingPiston`), with one hazard outline round the pair. Collider unchanged, 2 x 2.8 x 2 m each |
| Painting | Five paint tanks; four carry a spinning bullseye on a pole; three patches of paint | Tanks at (24.6, 17.5), (37.4, 17.5), (27.4, 11.5), (38.9, 5.5) and (23.1, 1.5): white body, coloured band, a lid that spins (`DressingSpinner`), a paint drip, and poles on the first four. The targets (S2's `SpinningTarget`) stand on the poles 3.55 m up and spin about the vertical. The tanks replace the four low cover blocks and are now the Guard Bot's cover. Puddles moved into the prototype's three patches |
| Storage | A shelf maze: short runs of 1 to 4 cells on rows 1, 3, 5 and 7, with staggered gaps | Eight runs (2.86 to 5.72 m) on rows at z 37.5, 33.5, 29.5 and 25.5, each a shelving unit with toys. The run beside door 3 is left out (it would half block the door and Saboteur A's waypoint), and the rows sit 0.5 m south of the exact mapping so Saboteur D's patrol along the north wall stays walkable |
| Control | Twelve server pillars round a central console; three cores float above pillars | Twelve 1.3 x 2.8 x 1.5 m pillars with glowing LED faces and purple caps; the console moved to the room centre (10.5, 31); the cores float 3.75 m up above servers 1, 2 and 9 |

**Still S2's (not in this branch):** the prototype's second (westward) belt and four loose crates in Assembly; three crates, a floor trap and slippery paint in Painting. Our terminal stays at the room centre, because the prototype's spot is where S4's Guard spawns.

## Room dressing

Dressing from the prototype that makes each room read as its own part of the factory. None of it has a collider, so it never changes the NavMesh or the grid; the obstacles themselves are in the section above.

| Room | Dressing | Notes |
| --- | --- | --- |
| Storage | Every shelf run is a shelving unit: a thin spine, blue posts at most 3 m apart, four white boards and 144 toys in all on the lower three boards | Toys stay inside the shelf's 1 m depth. They are lit by light probes, not lightmaps, to keep the lightmaps small |
| Painting | Eight glossy paint puddles, flat on the floor | Purely visual: the prototype's slippery paint would be S2's player physics |
| Control | Nine server racks against the walls, each with 24 status lights that blink (`DressingBlinker`) | 0.25 m deep, inside the 0.55 m wall clearance, clear of the doors, console, battery and overcharge anchors |
| Assembly (and one pair in Painting) | Eight wall gears in meshing pairs that turn (`DressingSpinner`); a small gear turns faster than its partner, at the tooth ratio | `GearMesh` builds the toothed discs |

**Surfaces:** a hazard-stripe skirting (0.6 m) runs along every room wall except across the doorways, with a dark plum band (0.3 m) under the ceiling; floors are tiled; the Control Room floor has a glowing grid; the Assembly press pair has a hazard outline on the floor. All are thin, flat and without colliders (the skirting stands 0.04 m off the wall, inside the 0.55 m clearance). See the Art Bible.

**At the shutdown:** the gears and paint-tank lids wind down to a stop, the press pistons rise and stop, and every server light goes dark on `CutsceneSignals.FactoryShutdown`, in step with `LightingState`.

## NavMesh and grid

- NavMesh agent type 0: **radius 0.55 m, height 3.25 m** (the largest measured capsules, decision D3). Slope 45, climb 0.75 unchanged.
- `NavMeshSurface` on `Level`: collects children, physics colliders. Doorway and area triggers sit outside `Level` so they never enter the bake.
- Bake output: `Scenes/Env/NavMesh-Level.asset` (Git LFS).
- `GridManager`: origin (0, 0, 0), **83 x 83 cells** of 0.5 m.

Measured after the bake (editor build of the grid):

| Check | Result |
| --- | --- |
| Walkable cells | 4,963 (Assembly 1,388, Painting 1,314, Storage 1,102, Control 1,159), down from 5,318 before the prototype rooms |
| Walkable cells per doorway | 4 of 6 (2 m clear after the 0.55 m clearance on each side) |
| Reachable from `player.start`, doors 3 and 4 closed | 3,800: Assembly, Painting, Storage; Control 0 |
| Reachable with every door open | all 4,963 |
| Every task anchor reachable from `player.start` on the NavMesh | all 34 |

## Area volumes

One trigger box per room with an `AreaVolume` (chapter number, display name). `AreaVolume.Find(position)` returns the room containing a point, or null in walls and doorways, for the chapter flow and the HUD.

## Task anchors

Each anchor is an empty object with a `TaskAnchor` (id, chapter, placement reason). S2 places the prop on its anchor in `Interactables.unity`; chapter data refers to it by id. Chapter `all` means the anchor is used in every chapter.

| Anchor | Chapter | Position | Why it is there |
| --- | --- | --- | --- |
| `player.start` | all | 10.5, 0, 3 | Open south end of the Assembly Floor facing the belts; the presses hide the Tracker's patrol so the first threat is heard before it is seen. |
| `charger` | all | 18.5, 0, 2.5 | Beside the Painting door, so the player tops up just before the first fight with the Guard. |
| `battery.1` | all | 2, 0, 10.5 | Assembly west wall, away from both doors: restocking means crossing the Tracker's patrol lane. |
| `battery.2` | all | 39, 0, 2 | Painting south-east corner, behind the Guard's cover line: a risky detour while the Guard holds the room. |
| `battery.3` | all | 39, 0, 19 | Painting north-east corner on the way to the Storage door, a reward for clearing the room. |
| `battery.4` | all | 39, 0, 39 | Storage north-east corner, past the last shelf row, near the Storage switch. |
| `battery.5` | all | 23, 0, 39 | Storage north-west corner, at the end of a shelf aisle the Saboteurs patrol. |
| `battery.6` | all | 2, 0, 23 | Control Room south-west corner, the only refill inside the final room. |
| `overcharge.1` | all | 31, 0, 1.5 | Painting south wall, in the Guard's field of fire: worth the risk before the targets and terminal. |
| `overcharge.2` | all | 28.8, 0, 27.5 | Storage, the prototype's open spot in the middle of the maze, between the rows the Saboteurs hunt through. |
| `overcharge.3` | all | 2, 0, 39 | Control Room north-west corner, far from the cores, for the final fight with the Captain. |
| `switch.1` | 1 | 1.5, 0, 4 | South-west corner, furthest from the Painting door, so restoring it means crossing back past the Tracker. |
| `ch1.lever` | 1 | 2, 0, 14.5 | West wall beside the belt start, so the player sees the belts start moving the moment they pull it. |
| `ch1.belt.start` | 1 | 3.5, 0, 17.5 | Top belt runs east along z = 17.5; starting at the west wall means the crate rides 12 m across the open room. |
| `ch1.plate` | 1 | 15.5, 0, 17.5 | At the belt's end: the crate arrives settled, and the plate click (L0 40) is near enough to pull the Tracker. |
| `ch1.dispenser` | 1 | 2, 0, 19.5 | Directly above the belt start, so a replacement crate lands on the belt without a long push. |
| `ch1.fuse.1` | 1 | 19.5, 0, 1.5 | South-east corner by the charger: the first fuse is safe and teaches pickups. |
| `ch1.fuse.2` | 1 | 10.5, 0, 9.5 | Middle of the room east of the press pair, in the Tracker's patrol lane, so it is collected under pressure. |
| `ch1.fuse.3` | 1 | 19.5, 0, 19.5 | North-east corner by the Painting wall, the furthest fuse from the switch, so collecting all three crosses the room twice. |
| `switch.2` | 2 | 40.25, 0, 10.5 | East wall opposite the Assembly door, so the player crosses the whole room past the Guard's cover. |
| `ch2.terminal` | 2 | 31, 0, 10.5 | Open room centre between the paint tanks: the 6 s hack puts the player in the Guard's sightlines and its beeps (L0 60) carry through both doors. |
| `ch2.target.1` | 2 | 24.6, 3.55, 17.5 | On a pole above the north-west paint tank (prototype). High and spread out, so the 12 s window means moving between the Guard's cover spots. |
| `ch2.target.2` | 2 | 37.4, 3.55, 17.5 | Above the north-east tank, across the room from target 1. |
| `ch2.target.3` | 2 | 27.4, 3.55, 11.5 | Above the tank left of centre, the first target a player entering from the Assembly door sees. |
| `ch2.target.4` | 2 | 38.9, 3.55, 5.5 | Above the south-east tank near the switch, the furthest from the door. |
| `switch.3` | 3 | 39.5, 0, 40 | North-east corner beside the sequence board, the far end of the shelf maze from the Painting door. |
| `ch3.board` | 3 | 36, 3.5, 40.75 | North wall 3.5 m up, above the 3.2 m shelves, so it can be read from most aisles. |
| `ch3.relay.1` | 3 | 40.2, 0, 33.5 | East wall, row 3 of the prototype maze: one of three spread-out spots, so any order forces long runs between the shelves. |
| `ch3.relay.2` | 3 | 21.8, 0, 25.5 | West wall near the Control door, across the maze from relay 1; a wrong order's alarm (L0 90) reaches the Painting Room. |
| `ch3.relay.3` | 3 | 27.8, 0, 22.2 | Beside the Painting door, the first relay the player passes, which makes the order matter. |
| `ch4.console` | 4 | 10.5, 0, 31 | Room centre between the server rows (prototype), so the 3 s hold happens in the open with the Captain able to arrive from either door. |
| `ch4.core.1` | 4 | 16.93, 3.75, 38 | Floating above a north-east server pillar (prototype), so cores are shot, not reached. |
| `ch4.core.2` | 4 | 4.06, 3.75, 38 | Above the north-west server, across the room from core 1: no single cover spot sees both. |
| `ch4.core.3` | 4 | 8.35, 3.75, 28 | Above an inner server of the south row, the first core the player sees from the south door. |

## Agent spawn rooms (S4 places the points in Agents.unity)

Tracker: Assembly. Guard: Painting. Saboteur A: Storage. B: Assembly. C: Painting. D: Storage north. Captain: Control (dormant).

## Not done yet

- S2's part of the prototype rooms (see Rooms as in the prototype).
