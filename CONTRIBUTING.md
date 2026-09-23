# Contributing to Factory Reset

Both modules grade each student's own Git history. Follow these rules from day one.

## One-time setup per machine

```bash
git lfs install
git config user.name "Your Full Name"
git config user.email "verified-email@your-github-account"   # unverified = unlinked commits
```

Add Unity Smart Merge to **this repo's** `.git/config` (fix the path to your Unity version):

```ini
[merge "unityyamlmerge"]
    name = Unity Smart Merge
    driver = 'C:/Program Files/Unity/Hub/Editor/2022.3.XXf1/Editor/Data/Tools/UnityYAMLMerge.exe' merge -h -p --force --fallback none %O %B %A %A
```

## Branches

```text
main   stable, demo-ready, tagged releases only
dev    integration, merged into daily
  s<N>/<type>/<topic>   e.g. s1/feat/gbfs-search, s3/art/tracker-toy-uv
```

- Short-lived task branches off `dev`, merged back within 1-3 days.
- Pull requests only. No direct pushes to `main` or `dev`. At least one reviewer.
- **Merge commits** (`--no-ff`), never squash. Squashing erases individual authorship.
- Put `Closes #<issue>` in the PR description so the project board updates.

## Commits

Conventional Commits, at least one meaningful commit per working day:

```text
feat(tracker): add closed set to GBFS
fix(guard): release cover reservation on stun
test(core): BinaryHeap decrease-key cases
art(saboteur): UV unwrap arms
docs(captain): justify beta = 0.5
perf(scheduler): cap searches at 2 ms per frame
refactor(runtime): inject blackboard at spawn
```

Check your weekly count: `git shortlog -sn --since="1 week ago"`.

## Architecture rules

- AI logic lives in `Scripts/AI` (pure C#). Brains never touch GameObjects.
- Assemblies and what they may reference:

  | Assembly | References |
  | --- | --- |
  | `ToyFactory.AI.Core` | nothing (engine types only) |
  | `ToyFactory.AI.Agents` | AI.Core |
  | `ToyFactory.Interfaces` | nothing |
  | `ToyFactory.Runtime` | AI.Core, AI.Agents, Interfaces, AI Navigation |
  | `ToyFactory.Tests.EditMode` | AI.Core, AI.Agents, test framework |

- `Player/`, `Interaction/` and `Managers/` compile into Unity's default assembly. They can use every assembly above, but Runtime cannot see them, so Runtime talks to them only through `Interfaces/`.
- Physics and NavMesh queries (Linecast, SamplePosition) belong in Runtime, behind an interface the brain receives. Tests use a fake.
- Namespaces follow the assembly name. Use `ToyFactory.Runtime.Diagnostics` for code in `Runtime/Debug/`: a namespace ending in `.Debug` hides `UnityEngine.Debug`.
- No `FindObjectOfType` or `GetComponent` in `Update`. Inject dependencies at spawn. Every brain handles "player missing, dead or unreachable".

## Scenes

| Scene | Owner |
| --- | --- |
| `Bootstrap` | S2 |
| `Env` | S1 (only scene with static geometry; bake lighting with Env alone loaded) |
| `Interactables` | S2 (everything non-static, lit by Light Probes) |
| `Agents` | S4 |
| `ModelShowcase` | S3 (not in build) |

Only the owner edits a scene. Everyone else works in a personal test scene and hands over prefabs.

## Art pipeline

- Blender sources go in `Blender/` (outside `Assets/`, so Unity never tries to import `.blend` files).
- Export FBX to `Assets/_Project/Models/<Model>/`: Apply Transform, Forward -Z, Up Y, Leaf Bones off.
- Textures power-of-two, BC7. Follow [Docs/ArtBible.md](Docs/ArtBible.md).
- Only S1 commits lighting and NavMesh bake output, and only after major changes (LFS quota).

## Before opening a PR

- EditMode tests pass; paste the summary into the PR.
- `.meta` files are committed with their assets.
- The game plays from `Bootstrap` without console errors.
