# Contributing to Factory Reset

Both modules grade each student's own Git history. Follow these rules from day one.

## One-time setup per machine

```bash
git lfs install
git config user.name "Your Full Name"
git config user.email "verified-email@your-github-account"   # unverified = unlinked commits
```

Add Unity Smart Merge to **this repo's** `.git/config` (fix the path to your Unity version). `.gitattributes` routes scenes, prefabs, materials, assets and Timeline `.playable` files through it:

```ini
[merge "unityyamlmerge"]
    name = Unity Smart Merge
    driver = 'C:/Program Files/Unity/Hub/Editor/6000.6.2f1/Editor/Data/Tools/UnityYAMLMerge.exe' merge -h -p --force --fallback none %O %B %A %A
```

## Branches

```text
main      stable, demo-ready, tagged releases only
develop   integration, merged into daily
  s<N>/<type>/<topic>   e.g. s1/feat/gbfs-search, s3/art/tracker-toy-uv
```

- Raise a change that belongs to another student's area with its owner directly (not as a GitHub Issue), say exactly what you need and why, and let the owner make it, even for one line. Interface changes need all four reviewers.
- Short-lived task branches off `develop`, merged back within 1-3 days.
- Pull requests only. No direct pushes to `main` or `develop`. At least one reviewer.
- **Merge commits** (`--no-ff`), never squash. Squashing erases individual authorship.

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
  | `ToyFactory.UI` | Interfaces, TextMeshPro, uGUI (never AI.Agents or Runtime) |
  | `ToyFactory.Tests.EditMode` | AI.Core, AI.Agents, test framework |

- `ToyFactory.Journey` (chapters, cutscenes) is planned. It is created only once its owner is agreed, and it will never reference AI.Agents. The UI assembly reads Journey events only after that exists.
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
| `UI` | S3 (HUD, subtitles, chapter card, results; added to Build Settings and loaded by S2) |
| `ModelShowcase` | S3 (not in build) |

Only the owner edits a scene, including its metadata. Everyone else works in a personal test scene and hands over prefabs. Test scenes live under `Scenes/Test/` and stay out of the build.

## Art pipeline

- Blender sources go in `Blender/` (outside `Assets/`, so Unity never tries to import `.blend` files).
- Export FBX to `Assets/_Project/Models/<Model>/`, overwriting the existing file so its `.meta` GUID is kept: Forward Y, Up Z, Apply Transform off, Apply Scalings "FBX Units Scale", Leaf Bones off, Object Types Empty and Mesh only, Bake Animation off. Keep **Bake Axis Conversion** enabled in the Unity model importer. This is the validated pipeline: models authored facing Blender +Y import facing Unity +Z, and every node in the rigid-part hierarchy keeps rotation 0 and scale 1.
- Textures power-of-two, BC7, through a shared texture import preset rather than one-off settings. The prop atlas is 1024 x 1024. Follow [Docs/ArtBible.md](Docs/ArtBible.md); S1 owns palette changes.
- Only S1 commits lighting and NavMesh bake output, and only after major changes (LFS quota).

### Model handoff

S3 owns everything under `Assets/_Project/Models/`, the character prefab variants in `Prefabs/Characters/`, and character and prop materials. Model ownership does not cover a brain, animation clips or placement in another student's scene.

- **Hierarchy contract:** the node names, parents and pivots in [Docs/DesignDoc.md](Docs/DesignDoc.md) section 8 are frozen once S4 accepts them. Never rename, reparent, move or re-rotate a contracted node. Final modelling may only add non-animated detail meshes beneath existing nodes. A new rigged or animated asset (for example Unit 047) needs its own section 8-style contract, agreed with S4 before S4 animates it.
- **Reimporting:** the importer generates file IDs from node names, so renaming or reparenting a node orphans prefab-variant overrides such as the disabled colliders. Overwrite the FBX in place, keep its `.meta`, then check the prefab variants for missing references and console errors.
- **Budgets:** check triangle counts after import, not in Blender (Tracker < 2,000, Saboteur < 3,000, Unit 047 < 2,500, each task prop < 600). Keep a final model inside its greybox silhouette so S4's capsule sizes and S1's NavMesh stay valid.
- **Characters:** one disabled primitive collider per mesh part and no Rigidbody; S4 enables them at fall-apart. Saboteur colour variants share one material and are tinted per instance; where the tint component's script lives is agreed with S4.
- **Handing over:** the PR says what the asset is for, how the import was validated (triangles, +Z facing, identity transforms, pivots, UVs, colliders), and what the scene owner has to do. Tell S4 after any reimport so animation bindings are rechecked. Props are handed to S2 as prefabs; S2 places them in `Interactables`.
- **Evidence:** record actual before/after numbers in the modelling section of `Docs/OptimisationLog.md` (poly stats, atlas, BC7 memory, LOD, tint approach), with machine and scene. Never record a number that was not measured.
- **Branches and commits:** `s3/art/<model>` and `art(<model>): ...`. Keep a model's `.blend`, FBX, prefab, materials and `.meta` files in one commit. Use `perf(models)` only with measured evidence.

### ModelShowcase

- `Scenes/ModelShowcase.unity` is S3's presentation scene (turntable and wireframe views). It stays out of Build Settings.
- Add every S3 model to it, including Unit 047 once it exists. Its turntable must not depend on S4's animation clips.
- Open it fresh in Unity at 16:9 for a clean lit check. Do not bake lighting or NavMesh from it.
- The models keep a neutral rotation so the line-up matches DesignDoc section 8.9.

## Before opening a PR

- EditMode tests pass; paste the summary into the PR.
- `.meta` files are committed with their assets.
- Art PRs: triangle counts after import and the validation notes from Model handoff are in the description; `.blend`, `.fbx` and textures are tracked by Git LFS.
- Docs that describe a changed behaviour or decision are updated in the same PR.
- The game plays from `Bootstrap` without console errors.
