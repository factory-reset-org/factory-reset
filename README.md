# Factory Reset

A low-poly, cartoon-styled 3D shooter set inside an abandoned toy factory whose AI has turned every toy against you. Battle four autonomous enemies, restore three control switches, and force a factory reset.

Joint project for SE3032 Graphics & Visualization and SE3062 Intelligent Systems.

## Team

| Student | GV role | IS agent | Architecture |
| --- | --- | --- | --- |
| S1 | World Builder | Tracker Toy | Greedy Best-First Search + hierarchical FSM |
| S2 | Systems Engineer | Guard Bot | Tactical A* + cover evaluation |
| S3 | Core Developer | Saboteur Bot | Utility AI with response curves |
| S4 | Agent Controller | Captain Bot | Goal prediction + Dijkstra fields + A* intercept |

## Requirements

- Unity **6000.6.2f1** (exact version in `ProjectSettings/ProjectVersion.txt`; everyone installs the same build)
- Git + Git LFS (`git lfs install` once per machine)
- Blender 4.x (only for editing sources in `Blender/`)

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
