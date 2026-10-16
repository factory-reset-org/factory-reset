# Physics layers and the collision matrix

Which layers collide with which, as set in **Edit > Project Settings > Physics > Layer Collision Matrix**. The table below is read from `ProjectSettings/DynamicsManager.asset`, so it is the real setting, not a description of an intention. A tick means the two layers collide (and raycasts with that mask see each other); a dash means they pass through each other.

## Layers

| # | Layer | What is on it |
| --- | --- | --- |
| 0 | Default | Anything nobody gave a layer: scene dressing and level pieces |
| 6 | Player | The player's character controller |
| 7 | Environment | Floors, walls, ceilings, static room furniture |
| 8 | Pushable | Pushable boxes and crates |
| 9 | HeldItem | A box or toy while the player carries it (kinematic) |
| 10 | Throwable | Thrown wind-up toys |
| 11 | Agents | The four robot types |
| 12 | Debris | Pieces thrown out of an exploding power core |
| 13 | Tracer | Shot bolts and impact bursts (look only, never collide) |
| 14 | TaskProp | Consoles, relays, switches, the lever, cages: what tasks are made of |
| 15 | Pickup | Battery and overcharge pickups (trigger zones) |
| 16 | ViewModel | The gun drawn by the overlay camera (no colliders) |

Layers 1 to 5 are Unity's built-in ones (TransparentFX, Ignore Raycast, Water, UI) and are not used by gameplay. Layers 6 to 15 were added with the first systems, and 16 (`ViewModel`) with the blaster's overlay camera.

## Matrix

| | Def | Ply | Env | Psh | Hld | Thr | Agt | Dbr | Trc | Tsk | Pck | VMd |
| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |
| **Def** Default | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ | – | ✓ | – | ✓ |
| **Ply** Player | ✓ | ✓ | ✓ | ✓ | – | ✓ | ✓ | – | – | ✓ | ✓ | ✓ |
| **Env** Environment | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ | – | ✓ | – | ✓ |
| **Psh** Pushable | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ | – | ✓ | – | ✓ |
| **Hld** HeldItem | ✓ | – | ✓ | ✓ | ✓ | ✓ | ✓ | – | – | ✓ | – | ✓ |
| **Thr** Throwable | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ | – | – | ✓ | – | ✓ |
| **Agt** Agents | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ | – | – | ✓ | – | ✓ |
| **Dbr** Debris | ✓ | – | ✓ | ✓ | – | – | – | ✓ | – | – | – | – |
| **Trc** Tracer | – | – | – | – | – | – | – | – | – | – | – | – |
| **Tsk** TaskProp | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ | – | – | ✓ | – | ✓ |
| **Pck** Pickup | – | ✓ | – | – | – | – | – | – | – | – | – | – |
| **VMd** ViewModel | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ | – | – | ✓ | – | ✓ |

Short names: Def = Default, Ply = Player, Env = Environment, Psh = Pushable, Hld = HeldItem, Thr = Throwable, Agt = Agents, Dbr = Debris, Trc = Tracer, Tsk = TaskProp, Pck = Pickup, VMd = ViewModel.

## Why the matrix looks like this

- **Tracer collides with nothing.** Bolts and impact bursts are only the picture of a hitscan shot; the hit was decided by the raycast the moment the shot was fired.
- **Pickup collides with Player only.** Battery and overcharge pickups are trigger zones; nothing else should be able to take one, so agents, boxes and toys pass through them.
- **Debris collides with the level and boxes only.** Core debris falls and settles on Default, Environment and Pushable, and ignores the player, the agents and toys so a burst never trips or blocks anyone.
- **HeldItem does not collide with Player.** A carried box or toy sits in front of the player's controller, and without this it would push the player back every frame.
- **Player and Throwable collide**, so a toy bounces off the player if thrown at their feet; the thrower ignores the pair explicitly while a toy is leaving the hand.
- **Shot masks.** The blaster's hit mask is Default, Environment, Pushable, Agents, TaskProp and Throwable. It leaves out Player, HeldItem, Debris, Tracer and Pickup on purpose, so a shot is never stopped by a pickup or by the player's own held box.

## Screenshot

The matrix in the same triangle layout as Unity's Layer Collision Matrix (Edit > Project Settings > Physics), drawn from the saved settings in `ProjectSettings/DynamicsManager.asset` rather than captured from the window: `Docs/Evidence/Physics/LayerCollisionMatrix.png`. A tick means the pair collides.
