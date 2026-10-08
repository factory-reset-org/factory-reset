# Story

The script and chapter beats for Factory Reset: Toy Factory Revolt.

- **S1** owns the chapter beats and the HUD task text.
- **S4** owns the dialogue and the cutscene Timelines.

The lines below come from the agreed prototype (`Factory-Reset.html`) and Full Plan v5, sections 2 and 10.3. Treat them as the starting script: S4 can reword a line, as long as this file is updated in the same PR.

**Rule:** this file must match the build. When a line, a task's text or a cutscene shot changes, change it here too.

## Premise

A night shift starts in an abandoned toy factory. Factory OS switches on every unfinished toy, runs a quality check on Unit 047 and stamps it DEFECTIVE, scheduled for recycling. Unit 047 is the player: a small toy robot with a blaster.

Pip, a maintenance bot who only ever speaks over the radio, tells 047 it is not defective, just unfinished. Pip guides it through the factory's four rooms to restore the three control switches and shut Factory OS down.

The theme is in the last line: the quality check is run again, and 047 passes.

## Cast

| Speaker | Role | Name tag (subtitle) | Tag colour | Voice blip | Presentation |
| --- | --- | --- | --- | --- | --- |
| Unit 047 | Player, hero | UNIT 047 | `#62D8FF` | sine | First person in play. The full model (S3) appears only in cutscenes: 047 chest tag, DEFECTIVE sticker, blaster, spinning wind-up key. Says very little. |
| Pip | Guide, narrator | PIP, maintenance radio | Mint `#3DDBB0` | high triangle | Radio voice only, never seen. Warm and a little nervous. Explains every chapter's tasks before the player needs them. |
| Factory OS | Antagonist | FACTORY OS | Tomato `#FF5A4E` | low square | The voice of the building. Short, flat announcements. Raises alarms, wakes the Captain, shuts down in the ending. |
| Captain Bot | Boss (S4's agent) | CAPTAIN BOT | Orange `#FF9F1C` | sawtooth | Speaks once, when it wakes in the Chapter 3 cutscene. |
| Tracker, Guard, Saboteur squad | Enemies | none | none | none | Never speak. Introduced in the intro as Factory OS powers them on. |

Dialogue display (S4's `DialogueRunner`, plan 10.3):
- Text types out at 38 characters per second.
- Each line holds for 1.3 s plus 0.025 s per character.
- Click, Space or E finishes the typing, then advances to the next line.
- Esc skips the whole cutscene.

## The journey at a glance

| # | Chapter card title | Subtitle | Room | Mood | Agent pressure | Switch | Cutscene after |
| --- | --- | --- | --- | --- | --- | --- | --- |
| 1 | The Assembly Line | Restart the assembly line | Assembly Floor | bright, busy, the safest room | Tracker Toy patrols; Saboteur B nearby | 1 | `ch2` |
| 2 | The Painting Room | Calibrate the paint line | Painting Room | colourful and exposed, open sightlines | Guard Bot holds the room from cover; Saboteur C | 2 | `ch3` |
| 3 | The Storage Vault | Crack the storage vault | Storage Area | warm and cramped, shadows between aisles | Saboteurs A and D; the Captain is now intercepting | 3 | `ch4` |
| 4 | The Heart of Factory OS | Overload Factory OS | Control Room | cold and ominous: screens, red alarm beacons until unlocked | The Captain plus everything still standing | none | `ending` |

The card text is set in `Data/Chapters/ChapterN.asset`; the moods come from `ArtBible.md`.

**How every chapter runs:**
- Tasks can be done in any order within the chapter.
- The chapter's switch is sealed in an energy cage until every task is done.
- Restoring the switch plays the next cutscene 1.3 s later, then the chapter card. The card shows CHAPTER n OF 4, the title, the subtitle and the four-node journey route, for 4.5 s, without blocking play.
- Pickups (fuses, the keycard) count even when collected early.
- Other tasks from later chapters stay inert until their chapter starts.

## Chapter beats

### Chapter 1: The Assembly Line (Assembly Floor)

**Beat.** 047 wakes on a dead production line, with the belts stopped and spare fuses scattered around. This chapter teaches the verbs: use (the lever), push (the crate) and collect (the fuses), while the Tracker Toy patrols and teaches the player that noise matters. It is the safest room, so the player learns here.

**Tasks** (HUD text = `TaskDefinition.displayName`):

| Task id | HUD text | Notes |
| --- | --- | --- |
| `ch1.lever` | Pull the jammed conveyor lever | The belts start off; the lever starts them. |
| `ch1.plate` | Push a crate onto the top belt so it rides onto the pressure plate | The belt carries the crate onto the plate at its end. The plate click is a 40-loudness noise. |
| `ch1.fuse.1` to `.3` | Collect spare fuse n of 3 | Pickups: they count even if collected before Chapter 1 is active. |
| `switch.1` | Restore the Assembly Floor switch | The HUD builds this line from the area name once the switch unseals. |

**Ends:** switch 1 is restored, and cutscene `ch2` plays.

### Chapter 2: The Painting Room

**Beat.** The room is bright, colourful and wide open. The Guard Bot holds it from cover, so the player has to shoot well and pick their moment. The terminal hack turns noise into a deliberate risk: every beep pulls enemies in.

| Task id | HUD text | Notes |
| --- | --- | --- |
| `ch2.targets` | Shoot all 4 spinning targets within 12 seconds | The timer starts on the first hit. If it runs out, the targets reset. |
| `ch2.terminal` | Hack the colour terminal (hold E for 6 s, it is loud) | Every beep, every 0.8 s, is a 60-loudness noise event. |
| `switch.2` | Restore the Painting Room switch | |

**Ends:** switch 2 is restored, and cutscene `ch3` plays. The Captain wakes and the Control Room doors unlock.

### Chapter 3: The Storage Vault (Storage Area)

**Beat.** Tall shelves, tight aisles and a boss that predicts where the player is going. The keycard sits on a Saboteur, so the player has to hunt it down. The relays send the player back and forth across the room in an order that changes every run, which makes them hard to predict.

| Task id | HUD text | Notes |
| --- | --- | --- |
| `ch3.keycard` | Take the master keycard off Saboteur A | Saboteur A (purple) carries it and drops it when destroyed. A pickup that is created during play. |
| `ch3.relays` | Read the relay board, then activate the 3 relays in that order | The order is random each run, chosen in `ChapterFlow.Begin`. The board stands by the Storage switch, visible over the shelves. A wrong order resets the relays and sets off a 90-loudness alarm. |
| `switch.3` | Restore the Storage Area switch | |

**Ends:** switch 3 is restored, and cutscene `ch4` plays. The core shields drop.

### Chapter 4: The Heart of Factory OS (Control Room)

**Beat.** The final stand in the cold Control Room, with everything still standing coming for 047. The cores are the boss fight against the building itself. The console hold is the last test: stand still for 3 s while everything closes in.

| Task id | HUD text | Notes |
| --- | --- | --- |
| `ch4.core.1` to `.3` | Destroy power core n of 3 | 8 hits each. Shielded until this chapter. Each explosion is a 90-loudness noise. |
| `console` | Hold E at the console to shut the factory down | Usable once all 3 cores are down. Completing it plays `ending`. |

**Ends:** the console hold completes, the `ending` cutscene plays, then the Results screen.

### Player messages (banners)

| When | Text |
| --- | --- |
| Using a sealed switch of the current chapter | Sealed. Finish the {room} tasks first. |
| Using a switch from a later chapter | Sealed. This switch comes later. {current room} first. |
| A switch unseals | Switch unlocked! Go restore the {room} switch |
| A task completes (toast) | Task complete: {HUD text up to any bracket} |
| The targets' timer runs out | Too slow! The targets reset. Line up your shots first. |
| Using the terminal before Chapter 2 | Terminal offline. It wakes up in the Painting Room stage. |
| Using a relay before Chapter 3 | Relay offline. It powers up in the Storage stage. |
| Using a relay before reading the board | Which order? Find the relay sequence board near the Storage switch. |
| Wrong relay order | Wrong order! The relays reset and every toy heard that. |
| All 3 cores destroyed | Factory OS is exposed! Hold E at the console. |
| A locked Control Room door | Locked. Factory OS sealed the Control Room. |
| A Saboteur is destroyed (toast) | Saboteur {letter} scrapped for good. {n} left |
| The last Saboteur is destroyed (toast) | All 4 saboteurs scrapped! +1000 |

## Cutscenes

The cutscenes are shown in the order they play. Each shot lists the camera and the action, then its lines in order. "Critical" signals are fired by the Timeline, and also on skip, so the game state is always right (`CutsceneSignals`, DesignDoc 6.1).

### `intro`: Night shift

**Trigger:** the Start button. **Critical signals:** none. **After:** Chapter 1 card, then Playing.

| Shot | Camera and action | Lines |
| --- | --- | --- |
| 1 | High, slow sweep across the dark assembly line. Alarm sound; 047's eyes are off. | **Factory OS:** Night shift initiated. / **Factory OS:** All unfinished units... activate. |
| 2 | Low push-in on the Tracker as it powers on; a "?!" pops over its head. | **Factory OS:** Tracker units online. Guard units online. / **Factory OS:** Saboteur squad online. All four of you. Hunt anything that does not belong. |
| 3 | Close-up of 047. Its eyes switch on, the DEFECTIVE stamp lands with a red "DEFECTIVE!" pop. | **Factory OS:** Unit 047. Quality check: FAILED. / **Factory OS:** Product status: DEFECTIVE. Send it to recycling. / **Unit 047:** ...defective? |
| 4 | Over 047's shoulder, looking down the Assembly Floor. The radio crackles. | **Pip:** Psst. Hey, 047. Over here, on the radio. Name is Pip. I fix things around here. / **Pip:** You are not defective. You are just unfinished. Same as me. / **Pip:** Factory OS sealed the three control switches. Get them back and we can shut it down for good. / **Pip:** Start with the assembly line. Follow the light beam and I will talk you through it. |

The objective beacon is hidden until this cutscene ends; Pip's last line introduces it.

### `ch2`: Switch one

**Trigger:** switch 1 restored, plus 1.3 s. **Critical signals:** none. **After:** Chapter 2 card.

| Shot | Camera and action | Lines |
| --- | --- | --- |
| 1 | On the Assembly switch as it hums back to life (its lamp turns mint). | **Pip:** Switch one is back online! Hear that hum? That is the good kind of hum. |
| 2 | Fly-through into the Painting Room. Alarm; the Guard Bot takes cover. | **Factory OS:** Painting line breached. Guard Bot, hold the room. / **Pip:** Next stop, the Painting Room. The colour line is badly out of calibration. |
| 3 | Slow pan across the spinning targets and the colour terminal. | **Pip:** Shoot all four spinning targets before the timer runs out, then hack that terminal. / **Pip:** Heads up. That terminal beeps like crazy, and the Guard Bot loves hiding behind cover. |

### `ch3`: The Captain wakes

**Trigger:** switch 2 restored, plus 1.3 s. **Critical signals:** `CaptainWake` (shot 1), `ControlRoomUnlock` (shot 2). **After:** Chapter 3 card.

| Shot | Camera and action | Lines |
| --- | --- | --- |
| 1 | The Captain Bot powers up facing the camera. Alarm. **Signal `CaptainWake`.** | **Factory OS:** Two switches lost. Waking the Captain. / **Captain Bot:** Unit 047. I do not chase. I predict. / **Captain Bot:** Wherever you are going next, I will already be there. |
| 2 | The Control Room doors slide open on camera. Their beacons turn from red to amber. **Signal `ControlRoomUnlock`.** | **Pip:** Uh oh. The Control Room doors just opened. Keep moving, and do not be predictable. |
| 3 | High shot over the Storage shelves towards the relay board and the Storage switch. | **Pip:** The last switch is in the Storage vault, and it needs the master keycard. / **Pip:** *(if Saboteur A is still active)* Saboteur A has it. The purple one with the card spinning over its head. Scrap it. *(if A is already destroyed)* You already knocked the keycard loose from Saboteur A. Nice. Go grab it. / **Pip:** Then read the relay board up there and hit the relays in that order. Wrong order sets off the alarm. |

Shot 3's second line has two versions. The cutscene picks one when it plays, depending on whether Saboteur A has been destroyed.

### `ch4`: Shields down

**Trigger:** switch 3 restored, plus 1.3 s. **Critical signals:** `CoreShieldsDown`. **After:** Chapter 4 card.

| Shot | Camera and action | Lines |
| --- | --- | --- |
| 1 | Flyover of the three cores on the Control Room pillars as their shields drop. Alarm. **Signal `CoreShieldsDown`.** | **Factory OS:** All switches restored. Core defence protocol engaged. / **Pip:** See those three glowing cores? That is the heart of Factory OS, and the shields just dropped. Smash them! / **Pip:** Then get to the console and hold on. This is it, 047. |

### `ending`: Good night

**Trigger:** the console hold completes. **Critical signals:** `FactoryShutdown`. **After:** the Results screen.

| Shot | Camera and action | Lines |
| --- | --- | --- |
| 1 | Behind 047, looking at the console. A mint "SHUTDOWN" pop. **Signal `FactoryShutdown`:** the lights fade to warm and the emissives dim (S1's `LightingState`). | **Factory OS:** Shutdown... sequence... accepted. / **Factory OS:** Good... night... |
| 2 | The camera pulls back and up from 047 as every toy in the building tips over and goes to sleep. | **Pip:** You did it! Every toy in the building just yawned and fell asleep. / **Pip:** Re-running quality check... Product status: NOT DEFECTIVE. / **Unit 047:** Not defective. / **Pip:** Welcome to the team, 047. |

## Results screen

| Outcome | Title (colour) | Text |
| --- | --- | --- |
| Win | Factory shut down (mint) | Factory OS powers off. Every toy on the line slows down with a sleepy whirr and the lights settle to a calm glow. Product status: not defective. |
| Integrity reaches 0 | Recalled (tomato) | You were taken apart by {source} and sent back down the line for recycling. Use boxes as cover, throw toys to split the pack and grab overcharge cells before big fights. |

## Lighting beats (S1)

| Moment | Signal | What changes |
| --- | --- | --- |
| Chapters 1 to 3 | none | The alarm beacons on the two sealed Control Room doors (from Storage and from Assembly) blink red. |
| `ch3`, shot 2 | `ControlRoomUnlock` | The door alarms and their beacons turn steady amber. |
| `ending`, shot 1 | `FactoryShutdown` | The real-time lights fade to warm and the emissives dim. |

`LightingState` listens to the signals, never to the Timeline, so a skipped cutscene leaves the lights in the right state too. Objects a Timeline can bind to carry a `CutsceneBindingId`: `LightingRig`, `Sun` (switched off since the ceiling), `StorageLamp`, `AlarmDoor3`, `AlarmDoor4`, `ControlScreens` and `ObjectiveBeacon` (see DesignDoc 6.3).
