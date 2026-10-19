# Background music (third-party, CC0 and soundimage.org)

Five looping tracks for the title screen and the four rooms, and two short stings for the
Results screen, downloaded on 2026-10-19.
They replace the music that was made in code (`MusicComposer`, removed), which the team
found too soft and toy-like for the game. They are played by `MusicDirector` (S4,
`Scripts/Journey/Cutscenes/`); the choice is recorded in `Docs/DesignDoc.md` (decision
log, 2026-10-19).

These are not the team's own work. Four are public domain (CC0 1.0,
https://creativecommons.org/publicdomain/zero/1.0/). Three are by Eric Matyas, free to use
in a game, commercial use included, on condition that the game's own credits name him:
"Music by Eric Matyas, www.soundimage.org", with his track titles when other composers'
music is used too (https://soundimage.org/attribution-info/). The title screen's credits
block shows that line (`ThirdPartyCredits`).

## Credits

| Plays in | File | Track | Composer | Licence | Source |
| --- | --- | --- | --- | --- | --- |
| Title screen, and after the results jingle | `Title_CreepyAmbientLoop.ogg` | Creepy Ambient Loop (v2) | epb9000 | CC0 1.0 | https://opengameart.org/content/creepy-ambient-loop |
| Chapter 1, Assembly Floor | `AssemblyFloor_CreepyAction.ogg` | Creepy Action | Eric Matyas | Free with attribution | https://soundimage.org/horrorsurreal/ |
| Chapter 2, Painting Room | `PaintingRoom_TheyreClosingIn.ogg` | They're Closing In (looping) | Eric Matyas | Free with attribution | https://soundimage.org/dark-ominous/ |
| Chapter 3, Storage Area | `StorageArea_NightStalker.ogg` | Night Stalker (looping) | Eric Matyas | Free with attribution | https://soundimage.org/horrorsurreal/ |
| Chapter 4, Control Room | `ControlRoom_HeavyBossBattle1.ogg` | Heavy Boss Battle 1 | MintoDog | CC0 1.0 | https://opengameart.org/node/183631 |
| Results screen, a won run (once) | `Win_VictoryFanfareShort.wav` | Victory Fanfare Short | cynicmusic | CC0 1.0 | https://opengameart.org/content/victory-fanfare-short |
| Results screen, a lost run (once) | `Lose_GameOver.ogg` | Game Over | Kistol | CC0 1.0 | https://opengameart.org/content/game-over |

## Changes

None to the music. Each file is the composer's own looping version, unedited and
imported at 44.1 kHz. Each track's loudness is evened out in `MusicDirector` by a gain per
track, not by editing the file.

Every loop was checked for a click at its seam (the jump from its last sample to its
first, against the largest step inside the track). "The Rise of the Mech Beings" was
first chosen for the Assembly Floor but jumped 0.22 at its seam, more than any step
inside it, so it would click every 42 s; "Creepy Action" (jump 0.02) replaced it.
