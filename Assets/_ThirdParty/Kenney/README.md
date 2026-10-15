# Kenney task props (third-party, CC0)

Models by Kenney (www.kenney.nl), released under Creative Commons Zero (CC0 1.0,
http://creativecommons.org/publicdomain/zero/1.0/). Attribution is not required;
it is given here and in `Docs/DesignDoc.md` (decision log, 2026-10-13). Each pack's
original `License.txt` sits next to its exports.

These are not Student 3's own models. They stand in for task props that had no
model, and do not count as S3's custom models for the GV mark.

| Pack | Version | Source | Downloaded |
| --- | --- | --- | --- |
| Factory Kit | 3.0 | https://kenney.nl/assets/factory-kit | 2026-10-13 |
| Blaster Kit | 2.1 | https://kenney.nl/assets/blaster-kit | 2026-10-13 |
| Platformer Kit | 4.1 | https://kenney.nl/assets/platformer-kit | 2026-10-13 |

## Which model became which prop

| Prop (prefab in `Prefabs/Props/`) | Kenney model | Pack | Changes made |
| --- | --- | --- | --- |
| `PaintCrate_Root` | `crate-strong` | Platformer | Scaled x1.67 to 1 x 1 x 1 m |
| `Fuse_Root` | `clip-small` | Blaster | Scaled x1.28 to fit 0.2 m |
| `PressurePlate_Root` | `button-floor-square` | Factory | Fitted to 1.2 x 0.04 x 1.2 m (height x0.23); top (`PressurePlate_Top`) on its own white tint material |
| `ControlSwitch_Root` | `warning-traffic` | Factory | Scaled x0.77 to 1.2 m tall; lamp faces split into `ControlSwitch_Light` on a white tint material |
| `SpinningTarget_Root` | `target-large` | Blaster | Turned to face +Z, fitted to 1.24 m across x 0.05 m; both faces split into `SpinningTarget_Face` on its own material |
| `Terminal_Root` | `screen-small` | Factory | Fitted to 0.8 x 1.4 x 0.5 m |
| `Relay_Root` | `warning-orange` | Factory | Scaled x0.93 to 1.2 m tall; lamp faces split into `Relay_Light` on a white tint material |
| `Console_Root` | `screen-panel-wide` | Factory | Fitted to 1.2 x 1.0 x 0.6 m |
| `DoorPanel_Root` | `door-wide-closed` | Factory | Turned so the panel's thin side is X, given 0.2 m thickness (the source is a single sheet), fitted to 0.2 x 5 x 3 m |
| `Battery_Root` | `grenade-a` | Blaster | Fitted to 0.25 x 0.4 x 0.25 m |
| `Overcharge_Root` | `jewel` | Platformer | Scaled x0.94 to fit 0.35 m |
| `TrapPlate_Root` | `trap-spikes` | Platformer | Base fitted to 1 x 0.05 x 1 m on its own tint material; spikes kept as the `TrapPlate_Spikes` child |
| `BlasterModel_Root` | none since 2026-10-15 (was `blaster-a`) | n/a | Replaced by S3's own toy energy blaster, modelled from primitives in the same file and exported over the same FBX so GUIDs and prefab references stay. Same nodes: `BlasterModel` (body), `BlasterModel_Cell` (every energy part: bulb, chamber, coils, conduit, charge bars; pivot at its centre), `BlasterModel_Muzzle` (bulb tip). Not a Kenney model any more |

Every prop has its pivot at the centre of its bounds, matching the primitive it
replaces. Rotation 0 and scale 1 on every node; it faces Unity +Z. Blender sources
are in `Blender/ThirdParty/Kenney/`.
