# Optimisation Log (GV evidence)

Real numbers only. Baseline as soon as one room is playable, then one entry per technique. With SRP Batcher on, report SetPass calls and Batches.

| Date | Owner | Technique | Metric | Before | After | Evidence (screenshot) | Notes / honest limits |
| --- | --- | --- | --- | --- | --- | --- | --- |
| 2026-10-02 | S3 | Final TrackerToy model within budget | Triangles after Unity import (budget < 2,000) | 1,040 (greybox) | 1,816 | Import check in Unity 6000.6.2f1: 31 nodes, identity transforms, UVs on all 19 meshes, one shared 0-1 UV layout (70.7% coverage, no overlaps) | Not an optimisation: detail was added to reach the final look. Recorded as the modelling baseline for later LOD and atlas entries. Colours are still greybox until the Art Bible palette exists. |
| 2026-10-02 | S3 | Final SaboteurBot model within budget | Triangles after Unity import (budget < 3,000) | 800 (greybox) | 2,488 | Import check in Unity 6000.6.2f1: 34 nodes, identity transforms, UVs on all 21 meshes, one shared 0-1 UV layout (66.2% coverage, no overlaps at 1024 x 1024) | Not an optimisation: detail was added to reach the final look. Every part stays inside its greybox bounds, so the 21 prefab colliders are unchanged. Colours and the A-D tints are still to come (B5). |
