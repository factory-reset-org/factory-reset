# Poly Pizza task props (third-party, CC0 and CC BY 3.0)

Models downloaded from Poly Pizza (https://poly.pizza) on 2026-10-14. Three are
public domain (CC0 1.0, https://creativecommons.org/publicdomain/zero/1.0/). Four are
under Creative Commons Attribution 3.0 (https://creativecommons.org/licenses/by/3.0/),
which requires the credit below. They are also recorded in `Docs/DesignDoc.md`
(decision log, 2026-10-14).

These are not Student 3's own models. They stand in for task props that had no
model, and do not count as S3's custom models for the GV mark.

## Credits

| Prop (prefab in `Prefabs/Props/`) | Model | Author | Licence | Source |
| --- | --- | --- | --- | --- |
| `SwitchCage_Root` | Cage | iPoly3D | CC0 1.0 | https://poly.pizza/m/TAqDCvxcxd |
| `PowerCore_Root` | Pickup Sphere | Quaternius | CC0 1.0 | https://poly.pizza/m/1xtAc12dmv |
| `WindUpToyModel_Root` (body) | toy robot | Tiff Eidmann | CC BY 3.0 | https://poly.pizza/m/5tzTpu3fNHs |
| `PlateStop_Root` | Concrete Barrier | J-Toastie | CC BY 3.0 | https://poly.pizza/m/5Cg2oW7EFn |
| `CrateDispenser_Root` | Vending Machine | dook | CC BY 3.0 | https://poly.pizza/m/yNG4vqWVq0 |
| `Charger_Root` | Low poly vending machine | Anastasiia Ku | CC BY 3.0 | https://poly.pizza/m/aZcNr5-opYI |

## Changes made

All models were changed (CC BY 3.0 asks for changes to be indicated):

- Scaled to S2's prop sizes, pivot at the centre of the bounds, facing Unity +Z, rotation 0 and scale 1 on every node.
- Parts joined into one mesh per prop; original colours replaced by the nearest
  shared palette plastic (`Materials/Characters/M_Char_*`).
- `SwitchCage`: fitted to 1.2 x 1.6 x 1.2 m.
- `PowerCore`: the red sphere split into `PowerCore_Core` (`M_Prop_PowerCore`, 0.64 m),
  the grey clamps scaled to 0.98 m across so they sit inside the shield, and a 1.04 m
  copy of the sphere added as `PowerCore_Shield` (`M_Prop_PowerShield`, transparent).
- `PlateStop`: fitted to 1.5 x 0.5 x 0.2 m and set to the yellow plastic (belt rails).
- `CrateDispenser`: turned to face +Z and fitted to 1.2 x 1.6 x 0.4 m (depth about a third of the original's proportion).
- `Charger`: fitted to 0.8 x 1.4 x 0.5 m. `Charger_Far` is a second version of the same mesh with half the triangles (Blender Decimate, collapse, ratio 0.5), used as level 1 of the prefab's LOD group; it is a derivative of the same model and carries the same credit.
- `WindUpToyModel`: the robot fitted to 0.2 m tall with its pivot at the base
  (`WindUpToy_Model`). Its `WindUpToy_Key` child is not from Poly Pizza: it is a
  butterfly wind-up key modelled by S3 (264 triangles), set into the robot's back
  with its pivot on the shaft axis, so it turns about its own local Z.

Blender sources are in `Blender/ThirdParty/PolyPizza/`.

## Redesign (2026-10-15)

All six props were redesigned to match the Art Bible's smooth toy-plastic style. Each
was rebuilt inside its existing `.blend` and exported over the same FBX, so GUIDs,
pivots and prefab references stay. The original geometry was replaced by bevelled,
smooth-shaded parts in the shared palette plastics; the credits above stay because
the props started from these models.

| Prop | Redesign | Triangles after Unity import (before, after) | Node changes |
| --- | --- | --- | --- |
| `SwitchCage_Root` | A see-through glass box (`M_Prop_Glass`) in a grey and steel frame, with a gold padlock on the front | 2,240, 768 | The glass is the `SwitchCage` mesh; the frame is a new `SwitchCage_Frame` child |
| `PowerCore_Root` | An 0.8 m faceted red crystal, two gold orbit rings, the 1.04 m transparent shield | 2,112, 752 | `PowerCore` is now an empty parent of `PowerCore_Core`, `PowerCore_Ring` (new) and `PowerCore_Shield`, so the core is the first renderer `PowerCore` finds and flashes |
| `WindUpToyModel_Root` | A yellow wind-up duck with a red beak and feet, and a red butterfly key in its tail | 2,309, 792 | `WindUpToy_Key` moved to the tail (0, 0.09, -0.13 in Unity); it still spins about its own local Z |
| `PlateStop_Root` | Grey foot, rounded yellow bumper with black hazard stripes on both faces, steel bolts | 166, 728 | none |
| `CrateDispenser_Root` | Blue dispenser with a gold hopper, a chute mouth and a red call button, a ramp, and a yellow-rimmed floor pad under S2's `Outlet` 1.2 m out | 1,851, 780 | none. The chute faces Unity -Z, toward the `Outlet` |
| `Charger_Root` | A small Tesla tower: blue plinth, white column in a gold spiral coil, steel top ring, glowing cyan orb (`M_Prop_Glow`) | 2,212, 778 (`Charger_Far` 1,106, 392) | none. `Charger_Far` is the same build at half the segments, not a decimation |
