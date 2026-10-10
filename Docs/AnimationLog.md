# Animation Log (GV evidence, S4)

Every piece of motion on the agents, in the cutscenes and in their effects: how it moves, the numbers behind it, and what checks it. The design reasons are in the DesignDoc (agent animation, hits and shooting, hit effects and icons, cutscenes). The frame cost of all of it is measured in `OptimisationLog.md` (the S4 rows of 2026-10-10). Screenshots are in `Docs/Evidence/Agents/`.

Most of it is written in code on S3's frozen pivots rather than keyframed, for one reason: the motion has to follow something a clip cannot know in advance (the speed the body really moves at, the moment a hit lands, the shot a cutscene has reached). Where a clip does fit (the locomotion loops), the clips are generated from numbers, so a change is one edit and a rebuild.

## Locomotion

| Technique | How it moves | Numbers | Checked by |
| --- | --- | --- | --- |
| Turning | The body turns towards where it walks with `Quaternion.RotateTowards`, about the vertical axis only, so it never snaps round and never tilts on a slope. Standing, it turns to the brain's look target instead | 360°/s; a waypoint counts as reached within 0.3 m | `AgentFacingTests` (a standing agent faces its look target within 3°; a walking one faces where it walks within 5°) |
| Speed easing | Speed eases towards the route's speed instead of jumping to it; a stop stays immediate so arrivals are exact | 12 m/s², 0 to 4.6 m/s in about 0.4 s | `AgentSchedulingTests.BodiesEaseUpToSpeed` (under 2 m/s after two frames, 4.6 ± 0.05 m/s after 0.6 s) |
| Blending into a new route | A replan that turns 25° to 120° from the current heading is joined with a quadratic Bézier B(t) = (1−t)²P + 2(1−t)tC + t²E: from the agent P to a point E about 0.35 s along the route, control point C half that distance ahead on the heading. The curve stays inside the triangle P, C, E, so it never overshoots | E is 0.6 to 1.6 m ahead; only while moving at 0.5 m/s or more; every segment passes the grid line check or the route is used as it is | `PathBlenderTests`; `AgentSchedulingTests.ARouteChangedMidWalkIsJoinedWithACurve` (keeps going 0.15 m or more the old way while bending, instead of pivoting) |
| No hitch on a replan | A new route that starts at the centre of the cell the agent is leaving skips that first waypoint when the agent is already nearer the second | Was a visible step back once in seven re-routes in a scripted run | `AgentFacingTests.ANewRouteDoesNotSendTheAgentBackToItsCellCentre` |
| Animation speed from real movement | The Blend Tree reads `GroundSpeed`: how far the body really moved this frame, capped at the speed it was told. Pressed against another body it shows standing, not running on the spot | 0.1 s damping in `AgentAnimatorBridge` so speed changes do not pop | `AgentSchedulingTests.ABodyPressedAgainstSomethingStandsInsteadOfRunningOnTheSpot` (commanded above 4 m/s, shown under 0.2 m/s) |
| Stand-off | Within reach of the player the body holds still and faces them instead of driving into the player's capsule; it walks on once the player is 0.3 m further away, so it does not flicker at the limit | 1.4 m Tracker, 1.6 Saboteur and Guard, 2.0 Captain | `AgentStandOffTests` |

## Clips generated from numbers

| Technique | How | Numbers | Checked by |
| --- | --- | --- | --- |
| Clip builder | Every locomotion clip is sine waves on the frozen pivots, `value = offset + amplitude · sin(2π(cycles · t/L + phase))`, written by `AgentAnimationBuilder` from `AgentMotionLibrary`. Whole cycles per clip make every loop seamless | 5 clips per agent (Idle, Walk, Run, LeanLeft, LeanRight) | `AgentAnimationAssetTests.EveryClipLoopsWithoutAJump`, `EveryCurveDrivesAPivotOfTheModel` |
| Blend Tree | One 2D Freeform Cartesian tree over Speed and TurnRate. Every clip keys the same channels, so blending never pulls a pivot towards a default | Walk / run at the brains' own speeds (Captain 2.5 / 4.6 m/s); leans at ±180°/s | `AgentAnimationAssetTests.ControllerBlendsFiveClipsOverSpeedAndTurnRate` |
| Captain stride | A hip swing of ±θ moves a boot 2·l·sin θ per step (l = 1.08 m), so the cycle that keeps the boots from sliding at speed v is L = 2 · 2·l·sin θ / v | Walk ±22° at 2.5 m/s: 0.65 s. Run ±35° at 4.6 m/s: 0.54 s | Worked by hand in the DesignDoc; the speeds are the brain's |
| Wheels | A wheel of radius r rolling d turns d / r radians, so each wheel turns `Speed · Δt / r` a frame. A clip could match only one speed and would slip at every other | Tracker's four wheels, the Saboteur's one | `AgentAnimationTests.OneTurnOfAWheelCoversItsCircumference` |
| Wind-up key | Turns at 180°/s times the Tracker's energy, so it slows as the toy runs down, and at −720°/s while it rewinds | Energy read from the brain through `IWindUpState` | `AgentAnimationTests.KeySlowsAsTheToyRunsDownAndSpinsBackWhileRewinding` |

