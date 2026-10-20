# Factory Reset

A cartoon 3D shooter in smooth, glossy toy plastic, set inside an abandoned toy factory whose AI has turned every toy against you. Fight your way through four chapters of the factory against four kinds of autonomous enemy (a Tracker, a Guard, a squad of four Saboteurs and a Captain), earn each of the three control switches by finishing that area's tasks, and force a factory reset.

Joint project for SE3032 Graphics & Visualization and SE3062 Intelligent Systems.

## Team

| Student | GV role | IS agent | Architecture |
| --- | --- | --- | --- |
| S1 | World Builder | Tracker Toy | Greedy Best-First Search + hierarchical FSM |
| S2 | Systems Engineer | Guard Bot | Tactical A* + cover evaluation |
| S3 | Core Developer | Saboteur Squad (4 instances) | Utility AI with response curves and squad target claims |
| S4 | Agent Controller (movement, animation, cutscenes) | Captain Bot | Goal prediction + Dijkstra fields + A* intercept |

## The journey

Four chapters in a fixed order: Assembly Floor, Painting Room, Storage Area, Control Room. Each chapter's control switch is sealed in an energy cage until the tasks in that area are done. Restoring a switch plays the next cutscene. The Captain sleeps until the second switch is restored, and the Saboteurs, once scrapped, stay scrapped. Design decisions are in [Docs/DesignDoc.md](Docs/DesignDoc.md).

## Saboteur Squad and models (S3)

The Saboteur Squad is four instances (A to D) of one utility-AI brain: each action (close a door, arm a trap, steal a battery, attack, flee, search, patrol) is scored from response curves, and a thin squad layer keeps the four apart through target claims, attack saturation and one shared detour cache. A Saboteur closes the door that lengthens the player's route most, and Saboteur A drops the chapter keycard when it is scrapped. The design, the maths and the measurements are in [Docs/AI/SaboteurBot.md](Docs/AI/SaboteurBot.md) and [Docs/AIPerformanceLog.md](Docs/AIPerformanceLog.md).

Press **F3** in the Editor or in a build for the AI debug overlay; the Saboteur layer lists every action's considerations and scores, who has claimed which target, and why each candidate lost.

S3 modelled the Tracker, the Saboteur (one body, squad colour on the belt and claws), the Guard, the Captain, Unit 047 and the keycard, and made the task props (the pushable crate, belts, lever, blaster, wind-up key and the redesigned palette-plastic props). Every model is a rigid-part hierarchy exported from `Blender/` to `Assets/_Project/Models/` with the validated settings in [CONTRIBUTING.md](CONTRIBUTING.md); triangle counts, texture memory and UV layouts are in [Docs/OptimisationLog.md](Docs/OptimisationLog.md) and `Docs/Evidence/Models/`. S3's textures share one import preset (`Assets/_Project/Textures/S3_Texture_BC7.preset`: BC7, mipmaps, power of two).

### Third-party credits

Some props and the music are not the team's work. Each pack's author, licence, link and the changes made are in its README under `Assets/_ThirdParty/`; the game's credits screen shows the credits the licences ask for.

| Pack | Used for | Licence |
| --- | --- | --- |
| Kenney (Factory Kit, Blaster Kit, Platformer Kit) | Starting shapes of some task props, redesigned since | CC0 1.0 |
| Poly Pizza: iPoly3D (Cage), Quaternius (Pickup Sphere) | Switch cage, power core | CC0 1.0 |
| Poly Pizza: Tiff Eidmann (Toy robot), J-Toastie (Concrete Barrier), dook (Vending Machine), Anastasiia Ku (Low poly vending machine) | Wind-up toy, plate stop, crate dispenser, charger | CC BY 3.0 |
| Music: epb9000, MintoDog, cynicmusic, Kistol | Title, Control Room and Results music | CC0 1.0 |
| Music: Eric Matyas, www.soundimage.org | Assembly Floor, Painting Room and Storage Area music | Free with credit |

## Requirements

- Unity **6000.6.2f1** (exact version in `ProjectSettings/ProjectVersion.txt`; everyone installs the same build)
- Git + Git LFS (`git lfs install` once per machine)
- Blender **5.2.0 LTS** (only for editing sources in `Blender/`)

## Getting started

1. Clone the repo and open the folder in Unity Hub.
2. Set up Smart Merge in your local `.git/config` (see [CONTRIBUTING.md](CONTRIBUTING.md)).
3. Open `Assets/_Project/Scenes/Bootstrap.unity` and press Play. It loads `Env`, `Interactables`, `Agents` and `UI` additively and waits on the title screen; press Start.

The title screen has three buttons: **Start** (or Enter, or Space), **Credits** (bottom left) and **Quit** (bottom right). Quit stops Play mode in the Editor and closes the game in a build; the Results screen has the same Quit button.

## Controls

| Input | Action |
| --- | --- |
| W A S D / arrow keys | Move |
| Mouse | Look |
| Left Shift | Sprint (louder footsteps: agents hear them) |
| Left mouse button (or Enter) | Shoot the blaster |
| R | Reload from a spare cell |
| E | Use: levers, switches, doors, the crate dispenser, the charger, and shove a crate. Hold E on the terminal and the console |
| Q or right mouse button | Throw a wind-up toy (a lure for the Tracker) |
| Tab | Fold the task list down to the task in hand, or open it again (kept for the next run) |
| H | Hide the key hints at the bottom, or bring them back (kept for the next run) |
| M | Mute and unmute |
| Esc | Pause; in a cutscene, skip it |
| Space, E or left click | In a cutscene, finish the line and go to the next |

The pause menu has Resume, Restart from checkpoint, Restart game, and Sound and Music volume sliders (kept for the next run). Each room's start is a checkpoint (after Room 1, Room 2 and Room 3). A game has three restarts: going back to the checkpoint uses one, after being recalled or from the pause menu, and Restart game gives all three back. Recalled with none left, the run ends.

Debug keys:

| Input | Action | Where |
| --- | --- | --- |
| F3 | Toggle the AI debug overlay (routes, cover scores, predictions, noise) | Editor and every build |
| F6 / F7 / F8 | Jump to Chapter 2 / 3 / 4 | Editor and development builds |

## Building

Use **Factory Reset → Build → Windows Player**: it builds the five scenes in order (`Bootstrap` at index 0, then `Env`, `Interactables`, `Agents`, `UI`) to `Builds/Windows/FactoryReset.exe` (git-ignored) and appends a row to [Docs/BuildLog.md](Docs/BuildLog.md) with the commit, size, time, warnings and errors. A full build takes about an hour the first time (shader compilation). By hand: File → Build Profiles, the same five scenes in the same order, Windows, x86_64.

## Documentation

- [Docs/DesignDoc.md](Docs/DesignDoc.md): architecture, decisions, justifications
- [Docs/ArtBible.md](Docs/ArtBible.md): visual rules
- [Docs/AI/](Docs/AI/): one design document per agent
- [Docs/OptimisationLog.md](Docs/OptimisationLog.md) and [Docs/AIPerformanceLog.md](Docs/AIPerformanceLog.md): measured evidence
