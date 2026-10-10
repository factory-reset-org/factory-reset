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
| Grey plastic | `#5B6B8C` | 0 | 0.7 | machine bases, paint-tank lids |
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

The environment has three small tiling textures from the prototype, each 256 x 256, BC7, mipmapped, trilinear, anisotropic 8, in `Textures/Environment`:
- `T_Env_HazardStripes`: yellow and plum 45 degree bands, four per metre, seamless both ways. Used on the wall skirting and the press outlines (`M_Env_HazardStripe`).
- `T_Env_FloorTiles`: a 2 m tile of 4 x 4 grey tiles with darker grout, multiplied by each room's floor tint (`M_Env_FloorTiled_<Room>`, tiling 10.375 over a 20.75 m floor).
- `T_Env_Bullseye`: the prototype's red, cream, blue and yellow rings with a plum rim, on the Painting Room targets (`M_Env_Bullseye`, a little emission so they read from across the room; a hit tints them green through S2's `PropTint`).
- `T_Env_ServerLights`: rows of small mint, yellow, cyan and pink LEDs on black, the emission map of the Control Room's server pillars (`M_Env_ServerPillar`, dark `#151228` plastic, caps in `M_Env_PlasticPurple` `#8F6BFF`).
- `T_Env_FloorGlowGrid`: thin outlines inset in each tile, used as the Control Room floor's emission map (cyan `#3DD9FF`).

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
- The plain `M_Env_Floor_<Room>` tints are now used by the dressing (toys, puddles, rack trims); the floors themselves use the tiled `M_Env_FloorTiled_<Room>` copies, so the tile texture never lands on a toy.
- Wall trims, as in the prototype: a 0.6 m hazard-stripe skirting along the foot of every room wall (broken at the doorways), and a 0.3 m dark plum band under the ceiling.
- Rounded edges (Week 13): every environment box is a true rounded box (`RoundedBoxMesh`), replacing the single 45 degree chamfer, which read as a hard edge from more than a couple of metres. Radius: a quarter of the smallest side, at most 0.25 m (presses, pillars 0.25 m; lintels 0.125 m; shelves 0.08 m), at most 0.08 m on walls; toys 0.075 to 0.1 m. Smooth normals across the rounding catch a soft highlight, which is what reads as moulded plastic.
- Prototype rooms (Week 13): paint tanks are smooth lathed meshes (rounded rims, 320 to 480 triangles per part) in white plastic with a coloured band; presses are rounded boxes in the toy colours with a chrome piston. The alarm beacons' materials (`M_Env_GlowAlarm`, `M_Env_GlowAlarmOff`, `M_Env_GlowAmber`) had lost their `_EMISSION` keyword on save and rendered as dull red; they now keep it (Realtime Emissive, since they swap at runtime), and the alarm red is saturated so it reads as red, not pink.
- Assembly Floor revamp (Week 13, the final look of room 1): everything is still palette plastic. New materials, all URP Lit and non-metallic: `M_Env_PlasticMint` (`#3DDBB0`, 0.8), `M_Env_PlasticOrange` (`#FF9F1C`, 0.8), `M_Env_PlasticBubble` (`#FF5CA8`, 0.8) and `M_Env_PlasticPlum` (`#1E1638`, 0.7) for trims, toys, bins and boxes; `M_Env_Signs` and `M_Env_SignsGlow`, which share one 1024 x 1024 sign atlas (`T_Env_Signs_Assembly`: the room sign, door plaques, three posters, the clock face, the machine label and screen, the line status screen, a pegboard and locker numbers, painted in code by `SignPainter` in palette colours, with a faint emission so they read in the bake, and a strong one on screens); and `M_Env_Window` with `T_Env_WindowSky` (256 x 256, a daylight sky over distant sheds), emissive, so the windows light the room. Small spheres are 12 x 7 lathes (about 170 triangles) and thin rods have 8 sides, so the room's many small parts stay cheap. The Assembly floor tiles are 1 m (S2's request, the crate size); the other rooms keep 0.5 m.
- Painting Room revamp (Week 13): its own sign atlas (`T_Env_Signs_Painting`, materials `M_Env_SignsPainting` and `M_Env_SignsPaintingGlow`) with the room sign, door plaques, three posters, a colour-wheel mural, the mixer label, a gauge face and a paint-levels screen, all in palette colours. Paint splats are flat generated meshes with a rounded rim in the palette plastics (Grape and Mint replace the floor tints the old puddles used). The tanks gain a footed base, steel hoops, a sight glass and a ladder in the existing plastics. Each room now has its own look: square windows, corner columns, a mint rail and wall pipes in the Assembly Floor; round portholes, a three-colour stripe, paint drips and ceiling paint lines in the Painting Room. No room has hanging lamps; the ceiling panels light the rooms.
- Storage Area revamp (Week 13): its own sign atlas (`T_Env_Signs_Storage`, materials `M_Env_SignsStorage` and `M_Env_SignsStorageGlow`) with the room sign, door plaques, relay plaques, aisle signs, shelf labels, the dock sign, three posters, the relay board's header, step badges, arrow and instruction, and the glowing relay map. One new plastic, `M_Env_RelayGreen` (`#34C759`), so the green relay's floor ring matches its colour; red and blue use the palette's red and blue plastics. The Storage Area reads as a warehouse: high strip windows, an orange band, steel roof beams with aisle signs, a roller-shutter dock and yellow aisle lines. The shelves hold toy shapes (teddies, robots, ducks, taped boxes) in mixed palette colours instead of blocks; blue is left out of the mix so nothing disappears against the blue shelving.
- Room dressing (Week 13) adds no new materials. Toys reuse the red, yellow and blue plastics and the four floor tints; puddles reuse the plastics, whose 0.8 smoothness reads as wet paint; server racks are the ceiling plum with Control trims; server lights reuse `M_Env_GlowCircuit`; gears are yellow, red, blue or chrome. Small or moving pieces (toys, gears, server lights) are lit by light probes instead of lightmaps.
- Ambient is Trilight, but with the ceiling closed it no longer reaches inside the rooms; the panels and their bounce light do the work. Fog is exponential, density 0.006, in dark plum `#2B2450` like the prototype (it was pale lavender, which looked like haze under a ceiling).
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