## Fighting

| Technique | How | Numbers | Checked by |
| --- | --- | --- | --- |
| Aim pose | An Aim override layer (cannon arm up, shield or torso braced) fades in while the agent attacks. Its weight is driven from code, so it never fights the locomotion layer | Fades over 0.1 s; the weapon turns the body to face the target and fires only within 25° | `AgentAnimationAssetTests.ShootersHaveAnAimOverrideLayerStartingAtWeightZero`, `AgentCombatTests.AShotWaitsForTheBodyToTurnToTheTargetSoItNeverLeavesSideways` |
| Shot recoil | Each shot kicks the arm holding that barrel up, the torso and the head back, added on top of the aim pose in LateUpdate. The Captain alternates cannons, so its kick alternates arms | Arm 28°, torso 6°, head 8°, peaking at 15% of 0.28 s | `AgentCombatTests.EachShotKicksTheArmOfTheCannonThatFired` |
| Tracker pounce | The whole toy pounces: crouch and rock back, hop forward nose first with the bite, hop back. Keyframed as curves over normalised time and added on top of the Animator's pose, so nothing builds up | Every 1.3 s: wind-up 0.2 s, hop 0.35 m in 0.15 s, back in 0.25 s | `AgentMeleeDamageTests.TheTrackersPounceIsCosmeticUntilItIsGivenDamage` |
| Saboteur swipe | Arm up with the pincer open while the body twists away, then twists back, leans in, rolls forward and slashes, alternating arms | Every 1.2 s; rolls 0.3 m on its wheel; the damage lands at contact (half way), not at the start, so the wind-up is a telegraph | `AgentMeleeDamageTests.EachSwipeHurtsThePlayerOnceAtTheMomentOfContact`, `APlayerWhoBacksOffDuringTheWindUpIsNotHit` |

## Taking hits

| Technique | How | Numbers | Checked by / evidence |
| --- | --- | --- | --- |
| Hit squash | Each hit squashes the model and throws a few sparks at the chest; no word, so a burst of hits does not fill the screen | 0.15 s | `AgentKnockdownTests.AHitThrowsSparksAndSquashesButShowsNoWord` |
| Knock-out | Explosion burst, a comic word ("KNOCKED OUT!"), the body tips over (the Captain kneels instead), lies until its reboot, then gets back up | Tracker 7 s, Guard 8 s, Captain 6 s down | `AgentKnockdownTests.KnockOutExplodesTipsOverGoesDarkAndGetsBackUp`; `Evidence/Agents/captain_knockout_word.png` |
| Captain kneel | A 3.25 m boss toppling like a toy looked wrong, so it drops onto its right knee: front shin vertical (hip −91°, knee +91°) so its boot stays on the floor as the model drops 0.44 m · (1 − cos 91°) ≈ 0.45 m; back leg (hip 15°, knee 85°, ankle −95°) found by measuring the model's mesh bounds over a grid of angles | Down in 0.55 s, landing a little heavy; knee, shin and boot rest within 3 mm of the floor | `AgentKnockdownTests.TheCaptainKneelsInsteadOfTippingThenStepsBackUp`; `Evidence/Agents/captain_kneel.png` |
| Captain stand-up | Leans in and pushes, rises into a lunge, lifts the front foot and steps it back beside the other, then hands back to the Animator | 1.2 s; both boots within 2.5 cm of the floor except the front foot's 5 cm lift for the step | `AgentKnockdownTests.TheCaptainKneelsInsteadOfTippingThenStepsBackUp`, `TheDormantCaptainKneelsDarkThenItsVisorLightsAndItStandsUp` |
| Scrapped (Saboteurs) | The same burst with "SCRAPPED!", lies there, then sinks into the floor and shrinks with a last puff | Lies 1.2 s, sinks over 0.7 s | `AgentKnockdownTests.ScrappedAgentShowsItsWordSinksAndIsSwitchedOff`, `AgentCombatTests.SaboteurIsScrappedForGoodWhenItGoesDown` |
| Power-down (ending) | Every agent powers down for good in a ripple in spawn order: lights sputter out, the Animator winds down to a stop, the body sags; the Captain sinks onto its knee at half speed | 0.4 s + 0.15 s per agent id; wind-down 1.2 s; sag 5° | `AgentKnockdownTests.WhenTheFactoryShutsDownEveryAgentPowersDownOneAfterAnother` |

