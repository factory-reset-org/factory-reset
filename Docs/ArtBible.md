# Art Bible

Agreed in Week 11 before anyone models. Changed only by team agreement. PRs with off-palette assets are rejected.

## Style
Smooth, shiny toy plastic: rounded, bevelled edges (no hard faceted low-poly look), bright cartoon colours, glossy plastic with soft highlights, painted metal only for small parts (joints, antennae), oversized machinery, glowing projectiles, comic hit effects. No photorealism.

Changed in Week 12 from the original low-poly style, matching the revised Guard Bot model. In practice:
- Plastic: Metallic 0, Smoothness 0.7-0.85, so highlights read as glossy toy plastic.
- Large flat surfaces (floors, walls): Smoothness about 0.6, so they read as a surface rather than a mirror.
- Silhouettes stay chunky and simple; the smoothness comes from bevels and smooth shading, not from extra detail.
- Glossy plastic depends on reflections: the lighting pass adds reflection probes so highlights show in every room.

## Materials (URP Lit, 6-8 total)

| Material | Base colour | Metallic | Smoothness | Used on |
| --- | --- | --- | --- | --- |
| Red plastic | | | | |
| Blue metal | | | | |
| Yellow warning | | | | |
| Grey concrete | | | | |
| Green circuit | | | | |
| Orange rust | | | | |

## Polygon budgets

| Asset | Triangles |
| --- | --- |
| Tracker Toy | < 2000 |
| Saboteur Bot | < 3000 |
| Energy Blaster / Battery Station (optional) | < 500 |

## Textures
Power-of-two, BC7. Props atlas 1024×1024.

## Lighting and post-processing
Baked GI + 2-3 real-time point lights. Bloom, colour grading (warm factory), vignette, exponential fog.

## Room identity

| Room | Key colour | Mood |
| --- | --- | --- |
| Assembly Floor | | |
| Painting Room | | |
| Storage Area | | |
| Control Room | | |
