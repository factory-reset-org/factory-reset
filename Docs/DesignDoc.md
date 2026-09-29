# Design Document

Architecture, decisions and justifications. Every decision needs a "because" you can say out loud in the viva.

## 1. Architecture overview
<!-- Layers: AI Core → Agent Brains → Runtime adapters → Execution → Presentation -->

## 2. Brain / body contract
<!-- AgentContext, AgentIntent, IAgentBrain: final signatures once merged -->

## 3. Search contracts
<!-- ICostModel, PathResult, IPathfinder -->

## 4. Grid
<!-- Cell size, connectivity, GridChange events, Version -->

## 5. Scenes and loading order

## 6. Event flow

## 7. Decision log

| Date | Decision | Alternatives considered | Because | Owner |
| --- | --- | --- | --- | --- |
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
