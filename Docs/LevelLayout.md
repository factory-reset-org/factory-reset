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
| Doorway height | 3.5 m (Captain is 3.23 m) | 5.0 m for doors 1, 2 and 4 (lintel 5-6 m, framed; Assembly and Painting revamps); 4.0 m for door 3 (lintel 4-6 m) until its rooms are redone |
| Passage width | 2.2 m | narrowest gap 4.5 m (Storage aisles) |
| Wall thickness | 0.5 m | 0.5 m |
| Wall height | 4 m | 6 m, closed by the ceiling |
| Grid snap | 0.5 m | every wall and door edge; room obstacles follow the prototype's grid instead (see Rooms as in the prototype) |

Obstacles follow the prototype's rooms (next section): the 2.6 m Toy-O-Matic assembler (Assembly), five 2.4 m paint tanks (Painting), eight 3.2 m shelf runs (Storage; occluders for Occlusion Culling) and twelve 2.8 m server pillars (Control).

Only the `Level` hierarchy is Static (GI, occluder, occludee, batching, reflection probe). Doorway markers, area volumes and anchors are not geometry and are not static.

**Smooth plastic style:** every box in the level (walls, lintels, obstacles, shelves, toys, trims) is a rounded box from `RoundedBoxMesh`: true rounded edges and corners with smooth shading, saved per size in `Prefabs/Environment/Meshes` (`RoundBox_*`) with lightmap UVs. The radius is a quarter of the smallest side, at most 0.25 m, at most 0.08 m on walls (so room corners keep no deep grooves); pieces under 1 m (toys) get 1 segment (108 triangles), big curves 3 (588). `Tools > Factory Reset > Round Environment Meshes` rebuilds them in place, keeping each asset's GUID. Box colliders keep their exact size, so rounding never changes the NavMesh or grid. Floors stay flat. One baked reflection probe per room (box projection, 128 px) gives the glossy materials something to reflect; it is re-baked after the lighting pass.

## Ceiling

Every room has a ceiling at 6 m, as in the prototype. The walls were raised from 5 m to 6 m to meet it, and the door lintels then filled 4-6 m, so the doorway openings were unchanged. Doors 1, 2 and 4 are now 5 m tall, with lintels at 5-6 m (see Assembly Floor and Painting Room below). S4's cutscene cameras are checked against this height (`CutsceneShotPlan.CeilingHeight`, at least 1 m under it).

- One slab per room (`Level/Ceilings/Ceiling_<Room>`), 20.75 x 0.2 x 20.75 m like the floors, in dark plum (`M_Env_Ceiling`).
- **No collider.** Nothing can reach 6 m, and a collider would put the slab's top into the NavMesh bake as a walkable roof. The NavMesh and grid are unchanged: no doorway, wall foot or obstacle under 4 m moved.
- Nine light panels per room (`Panels_<Room>`), 2.4 x 0.8 m in a 3 x 3 grid 6.5 m apart, with a baked rectangle area light under each (`Lighting/CeilingLights_Baked`). See the Art Bible for the lighting.
- Static like the rest of `Level`. The ceiling casts shadows on purpose: in the bake, an object that casts no shadows lets light through, so the sky would leak into the rooms.

## Rooms as in the prototype

Each room's obstacles are laid out cell for cell from the HTML prototype (`Factory-Reset.html`), where every room is 14 x 10 cells of 2 m (28 x 20 m). Ours are 20 x 20 m, so a prototype row keeps its 2 m and the 14 columns squeeze into 20 m (1.43 m each). Storage and Control are mirrored left to right, because our Control Room is west of Storage where the prototype has it east; after mirroring, every prototype door lines up with one of ours.

