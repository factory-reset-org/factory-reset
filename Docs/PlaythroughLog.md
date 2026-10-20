# Playthrough log

Full runs from the title screen to the results screen, kept as evidence for the final report. Screenshots are in `Docs/Evidence/Playthrough/`. Run 1 is a scripted run in the editor; run 2 is for a hand-played run on the Windows build.

## Run 1: scripted, in the editor (2026-10-19, after the play-test fixes)

`Tests/PlayMode/PlaythroughEvidenceTests.Evidence_ScriptedPlaythroughFromTitleToResults` (category Evidence, explicit: run it on its own) plays the whole game in the real scenes with every agent awake. Each task is done through the prop itself: the lever is used, the crate rides the top belt to the stop and latches the plate, the fuses and the keycard are walked into, the targets and cores are shot, the terminal and console are held, the relays are pressed in the order the board shows, and Saboteur A is shot until it drops the keycard. The player is moved to each prop, and every cutscene is skipped as soon as it starts.

**What it is not:** a hand-played run. There is no aiming, no route choice and no fighting back, and tasks happen as fast as the props allow, so the times below are much shorter than a real run. The player's health was topped up after every hit so the run reached the end; every hit was still counted, by who dealt it. Its full log is `Docs/Evidence/Playthrough/scripted_playthrough.txt`.

| Field | Value |
| --- | --- |
| Date | 2026-10-19 |
| Build | Unity 6000.6.2f1 editor, Play Mode. The Windows build of the same commit (`ad6742e`, 2026-10-18 02:59 in `Docs/BuildLog.md`) starts to the title screen with no errors in its log (`build_title.png`) |
| Commit | `s2/fix/final-systems` at the commit that adds this run |
| Machine | S2's laptop (Windows 11) |
| Player | Scripted (S2's evidence test) |
| Total time | 0:26 of game time, cutscenes skipped |
| Result (win / lose) | Win |
| Final score and grade | 13,270, grade A |

### Chapter by chapter

| Chapter | Task | Done at (mm:ss) | Notes | Screenshot |
| --- | --- | --- | --- | --- |
| 1 Assembly | `ch1.lever` conveyor lever | 00:01 | Both belts started | `ch1_assembly.png` |
| 1 Assembly | `ch1.plate` pressure plate | 00:05 | The crate, placed on the top belt, rode to the stop and latched the plate | |
| 1 Assembly | `ch1.fuse.1` to `ch1.fuse.3` | 00:05 | Each picked up by walking into it | |
| 1 Assembly | Switch 1 | 00:05 | Unsealed once the five tasks were done; the cutscene was skipped | |
| 2 Painting | `ch2.targets` spinning targets | 00:09 | Four targets hit inside the 12 s window | `ch2_painting.png` |
| 2 Painting | `ch2.terminal` terminal | 00:15 | Held for 6 s | |
| 2 Painting | Switch 2 | 00:15 | Cutscene skipped | |
| 3 Storage | `ch3.relays` relay order board | 00:19 | Order 2, 3, 1, read at the board and pressed in that order | `ch3_storage.png` |
| 3 Storage | `ch3.keycard` | 00:19 | Saboteur A scrapped after 2 hits, the keycard dropped and was picked up | |
| 3 Storage | Switch 3 | 00:19 | Cutscene skipped: the Captain woke and doors 3 and 4 unlocked | |
| 4 Control | `ch4.core.1` to `ch4.core.3` power cores | 00:22 | Shields down after the Chapter 4 cutscene; 8 hits each | `ch4_control.png` |
| 4 Control | `console` | 00:25 | Held for 3 s, then the ending | |
| Results | | 00:26 | Won, 13,270, grade A | `results.png` |

### Agents seen

| Agent | Damage dealt to the player in the run | Notes |
| --- | --- | --- |
| Tracker Toy | 16 | Two pounces at 8 each |
| Guard Bot | 30 | Three hits at 10 each; the Guard stays in the Painting Room, so they were there |
| Saboteur squad | 50 | Swipes |
| Captain Bot | 0 | Awake for the 6 s of Chapter 4 the run took, but it did not land a hit |

The run records totals by agent, not where each hit landed. Three of the four agent types hurt a player who never fought back; the Captain had only the few seconds of Chapter 4.

### Checks covered by the run and the tests

- [x] Every chapter task, every switch and the ending, in order, with no softlock (this run)
- [x] Every cutscene skippable without breaking the journey (this run skips all five)
- [x] Pushable boxes, belts and the plate latch (`CrateOnBeltTests`, `BoxShoveTests`, `PushableBoxSlideTests`)
- [x] Doors lift, come down and wait for the player (`LevelPropFixesTests`)
- [x] Battery pickup scores, charger glow, targets, console (`LevelPropFixesTests`)
- [x] The Guard shoots a player who never fires (`GuardBrainTests`)
- [ ] Blaster feel, sounds, the HUD hit marker, mute, pause, death and Play again: need a hand-played run (run 2)

### Frame rate

Not measured in this run: the editor window was not focused, and Unity throttles an unfocused editor, so its frame times (9 to 18 fps average) say nothing about the game. The frame rate is for run 2 on the Windows build, and the AI cost per frame is measured in `Docs/AIPerformanceLog.md` (seven agents under the 2 ms AI budget).

## Run 2: hand-played, on the Windows build

| Field | Value |
| --- | --- |
| Date | |
| Build (from `Docs/BuildLog.md`) | |
| Commit | |
| Machine | |
| Player | |
| Total time | |
| Result (win / lose) | |
| Final score and grade | |

| Chapter | Task | Done at (mm:ss) | Notes (what worked, what looked or felt wrong) | Screenshot |
| --- | --- | --- | --- | --- |
| 1 Assembly | `ch1.lever` conveyor lever | | | |
| 1 Assembly | `ch1.plate` pressure plate | | | |
| 2 Painting | `ch2.targets` spinning targets | | | |
| 2 Painting | `ch2.terminal` terminal | | | |
| 3 Storage | `ch3.relays` relay order board | | | |
| 4 Control | `ch4.core.1` power core | | | |

| Place | Average fps | Lowest seen | Notes |
| --- | --- | --- | --- |
| Assembly | | | |
| Painting | | | |
| Storage | | | |
| Control (all agents awake) | | | |
