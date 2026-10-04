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
      |  console (north)  |  relays, board     |
 21   +--------4----------+---------2---------+
      |  1 ASSEMBLY FLOOR |  2 PAINTING ROOM  |
      |  presses, belts   1  low cover blocks  |
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
| Doorway height | 3.5 m (Captain is 3.23 m) | 4.0 m, lintel 4-5 m |
| Passage width | 2.2 m | narrowest gap 4.5 m (Storage aisles) |
| Wall thickness | 0.5 m | 0.5 m |
| Wall height | 4 m | 5 m |
| Grid snap | 0.5 m | every wall, door and obstacle edge |

Obstacles: two 2.8 m presses (Assembly), four 1.2 m cover blocks (Painting; low enough to shoot over when standing, high enough to hide a crouched player), three 3.2 m shelf rows (Storage; occluders for the later Occlusion Culling pass), three 3 m pillars carrying the cores (Control).

Only the `Level` hierarchy is Static (GI, occluder, occludee, batching, reflection probe). Doorway markers, area volumes and anchors are not geometry and are not static.

## NavMesh and grid

- NavMesh agent type 0: **radius 0.55 m, height 3.25 m** (the largest measured capsules, decision D3). Slope 45, climb 0.75 unchanged.
- `NavMeshSurface` on `Level`: collects children, physics colliders. Doorway and area triggers sit outside `Level` so they never enter the bake.
- Bake output: `Scenes/Env/NavMesh-Level.asset` (Git LFS).
- `GridManager`: origin (0, 0, 0), **83 x 83 cells** of 0.5 m.

Measured after the bake (editor build of the grid):

| Check | Result |
| --- | --- |
| Walkable cells | 5,318 (Assembly 1,380, Painting 1,360, Storage 1,220, Control 1,358) |
| Walkable cells per doorway | 4 of 6 (2 m clear after the 0.55 m clearance on each side) |
| Reachable from `player.start`, doors 3 and 4 closed | Assembly, Painting, Storage; Control 0 |
| Reachable with every door open | all 5,318 |

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
| `overcharge.2` | all | 37.5, 0, 33.5 | Storage east aisle, between relays, for the Saboteur fights in Chapter 3. |
| `overcharge.3` | all | 2, 0, 39 | Control Room north-west corner, far from the cores, for the final fight with the Captain. |
| `switch.1` | 1 | 1.5, 0, 4 | South-west corner, furthest from the Painting door, so restoring it means crossing back past the Tracker. |
| `ch1.lever` | 1 | 2, 0, 14.5 | West wall beside the belt start, so the player sees the belts start moving the moment they pull it. |
| `ch1.belt.start` | 1 | 3.5, 0, 17.5 | Top belt runs east along z = 17.5; starting at the west wall means the crate rides 12 m across the open room. |
| `ch1.plate` | 1 | 15.5, 0, 17.5 | At the belt's end: the crate arrives settled, and the plate click (L0 40) is near enough to pull the Tracker. |
| `ch1.dispenser` | 1 | 2, 0, 19.5 | Directly above the belt start, so a replacement crate lands on the belt without a long push. |
| `ch1.fuse.1` | 1 | 19.5, 0, 1.5 | South-east corner by the charger: the first fuse is safe and teaches pickups. |
| `ch1.fuse.2` | 1 | 10.5, 0, 9.5 | Between the two presses, in the Tracker's patrol lane, so it is collected under pressure. |
| `ch1.fuse.3` | 1 | 19.5, 0, 19.5 | North-east corner by the Painting wall, the furthest fuse from the switch, so collecting all three crosses the room twice. |
| `switch.2` | 2 | 40.25, 0, 10.5 | East wall opposite the Assembly door, so the player crosses the whole room past the Guard's cover. |
| `ch2.terminal` | 2 | 31, 0, 10.5 | Open room centre, exposed from every cover block: the 6 s hack puts the player in the Guard's sightlines and its beeps (L0 60) carry through both doors. |
| `ch2.target.1` | 2 | 24, 2.5, 0.75 | South-west wall, 2.5 m up; visible from the room centre like the others, so the 12 s window is about aiming. |
| `ch2.target.2` | 2 | 38, 2.5, 0.75 | South-east wall: hitting it turns the player's back on the Storage door. |
| `ch2.target.3` | 2 | 40.75, 2.5, 16 | East wall, near the switch; a different bearing from the others so no two are hit without turning. |
| `ch2.target.4` | 2 | 24, 2.5, 20.25 | North wall near the north-west corner, behind and to the left of a player entering from the Assembly door, so the last target forces a turn away from the Guard. |
| `switch.3` | 3 | 39.5, 0, 40 | North-east corner beside the sequence board, the far end of the shelf maze from the Painting door. |
| `ch3.board` | 3 | 36, 3.5, 40.75 | North wall 3.5 m up, above the 3.2 m shelves, so it can be read from most aisles. |
| `ch3.relay.1` | 3 | 23.5, 0, 23 | South-west corner of Storage, one of three far corners, so any order forces long runs between the shelves. |
| `ch3.relay.2` | 3 | 39, 0, 28 | East aisle, across the maze from relay 1; a wrong order's alarm (L0 90) reaches the Painting Room. |
| `ch3.relay.3` | 3 | 28.5, 0, 39.5 | North aisle behind the last shelf row, the deepest point of the maze. |
| `ch4.console` | 4 | 10.5, 0, 40 | North wall, the far end from both doors, so the 3 s hold happens with the Captain able to arrive from either door. |
| `ch4.core.1` | 4 | 5, 3, 35 | On a 3 m pillar so cores are shot, not reached; west pillar. |
| `ch4.core.2` | 4 | 16, 3, 35 | East pillar, across the room from core 1: no single cover spot sees both. |
| `ch4.core.3` | 4 | 10.5, 3, 28.5 | Pillar facing the south door, the first core the player sees on entering. |

## Agent spawn rooms (S4 places the points in Agents.unity)

Tracker: Assembly. Guard: Painting. Saboteur A: Storage. B: Assembly. C: Painting. D: Storage north. Captain: Control (dormant).

## Not done yet

- Materials are provisional greybox tints; the Art Bible palette and lighting come in Task I.
- Play from `Bootstrap.unity` needs S2's SceneLoader, which calls `GridManager.BuildGrid()`.