| Room | Prototype layout | Built |
| --- | --- | --- |
| Assembly | Two stamping presses side by side between the two belts, on the west side | Replaced in the Assembly revamp by the Toy-O-Matic assembler on the same footprint (x 2.45-6.65, z 10.4-12.6), one 4.2 x 2.6 x 2.2 m collider: the same cover from the Tracker. See Assembly Floor below |
| Painting | Five paint tanks; four carry a spinning bullseye on a pole; three patches of paint | Refitted in the Painting revamp (see Painting Room below). Tanks at (24.6, 17.5), (37.4, 17.5), (27.4, 11.5), (38.9, 5.5) and (23.1, 1.5): white body, coloured band, a lid that spins (`DressingSpinner`), a paint drip, and poles on the first four. The targets (S2's `SpinningTarget`) stand on the poles 3.55 m up and spin about the vertical. The tanks replace the four low cover blocks and are now the Guard Bot's cover. Puddles moved into the prototype's three patches |
| Storage | A shelf maze: short runs of 1 to 4 cells on rows 1, 3, 5 and 7, with staggered gaps | Eight runs (2.86 to 5.72 m) on rows at z 37.5, 33.5, 29.5 and 25.5, each a shelving unit with toys. The run beside door 3 is left out (it would half block the door and Saboteur A's waypoint), and the rows sit 0.5 m south of the exact mapping so Saboteur D's patrol along the north wall stays walkable. Restocked with toys in the Storage revamp (see Storage Area below) |
| Control | Twelve server pillars round a central console; three cores float above pillars | Twelve 1.3 x 2.8 x 1.5 m pillars (rebuilt as detailed server racks in the Control revamp; see Control Room below); the console moved to the room centre (10.5, 31); the cores float 3.75 m up above servers 1, 2 and 9 |

**Still S2's (not in this branch):** the prototype's second (westward) belt and four loose crates in Assembly; three crates, a floor trap and slippery paint in Painting. Our terminal stays at the room centre, because the prototype's spot is where S4's Guard spawns.

## Assembly Floor (final dressing)

Built by `Tools > Factory Reset > Build Assembly Floor Dressing` (`Editor/World/AssemblyFloorDressing.cs`), which removes the old presses and any earlier build and rebuilds everything under `Level/Dressing/Assembly`, so the layout can be tuned and re-run. Meshes are rounded boxes, turned (lathe) shapes and small combined toy meshes from `DressingKit`, saved in `Prefabs/Environment/Meshes`; signs and posters are painted in code into one atlas (`SignPainter`, `T_Env_Signs_Assembly`). After a rebuild, rebake the NavMesh, lighting and occlusion.

Everything stays clear of S2's belts, plate, lever, dispenser, switch cage, charger, fuses, battery and crates, of `player.start`, the agents' spawns, the Tracker's patrol corners and S4's cutscene cameras. Solid props get one invisible box collider each; wall dressing and floor paint have none.

| Part | Where | What |
| --- | --- | --- |
| Toy-O-Matic assembler | x 2.45-6.65, z 10.4-12.6 (the presses' footprint) | A blue parts hopper heaped with coloured parts on chrome legs, a chute, a red stamper whose head stamps a teddy (`DressingPiston`), a white body with a mint band, a screen showing a toy blueprint and its name, a yellow robot arm on a turntable holding a robot toy (`DressingSpinner`), and a podium of blinking buttons (`DressingBlinker`). Hazard outline round it. 2.6 m collider |
| Doors 1 and 4 | east and north walls | Openings raised from 4 to 5 m: the yellow 2 m lintels become 1 m white lintels at 5-6 m, and each opening gets a grey frame (two posts and a header) with hazard plates at the foot. Door 1 has a mint "open" strip; door 4's red alarm beacon sits in the middle of its header. **S2:** the sliding panels (`Door_1`-`Door_4`) are 4 m tall; they need to be 5 m (scale y 5, centre y 2.5) for doors 1 and 4, or closed door 4 shows a 1 m gap above it |
| Floor | whole room | 1 m tiles (S2's request: the size of a crate), with the joins on whole metres; hazard edging along both belts; a dark start pad with a glowing mint ring at `player.start`; three painted chevrons towards the belts; a sun-yellow ring round the control switch; a painted gear medallion in the open middle (paint only: the patrol lane and crate routes cross it) |
| Walls | all four | A mint rail at 1.6 m; rounded grey corner columns; red, yellow and blue pipes along the west and north walls at 5-5.6 m with brackets, rising into the ceiling, and a blue drop to a valve box with a red wheel by the lever; a fire extinguisher; a turning wall fan above the charger |
| Windows | west (3) and south (2, behind the start pad) | 2.2 x 1.6 m, grey frames, white mullions and sills, and an emissive view of a daylight sky over distant sheds (`T_Env_WindowSky`), which also lights the room in the bake |
| Signs | north, east, south, west walls | "01 ASSEMBLY FLOOR" on the north wall (seen from the start), "CONTROL ROOM / SEALED" beside door 4, "PAINTING ROOM" with an arrow beside door 1, posters ("WIND IT UP!", "KEEP PAWS CLEAR", "SMILE! QUALITY TOYS"), a clock with turning hands, a glowing line status screen above the shelf, a tool pegboard |
| Workbench | south wall, x 3.6-6.8 | Orange top on two blue cabinets, a vise, a half-built robot, a duck, loose parts and a lamp. 1 m collider |
| Lockers | south wall, x 12.5-16.3 | Four blue and mint lockers with vents, handles and numbers. 1.9 m collider |
| Pallet | south-west corner | A pallet of taped toy boxes with a teddy on top, behind the switch. 1.4 m collider |
| Parts shelf | east wall, z 16.6-18.6 | Grey shelving with coloured bins of parts and toy boxes, the line status screen above it. 2.1 m collider. Kept clear of z 15-16, where `SaboteurDoorSceneTests` opens a second gap through the wall beside door 1 |
| Ceiling | over the room | An overhead toy rail (x 6-15.2, z 11.6-16 at 5.35 m) carrying twelve hanging toys (teddies, robots, ducks) round the loop (`DressingRail`). Every beam and hanger runs between the ceiling light panels, never under one. No hanging lamps: the panels light the room |

**Budget:** 62,872 triangles for the whole room. Static parts are merged into one renderer per material per section (`DressingKit.MergeStatic`), so the room is 121 renderers instead of about 320 (the rest are moving or blinking parts); the small spheres are 12 x 7 lathes (about 170 triangles, not Unity's 768) and thin rods have 8 sides. New materials are palette plastics (`M_Env_PlasticMint`, `Orange`, `Bubble`, `Plum`), the sign atlas (`M_Env_Signs`, `M_Env_SignsGlow`) and the window view (`M_Env_Window`). Measured view costs are in `OptimisationLog.md`.

## Painting Room (final dressing)

Built by `Tools > Factory Reset > Build Painting Room Dressing` (`Editor/World/PaintingRoomDressing.cs`), with the same kit as the Assembly Floor (`DressingKit`, `RoomPieces`, `SignPainter`; signs in `T_Env_Signs_Painting`). It rebuilds `Level/Dressing/Painting` and refits the five tanks in place. The middle is left to the Guard fight: new solid props stand within a metre of the walls, clear of S2's targets, terminal, switch cage, crates, trap, pickups and slippery zones, the Guard's patrol ((25, 10.5) to (37, 10.5)), Saboteur C's ((38, 3) to (24, 3)) and the three Painting cutscene cameras. It is deliberately not a copy of room 1: round windows, no corner columns, a three-colour stripe instead of a rail, paint lines from the ceiling instead of wall pipes, ceiling extractor fans, and no rail of toys.

| Part | Where | What |
| --- | --- | --- |
| Paint tanks | the five tanks | A more detailed model on the same size and collider (the Guard's cover, the targets' poles): a footed base ring in place of the plain plinth, two steel hoops, a sight glass on the side showing the paint level, a ladder up the back, a gauge and a valve at the front, and paint runs down the sides. The round paint-drip balls are gone |
| Mixing vat | tank 5 (23.1, 1.5) | The tank with no target: a motor on a bridge over its turning lid and a MIXER label, fed by four paint lines (pink, yellow, blue, mint) that drop from a manifold in the ceiling with a shut-off wheel each |
| Doors 1 and 2 | west and north walls | Door 2 raised to 5 m (white lintel at 5-6 m); both openings framed on this side, with a mint "open" strip. **S2:** `Door_2`'s panel needs to be 5 m too, like doors 1 and 4 |
| Floor | slippery zones, by the drying rack | Eight paint splats with droplets where the round puddles were, on S2's three slippery zones; a trail of red paw prints from a splat towards the drying rack |
| Walls | all four | A pink, yellow and mint stripe at 1.5-1.7 m; paint drips running down from the top band; five round porthole windows (bolted grey frames) with the daylight view |
| Signs | east, north, west walls | "02 PAINTING ROOM" high on the east wall (seen from door 1), "ASSEMBLY FLOOR" and "STORAGE AREA" plaques by the doors, a colour-wheel mural, posters ("WET PAINT!", "SWATCHES", "STAY IN THE LINES!"), a glowing paint-levels screen |
| Paint tins and drums | south wall, x 25.5-28.5 and 34.1-36.5 | Eighteen stacked tins with coloured bands and lids; three drums with hoops. 1.1 m and 0.96 m colliders |
| Drying rack | east wall, z 12.6-15.6 | Three shelves of freshly painted toys. 2 m collider |
| Sink | west wall, z 16.5-18.3 | A cleaning sink with a tap and a pot of brushes, spray guns on hooks above. 1 m collider. Kept north of z 16, where `SaboteurDoorSceneTests` opens a second gap through this wall |
| Ceiling | between the light panels | Two extractor fans set into the ceiling, turning (`DressingSpinner`). No hanging lamps and no moving rack |

**Budget:** 89,160 triangles (the five detailed tanks are 28,484 of them), merged to 99 renderers. Measured view costs are in `OptimisationLog.md`.

## Storage Area (final dressing)

Built by `Tools > Factory Reset > Build Storage Area Dressing` (`Editor/World/StorageAreaDressing.cs`), with the same kit as the other rooms (signs in `T_Env_Signs_Storage`). It rebuilds `Level/Dressing/Storage`, raises door 3 and restocks the eight shelf runs. A second item, `Tools > Factory Reset > Restyle Relay Order Board`, restyles the relay board in `Interactables.unity`: the board is static set dressing, so its look is S1's, while S2's `RelayBoard` component, its collider and the three slots it tints are kept as they were. New solid props stand against the walls, clear of S2's relays, switch cage, batteries, overcharge and trap, Saboteur A's spawn and route ((37, 24) to (23, 28)), Saboteur D's ((25, 39) to (38, 39)) and the two Storage cutscene cameras. The room reads as a warehouse, unlike rooms 1 and 2: high strip windows, an orange wall band, steel roof beams with hanging aisle signs, a loading dock and yellow aisle lines.

| Part | Where | What |
| --- | --- | --- |
| Relay order board | north wall, (36, 3.5, 40.75) | A dark rounded panel on an orange backplate: a "RELAY ORDER" header; the three slots (tinted at runtime with the relays' colours, first step on the left) in chrome sockets with arrows between them; numbered step badges 1, 2, 3 under them; "USE THE RELAYS IN THIS ORDER"; two small lamps on arms. Beside it, a glowing map, "WHERE ARE THE RELAYS?", with each relay as a dot in its colour and a star at the board. Collider unchanged (2.4 x 1 x 0.1 m) |
| Relays | (40.2, 33.5), (21.8, 25.5), (27.8, 22.2) | A floor ring in each relay's colour (red, green, blue) inside a thin glowing ring, and a plaque on the wall behind ("RED RELAY", "GREEN RELAY", "BLUE RELAY"), so each square on the board leads to its relay. Paint and signs only, no colliders |
| Doors 2 and 3 | south and west walls | Door 3 raised to 5 m (white lintel at 5-6 m); both openings framed on this side, door 2 with a mint "open" strip; door 3's alarm beacons moved onto the new header and lintel. **S2:** `Door_3`'s panel needs to be 5 m too, like doors 1, 2 and 4 |
| Loading dock | east wall, z 22-27 | A closed roller shutter between hazard-striped jambs under a "DOCK 3" sign and two amber lamps, hazard paint on the floor, a toy forklift carrying a boxed pallet (one 2.6 x 1.8 x 1.2 m collider, x 37.9-40.5, z 21.8-23) and two shrink-wrapped pallets (1.4 m colliders) |
| Shelves | the eight runs | Restocked: the 144 placeholder blocks are replaced by teddies, robots and ducks (low-poly versions of the room toys) and taped toy boxes in mixed palette colours, a row each side of the spine on the lower two boards and boxed overstock on the third. Each run's stock is merged on its own and lit by light probes, as the blocks were. Labels A1 to D2 on the west ends; yellow aisle lines along both sides of every run |
| Walls | all four | An orange band at 1.35 m; four high strip windows on the north and east walls (4.65-5.25 m) with mullions and the daylight view; "03 STORAGE AREA" on the north wall; "PAINTING ROOM" and "CONTROL ROOM / SEALED" plaques beside the doors, their arrows pointing at them; posters ("LIFT WITH CARE", "FRAGILE", "COUNT EVERY TOY!") |
| Packing table | south wall, x 22.6-25.4 | Boxes, tape, a scale with a glowing display and a teddy. 1 m collider |
| Roll cage | south wall, x 34.05-35.35 | A wheeled cage of boxes. 1.7 m collider |
| Ceiling | z 27.75 and 34.25 | Two steel beams across the room between the light panels' rows, carried on wall brackets, each with a double-sided aisle sign ("PLUSH TOYS", "ROBOTS & BLOCKS") hanging between the panels and no lower than 4.9 m, so the high cutscene camera still sees the board. No hanging lamps and nothing moving |

**Budget:** 121,152 triangles in 124 renderers; the shelf stock is 93,412 of them in 88 renderers (eight runs, eleven materials each), the rest of the room 27,740 in 36. Measured view costs are in `OptimisationLog.md`.

## Control Room (final dressing)

Built by `Tools > Factory Reset > Build Control Room Dressing` (`Editor/World/ControlRoomDressing.cs`), with the same kit as the other rooms (signs and server fronts in `T_Env_Signs_Control`). It rebuilds `Level/Dressing/Control`, replaces the old `ServerRacks`, refits the twelve server pillars in place and restyles the three screens. The room is Factory OS's home and the darkest in the factory, but not a horror room: no windows, dark violet walls, dimmed cool ceiling light, violet strips and screens, and red only on the lit cables and the racks' plinth strips. Everything stays clear of S2's console, cores, traps and pickups, the Captain's spawn (10.5, 36) and the Control cutscene cameras: the Captain's wake shot still looks through the clear gap in the middle server row.

| Part | Where | What |
| --- | --- | --- |
| Server racks | the twelve pillars, same footprints and colliders | Rebuilt as detailed cabinets: a dark shell on a black plinth with a red strip, a crown, framed fronts of stacked server units (drive bays, storage arrays, network switches, vented blanks) on one side and fan units on the other, a chrome handle, vented side panels with the Factory OS gear emblem and a "DANGER HIGH VOLTAGE" label, and status lights that blink in four groups (`DressingBlinker`). The three racks under the cores carry a glowing violet cradle that beams up to the core |
| Cables and floor channels | from every rack to the walls | Four cables (one lit red) leave each rack low on its side or front and drop through a rubber-lipped floor port into a grated channel in the floor, lit red along its length. The outer racks' channels run to the side walls, the corner racks' to the north and south walls, where the cables come up out of the floor into the wall cabinets; the inner racks' channels meet a trunk that runs under the console. Paint and thin meshes only, no colliders |
| Wall cabinets | seven banks on all four walls | Built-in banks of 1.2 x 3.2 x 0.4 m server cabinets (ten on the west wall, five in each of the others' banks), alternating open fronts of server units with vented doors carrying the gear emblem, under a dark header with a violet strip, between steel pilasters, with blinking status lights (six groups per bank). One collider per bank; they stand within a metre of the wall, clear of the doors, traps and pickups |
| Mainframe | north wall, x 8.7-12.3 | A 3.6 m mainframe: four dark tape reels with violet rims, a red core slot between racks of drives, vents and a "DO NOT UNPLUG" plate, under a header with a violet strip. 3.6 m collider |
| Screens | north wall, 3.65-5.3 m | The three screens (the same objects, which `LightingState` dims at the shutdown) moved above the cabinets and mainframe, in black bezels: system-load graphs, "FACTORY OS / MAINFRAME ONLINE" with the gear emblem, and the three power cores |
| Walls | all four | Dark violet cladding from the skirting to the top band, with steel seams every 2.5 m and a violet strip at 0.68 m. No windows |
| Signs | west and south walls, door 3 | "04 CONTROL ROOM" over the west bank (seen from door 3), "STORAGE AREA" and "ASSEMBLY FLOOR" plaques pointing at their doors, "AUTHORISED UNITS ONLY" beside door 4 |
| Doors 3 and 4 | east and south walls | Both framed on this side with a red strip under the header; their alarm beacons moved just above the headers. **S2:** the door panels need to be 5 m, as before |
| Lighting | room | The floor turns dark with a violet glowing grid, the ceiling panels get their own dimmer material (`M_Env_CeilingPanelControl`), the nine area lights drop from 14 to 9 and turn a cooler violet (`#9C8EFF`), the fill light turns violet-grey, and a baked violet light glows over the mainframe (`Glow_Mainframe`) |

**Budget:** 120,478 triangles in 180 renderers, of which 94 are the blinking light groups (5,216 triangles); the wall banks are 57,748 triangles and the racks' detail 25,008. The racks merge per row and the cabinets per bank, not per room, so occlusion culling can leave out the ones out of view. Measured view costs are in `OptimisationLog.md`.

## Room dressing

Dressing from the prototype that makes each room read as its own part of the factory. None of it has a collider, so it never changes the NavMesh or the grid; the obstacles themselves are in the section above.

| Room | Dressing | Notes |
| --- | --- | --- |
| Storage | Every shelf run is a shelving unit: a thin spine, blue posts at most 3 m apart and four white boards, stocked with toys and toy boxes on the lower three boards (since the Storage revamp; see Storage Area above) | The stock stays inside the shelf's 1 m depth. It is lit by light probes, not lightmaps, to keep the lightmaps small |
| Painting | Eight paint splats on S2's three slippery zones (were round puddles until the Painting revamp) | Purely visual; S2's `SlipperyFloor` zones make them slippery |
| Control | Banks of server cabinets built into every wall and a mainframe, with blinking status lights (`DressingBlinker`); replaced the nine thin wall racks in the Control revamp (see Control Room above) | 0.4 m deep with a collider per bank, clear of the doors, console, traps, battery and overcharge anchors |
| Assembly (and one pair in Painting) | Eight wall gears in meshing pairs that turn (`DressingSpinner`); a small gear turns faster than its partner, at the tooth ratio | `GearMesh` builds the toothed discs |

**Surfaces:** a hazard-stripe skirting (0.6 m) runs along every room wall except across the doorways, with a dark plum band (0.3 m) under the ceiling; floors are tiled; the Control Room floor is dark with a violet glowing grid; the Assembly assembler has a hazard outline on the floor, and the Assembly floor's tiles are 1 m, the size of S2's crates (the other rooms' are 0.5 m). All are thin, flat and without colliders (the skirting stands 0.04 m off the wall, inside the 0.55 m clearance). See the Art Bible.

**At the shutdown:** the gears, paint-tank lids, the assembler's robot arm, the Assembly wall fan, clock and toy rail, and the Painting extractor fans wind down to a stop, the assembler's stamper rises and stops, its buttons go dark, and every server light goes dark on `CutsceneSignals.FactoryShutdown`, in step with `LightingState`.

## NavMesh and grid

- NavMesh agent type 0: **radius 0.55 m, height 3.25 m** (the largest measured capsules, decision D3). Slope 45, climb 0.75 unchanged.
- `NavMeshSurface` on `Level`: collects children, physics colliders. Doorway and area triggers sit outside `Level` so they never enter the bake.
- Bake output: `Scenes/Env/NavMesh-Level.asset` (Git LFS).
- `GridManager`: origin (0, 0, 0), **83 x 83 cells** of 0.5 m.

Measured after the bake (editor build of the grid):

| Check | Result |
| --- | --- |
| Walkable cells | 4,545 (Assembly 1,279, Painting 1,216, Storage 1,019, Control 1,031), down from 4,673 before the Control revamp, 4,756 before the Storage revamp, 4,963 before the Assembly and Painting revamps and 5,318 before the prototype rooms |
| Walkable cells per doorway | 4 of 6 (2 m clear after the 0.55 m clearance on each side) |
| Reachable from `player.start`, doors 3 and 4 closed | 3,510: Assembly, Painting, Storage; Control 0 (counted with the two doorways blocked) |
| Reachable with every door open | all 4,545 |
| Every task anchor reachable from `player.start` on the NavMesh | all 34 |

## Area volumes

One trigger box per room with an `AreaVolume` (chapter number, display name). `AreaVolume.Find(position)` returns the room containing a point, or null in walls and doorways, for the chapter flow and the HUD.

## Task anchors

Each anchor is an empty object with a `TaskAnchor` (id, chapter, placement reason). S2 places the prop on its anchor in `Interactables.unity`; chapter data refers to it by id. Chapter `all` means the anchor is used in every chapter.

| Anchor | Chapter | Position | Why it is there |
| --- | --- | --- | --- |
| `player.start` | all | 10.5, 0, 3 | Open south end of the Assembly Floor facing the belts; the Toy-O-Matic assembler hides part of the Tracker's patrol so the first threat is heard before it is seen. |
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
| `ch1.fuse.2` | 1 | 10.5, 0, 9.5 | Middle of the room east of the assembler, in the Tracker's patrol lane, so it is collected under pressure. |
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
