# Art Bible

Agreed in Week 11 before anyone models. Changed only by team agreement. PRs with off-palette assets are rejected.

## Style
Smooth, shiny toy plastic: rounded, bevelled edges (no hard faceted low-poly look), bright cartoon colours, glossy plastic with soft highlights, painted metal only for small parts (joints, antennae), oversized machinery, glowing projectiles, comic hit effects. No photorealism.

Changed in Week 12 from the original low-poly style, matching the revised Guard Bot model. In practice:
- Plastic: Metallic 0, Smoothness 0.7-0.85, so highlights read as glossy toy plastic.
- Large flat surfaces (floors, walls): Smoothness about 0.6, so they read as a surface rather than a mirror.
- Silhouettes stay chunky and simple; the smoothness comes from bevels and smooth shading, not from extra detail.
- Glossy plastic depends on reflections: the lighting pass adds reflection probes so highlights show in every room.

## Palette

Colours come from the agreed browser prototype (`Factory-Reset.html`), so the Unity build matches what the team play-tested.

| Name | Hex | Role |
| --- | --- | --- |
| Tomato | `#FF5A4E` | danger, the Tracker, alarms |
| Sun | `#FFC933` | warnings, interactables, doors |
| Mint | `#3DDBB0` | Assembly Floor, safe/restored, circuits |
| Cobalt | `#3A6CF4` | the Guard, machinery |
| Bubble | `#FF5CA8` | Painting Room |
| Orange | `#FF9F1C` | Storage Area |
| Grape | `#8F6BFF` | Control Room |
| Plum | `#1E1638` | UI panels, dark trims |
| Ink | `#FFF8EC` | UI text |

## Materials (URP Lit, 8 total)

All plastic is non-metallic; the gloss comes from smoothness and reflection probes, not from metal.

| Material | Base colour | Metallic | Smoothness | Used on |
| --- | --- | --- | --- | --- |
| Red plastic | `#FF5A4E` | 0 | 0.8 | toys, the Tracker, alarm details |
| Blue plastic | `#3A6CF4` | 0 | 0.8 | the Guard, machine bodies |
| Yellow plastic | `#FFC933` | 0 | 0.8 | warning stripes, belt rails, doors, levers |
| White plastic | `#E8ECF2` | 0 | 0.6 | walls and large panels (lower smoothness so walls are not mirrors) |
| Grey plastic | `#5B6B8C` | 0 | 0.7 | machine bases, shelves, pillars, cover blocks |
| Floor plastic | room key colour | 0 | 0.6 | floors; one variant per room (see Room identity) |
| Chrome | `#B8C4D6` | 1 | 0.9 | small parts only: joints, antennae, gears, bolts |
| Glow circuit | `#3DDBB0`, emission | 0 | 0.5 | screens, lamps, circuit trims (emissive, drives bloom) |

## Polygon budgets

Raised in Week 12 for the bevelled style: smooth shading needs a bevel on every visible edge, which roughly doubles the triangles of a hard-edged part. All seven agents together stay under 30,000 triangles, which is small for any target GPU; the budgets exist to keep LOD and optimisation evidence honest, not because the scene is triangle-bound.

| Asset | Triangles | Was |
| --- | --- | --- |
| Tracker Toy | < 3,000 | < 2,000 |
| Saboteur Bot (one mesh, four tints) | < 4,000 | < 3,000 |
| Guard Bot | < 4,000 | none |
| Captain Bot | < 4,500 | none |
| Unit 047 | < 3,500 | < 2,500 |
| Each task prop | < 800 | < 600 |
| Energy Blaster / Battery Station (optional) | < 800 | < 500 |

Modelling rules for the style: bevel every visible edge (2 segments for parts larger than 0.3 m, 1 segment below that), use smooth or weighted normals, keep silhouettes chunky. Record each model's count in `OptimisationLog.md`.

## Textures
Power-of-two, BC7. Props atlas 1024×1024. Plastic needs no detail textures: flat base colours plus a smoothness value; decals (stickers, the 047 tag) go on the props atlas.

## Lighting and post-processing
Baked GI + 2-3 real-time point lights. Bloom, colour grading (warm factory), vignette, exponential fog. For glossy plastic:
- One baked reflection probe per room, box projection on, so highlights show the room's own colours.
- Screen-space ambient occlusion at low intensity to ground objects; soft shadows.
- Bloom threshold above 1, so only emissives glow and plastic highlights do not glare.

As built in `Env.unity` (S1, Week 12):
- Materials are `Materials/Environment/M_Env_*`. Floors use a lighter tint of the room key colour (Assembly `#7FE3C6`, Painting `#FF9CC8`, Storage `#FFC170`, Control `#A592FF`); the full key colour over a 20 m floor drowns the props.
- A ninth material, `M_Env_GlowAlarm` (red `#FF5A4E` emission), is used on the Control Room alarm beacons. It is left out of the bake so the lighting state can turn it amber at runtime.
- Lights, under `Lighting` (relit in Week 13 for the ceiling, as in the prototype):
  - Every room is closed by a dark plum ceiling at 6 m (`M_Env_Ceiling`, `#2B2450`) with nine glowing panels (`M_Env_CeilingPanel`, warm white `#FFF3D6`, emission x2.2).
  - Under each panel is a baked rectangle area light, 2.4 x 0.8 m (`CeilingLights_Baked`, 36 lights). They are tinted with the room colour: Assembly `#D8FFF1`, Painting `#FFE1EF`, Storage `#FFE6C4`, Control `#B8A8FF`. Intensity is 32, except Control at 14, which keeps the final room cold and dim.
  - The sun and the four point fills are switched off: the ceiling would block the sun anyway, and the panels now light the rooms the way the prototype does. Painting keeps its baked pink accent.
  - Three real-time lights remain: the orange lamp in Storage and the two red Control Room door alarms.
  - Lighting settings are in `Settings/Lighting/LS_Env`.
- Ambient is Trilight, but with the ceiling closed it no longer reaches inside the rooms; the panels and their bounce light do the work. Fog is exponential, density 0.006.
- 629 light probes: a 3 m grid at 0.5, 2 and 4 m, six probes at each doorway, and 118 more along the agents' patrol routes and the player's route through the four doorways (pairs at 0.75 m and 2 m wherever a route point was more than 1.25 m from a probe).
- Post-processing is one global Volume (`Settings/PostProcessing/PP_Factory`):
  - Bloom: threshold 1.1, intensity 0.6.
  - Neutral tonemapping. ACES was not used because it greys out the pastels.
  - Colour adjustments: exposure -0.2, contrast +12, saturation +15.
  - White balance: +8 warm.
  - Vignette: 0.25, Plum colour.
- A camera only shows the grade if its URP camera data has Post Processing ticked.

## Room identity

| Room | Key colour | Mood |
| --- | --- | --- |
| Assembly Floor | Mint `#3DDBB0` | bright and busy: moving belts, the first, safest room |
| Painting Room | Bubble `#FF5CA8` | colourful and exposed: paint spatter, open sightlines |
| Storage Area | Orange `#FF9F1C` | warm and cramped: tall shelves, shadows between aisles |
| Control Room | Grape `#8F6BFF` | cold and ominous: screens, red alarm beacons until unlocked |

## Saboteur squad tints

One Saboteur material, tinted per unit with a MaterialPropertyBlock (S3's `SaboteurTint`).

| Unit | Colour | Hex |
| --- | --- | --- |
| A | Purple | `#8F6BFF` |
| B | Teal | `#2FB894` |
| C | Orange | `#FF8A1C` |
| D | Pink | `#E0409A` |