## Character materials (S3)

The agents, Unit 047 and the keycard use 17 shared plastics in `Materials/Characters/` (`M_Char_*`), built by `Factory Reset > Materials > Build Character Materials` from the colours of each character's reference sheet. All are URP Lit, Metallic 0 and Smoothness 0.7 to 0.9; no part uses chrome, because a metal part reads black without reflection probes. The FBX files keep the three colour zones they were exported with; the builder sets each part's materials on the character prefab variants.

| Model | Light zone (main body) | Mid zone (secondary) | Dark zone (joints, tyres, frames) | Parts that differ |
| --- | --- | --- | --- | --- |
| Tracker Toy | Red `#FF5A4E` | Cream `#FFF1D6` | Black `#1D1A2A` | gold key, black antenna stem, pink inside the ears, cream snout with black nose |
| Saboteur Bot | Purple `#8F6BFF`, tinted per unit | Pink `#FF5CA8` | Black | steel arms, joints and neck, pale goggle lenses |
| Guard Bot | Blue `#3A6CF4` | Yellow `#FFC933` | Black | navy hip, steel cannon with a red ring, cyan visor, grey head vents, treads with the tread texture |
| Captain Bot | White `#F4F1FF` | Gold `#FFC933` | Black | navy legs and hat band, steel cannons with a pink ring, blue visor, gold shoulders |
| Unit 047 | Cyan `#62D8FF` (its speaker colour) | White | Navy `#22306A` | gold key, red sticker, grey legs, emissive cyan eyes |
| Keycard | Yellow | Steel | Black | none |

- **Saboteur tint.** Only the body's first slot carries `M_Char_SaboteurBody`; `SaboteurTint` overrides its base colour per squad letter, and no other slot uses that material, so nothing else is tinted.
- **Guard treads.** The belt body has its own material, `M_Char_GuardTread`, with a 128 x 128 seamless texture (BC7, repeat). The belt's UVs run along its length (U along the body's Z), so S4 scrolls the texture's U offset with the Guard's speed.
- **Unit 047's eyes.** `M_Char_Unit047Eyes` has emission on (cyan, HDR intensity 2), so the keyword survives builds; S4 animates `_EmissionColor` down to black for "off".
- **No overlapping faces.** Faces of two zones must never lie in the same plane and overlap: with two colours they flicker. The Captain's torso, arms and hat band and the Guard's cannon arm, shield arm and treads had such faces (flush details laid over a larger face); they are lifted 2 mm in the `.blend` files. `CharacterMaterialTests` checks every model.

## Saboteur squad tints

One Saboteur material, tinted per unit with a MaterialPropertyBlock (S3's `SaboteurTint`).

| Unit | Colour | Hex |
| --- | --- | --- |
| A | Purple | `#8F6BFF` |
| B | Teal | `#2FB894` |
| C | Orange | `#FF8A1C` |
| D | Pink | `#E0409A` |
