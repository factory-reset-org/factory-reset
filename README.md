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

## Requirements

- Unity **6000.6.2f1** (exact version in `ProjectSettings/ProjectVersion.txt`; everyone installs the same build)
- Git + Git LFS (`git lfs install` once per machine)
- Blender **5.2.0 LTS** (only for editing sources in `Blender/`)

## Getting started

1. Clone the repo and open the folder in Unity Hub.
2. Set up Smart Merge in your local `.git/config` (see [CONTRIBUTING.md](CONTRIBUTING.md)).
3. Open `Assets/_Project/Scenes/Bootstrap.unity` and press Play. It loads `Env`, `Interactables` and `Agents` additively.

## Controls

| Input | Action |
| --- | --- |
| _TBD_ | |
| F1 | Toggle AI debug overlay |

## Building

File → Build Settings: `Bootstrap` (index 0), `Env`, `Interactables`, `Agents`. Build to `Builds/` (git-ignored).

## Documentation

- [Docs/DesignDoc.md](Docs/DesignDoc.md): architecture, decisions, justifications
- [Docs/ArtBible.md](Docs/ArtBible.md): visual rules
- [Docs/AI/](Docs/AI/): one design document per agent
- [Docs/OptimisationLog.md](Docs/OptimisationLog.md) and [Docs/AIPerformanceLog.md](Docs/AIPerformanceLog.md): measured evidence