## Telling the player what the agents think

| Technique | How | Numbers | Checked by / evidence |
| --- | --- | --- | --- |
| "?" and "!" icons | Above every head, facing the camera, with a pop whenever the level rises; hidden while down, scrapped or frozen | Overshoot to 130% then settle, 0.25 s | `AlertIconTests` |
| Captain call-out bubble | A comic speech bubble above the Captain when it commits to a goal and the player can see it, in the cutscene comic words' style; pops in with the same overshoot as those words, faces the camera and grows with distance | 0 → 1.15 → 1 in 0.22 s; shown 2.8 s with a 0.5 s fade; grows ×1 to ×3 past 7 m | `CaptainCalloutTests`; `Evidence/Agents/captain_callout_bubble.png` |
| Prediction marker | A red ring and short beam flash on the goal the Captain committed to; the ring spreads like a ping and the whole marker fades | 2 s; ring 0.5 → 1.3 × with an ease-out; fades over the last 40% | `CaptainCalloutTests`; `Evidence/Agents/captain_prediction_marker.png` |

## Cutscenes

| Technique | How | Numbers | Checked by / evidence |
| --- | --- | --- | --- |
| Shots and camera | Five Timelines built in code by `CutsceneTimelineBuilder`; each shot is a Cinemachine camera bound by id; a letterbox frames the cutscene | Captain wake (ch3): 33.6 s | `CutsceneCameraTests`, `CutsceneTimelineAssetTests`, `CutscenePacingTests`; `Evidence/Agents/cutscene_ch3_captain_wakes.png` |
| Unit 047 in cutscenes | `Unit047Motion` on S3's frozen pivots: a 1.5° sway (3.2 s) and 1° rock (4.3 s) about the waist with the feet planted, a 6 mm breathing bob (1.8 s), the key at 180°/s; the head settles on each new look with a critically damped spring | `SmoothDampAngle` 0.35 s; beats land on the shot, however long the player reads | `Unit047MotionTests` |
| Comic cues | Pop-up words ("?!", "DEFECTIVE!", "ATTEN-HUT!", "SHUTDOWN") as Timeline markers: pop in with an overshoot, drift up 0.35 m and fade in the last quarter, facing the camera | 0 → 1.15 in 0.12 s, settle in 0.1 s | `CutsceneCueTests`, `CutsceneCuePlanTests` |
| Subtitles and chapter card | uGUI with a dynamic font at its final size (the old IMGUI text pixelated when scaled); the chapter card slides and fades in after each cutscene | Card 4.5 s, in over 0.35 s, out over the last 0.6 s | `PlaceholderSubtitlesTests`, `ChapterCardTests` |

## Frame cost

Measured in the Editor from Bootstrap (2026-10-10, ProfilerRecorder, about 3,000 frames each). Full tables and method in `OptimisationLog.md`.

| | Chapter 4 play, all 7 agents | Agents switched off | Cutscene (ch3) |
| --- | --- | --- | --- |
| Game loop, mean / p99 | 7.36 / 11.12 ms | 6.24 / 11.45 ms | 7.25 / 12.43 ms |
| Animators (all) | 0.144 ms | 0 | 0.147 ms |
| Scripts (Update + LateUpdate) | 0.694 ms | 0.329 ms | 0.509 ms |
| Timeline (`Director.PrepareFrame` + `ProcessFrame`) | - | - | 0.327 ms |

All of the agents' motion together costs about 1.1 ms of a 16.7 ms frame (60 fps), and the game stays under 12.5 ms at p99 in play and in the cutscene.

**Limits:** Editor timings with Mono, one machine, one session each; the screenshots are at the Game view's size.
