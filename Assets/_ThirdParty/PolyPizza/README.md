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
| `WindUpToyModel_Root` (key) | Key | Quaternius | CC0 1.0 | https://poly.pizza/m/rfOW44y0sL |
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
- `Charger`: fitted to 0.8 x 1.4 x 0.5 m.
- `WindUpToyModel`: the robot fitted to 0.2 m tall with its pivot at the base
  (`WindUpToy_Model`), and the key turned, scaled to 0.11 m and set into its back as
  the `WindUpToy_Key` child, its pivot on the shaft axis.

Blender sources are in `Blender/ThirdParty/PolyPizza/`.
