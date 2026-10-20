# UI evidence index

Pictures of every screen S3 owns (title with Quit, credits, HUD, pause, results), taken in the running game from `Bootstrap`. They are the evidence for plan task U1 ("look at the HUD, title and results on screen"). All are 1,920 x 1,080 (16:9); the four 21:9 ones are 2,560 x 1,080.

## How they were made

`Tests/PlayMode/UiScreenshotEvidenceTests.cs` (category Evidence, explicit) loads `Bootstrap`, drives the game through the screens, sends the game camera and every screen canvas into a render texture and writes the PNG. Run it with a graphics device (not `-nographics`):

```text
Unity.exe -batchmode -projectPath <project> -runTests -testPlatform PlayMode -testFilter ToyFactory.Tests.UiScreenshotEvidenceTests
```

Set `FR_UI_SHOTS` to write somewhere other than `Docs/Evidence/UI/`.

## Limits (read before quoting these)

- The run is **driven by the test, not played by hand**: the intro is skipped, the pause and Results states are requested directly. The score on the Results pictures is whatever the idle run had (5,768, grade C, 0:10), not a real playthrough.
- The leaderboard shows one entry, "Mia": a name typed by the test into the real name field and saved through the real Save path.
- The canvases are drawn by the game camera in a render texture instead of as a screen overlay, with the canvas plane 0.35 m from the lens. The first-person blaster is nearer than that, so in some pictures it covers a corner of the UI (the Music percentage on the pause screen, the Charge label in the HUD). In the game the overlay is always on top; this is a picture artefact, not a layout fault.
- Taken in the editor on the team machine (Unity 6000.6.2f1). Not from the Windows build.
- Layout checks at seven screen shapes are done by `HudLayoutSceneTests`; these pictures are a look, not a replacement for those tests.

## The pictures

| File | What it shows |
| --- | --- |
| `title_16x9.png`, `title_21x9.png` | Title: name, route, controls, START in the middle, CREDITS bottom left, **QUIT bottom right** |
| `credits_menu_16x9.png` | Credits opened from the title (menu mode, BACK button) |
| `credits_ending_16x9.png` | The credits as they roll after a won run (SKIP button) |
| `intro_cutscene_16x9.png` | Intro cutscene: letterbox bars and the subtitle bar; the HUD is hidden |
| `hud_chapter1_card_16x9.png` | Chapter 1 just started: chapter card over the HUD |
| `hud_chapter1_16x9.png`, `hud_chapter1_21x9.png` | HUD in play: objectives with the task in hand and its distance, switch lamps, score and time, integrity, charge and toys, key hints, crosshair |
| `hud_everything_16x9.png`, `hud_everything_21x9.png` | Everything up at once: room banner, checkpoint note, prompt with the hold bar, toy dots, hit marker |
| `hud_folded_16x9.png` | Tab folded the objectives and H hid the key hints |
| `pause_16x9.png` | Pause: Resume, Restart from checkpoint (3 left), Restart game, Sound and Music sliders |
| `results_name_entry_16x9.png`, `results_name_entry_21x9.png` | Results with the name field open |
| `results_saved_16x9.png` | Results after Save: rank in the top 10, PLAY AGAIN and QUIT |

## Not in this folder

- Hand-played screenshots from the Windows build (task P2 in the plan: needs a person).
- The F3 AI overlay, which is drawn with IMGUI and cannot be sent to a render texture; check it in the Editor or the build (checklist in the plan).
