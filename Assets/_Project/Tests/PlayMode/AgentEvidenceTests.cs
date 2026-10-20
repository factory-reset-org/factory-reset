using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using ToyFactory.Interfaces;
using ToyFactory.Journey.Chapters;
using ToyFactory.Journey.Cutscenes;
using ToyFactory.Journey.Debugging;
using ToyFactory.Runtime.Agents;
using ToyFactory.Runtime.Animation;
using ToyFactory.Runtime.Debugging;
using ToyFactory.UI;
using Debug = UnityEngine.Debug;
using Object = UnityEngine.Object;

namespace ToyFactory.Tests
{
    /// <summary>
    /// The S4 stills and clips in <c>Docs/Evidence/Agents/</c>, taken from the real game (Bootstrap, the real
    /// agents and cutscenes). Stills are PNG files at 1280 x 720. Most clips are side by side: the game on the
    /// left, and on the right the same frame with the agent models hidden and their rigs drawn instead (each
    /// pivot joined to its parent pivot and to the parts it carries, the CharacterController capsule, the
    /// facing), so the motion on the frozen pivots is plain to see. The clips are recorded at a fixed 30 frames
    /// per game second (<see cref="Time.captureFramerate"/>) and joined into MP4 files by ffmpeg (on the PATH, or
    /// named by the environment variable <c>FR_FFMPEG</c>); without it the frames are left in <c>Temp/AgentClips</c>.
    /// </summary>
    /// <remarks>
    /// Needs a graphics device and sets the Game view to 1280 x 720 so the screen captures (the HUD, the cutscene
    /// letterbox and subtitles, the overlay's labels) come out at that size. Each test loads Bootstrap once and
    /// stages one chapter.
    /// </remarks>
    [Category("Evidence"), Explicit("Evidence run: about 3 minutes, needs a graphics device and ffmpeg. Writes PNG and MP4 files to Docs/Evidence/Agents.")]
    public sealed class AgentEvidenceTests
    {
        const string Folder = "Docs/Evidence/Agents";
        const string FrameFolder = "Temp/AgentClips";
        const int Width = 1280;
        const int Height = 720;
        const int Fps = 30;

        static readonly Vector3 Up = Vector3.up;

        readonly List<Object> _created = new List<Object>();
        Component _health;
        System.Reflection.MethodInfo _setHealth;
        Camera _camera;
        RigLines _rig;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            Assert.IsTrue(SystemInfo.graphicsDeviceType != GraphicsDeviceType.Null, "Run this without -nographics.");
#if UNITY_EDITOR
            Type window = typeof(UnityEditor.EditorWindow).Assembly.GetType("UnityEditor.PlayModeWindow");
            window?.GetMethod("SetCustomRenderingResolution")?.Invoke(null, new object[] { (uint)Width, (uint)Height, "Evidence 1280x720" });
#endif
            Directory.CreateDirectory(Folder);
            yield return null;
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            Time.captureFramerate = 0;
            Time.timeScale = 1f;
            AgentDebugOverlay.LabelCamera = null;
            foreach (Object created in _created)
                if (created != null)
                    Object.Destroy(created);
            _created.Clear();

            Scene empty = SceneManager.CreateScene("Empty after agent evidence " + Guid.NewGuid());
            SceneManager.SetActiveScene(empty);
            for (int i = SceneManager.sceneCount - 1; i >= 0; i--)
            {
                Scene scene = SceneManager.GetSceneAt(i);
                if (scene != empty && scene.isLoaded)
                    yield return SceneManager.UnloadSceneAsync(scene);
            }
            if (GameClock.Current is Object clock && clock == null)
                GameClock.Publish(null);
            if (PlayerState.Current is Object player && player == null)
                PlayerState.Publish(null);
        }

        // ---------------------------------------------------------------- the game

        static IEnumerator Until(Func<bool> condition, float seconds, string what)
        {
            float until = Time.realtimeSinceStartup + seconds;
            while (!condition())
            {
                if (Time.realtimeSinceStartup > until)
                    Assert.Fail("Timed out waiting for " + what);
                yield return null;
            }
        }

        // Loads Bootstrap and presses Start. The intro is left to play when asked, and skipped otherwise.
        IEnumerator LoadGame(bool keepIntro = false)
        {
            AgentSpawner previous = AgentSpawner.Instance;
            SceneManager.LoadScene("Bootstrap");
            yield return null;
            yield return Until(() => AgentSpawner.Instance != null && AgentSpawner.Instance != previous
                && AgentSpawner.Instance.SpawnedAgents.Count > 0 && ChapterManager.Current != null
                && ChapterManager.Current.Flow != null && ChapterManager.Current.Flow.HasBegun
                && PlayerState.Current != null && Object.FindAnyObjectByType<ChapterJump>() != null, 60f, "the level");

            _health = ((Component)PlayerState.Current).GetComponent(Type.GetType("ToyFactory.Player.PlayerHealth, Assembly-CSharp"));
            _setHealth = _health.GetType().GetProperty("Current").GetSetMethod(true);
            _camera = NewCamera("Evidence Camera");
            _rig = _camera.gameObject.AddComponent<RigLines>();

            GameClock.Current.RequestState(GameState.Playing);
            yield return Until(() => CutsceneDirector.Current != null && CutsceneDirector.Current.IntroRequested, 20f, "the intro to be asked for");
            if (!keepIntro)
                yield return SkipCutscenes();
        }

        static IEnumerator SkipCutscenes()
        {
            float until = Time.realtimeSinceStartup + 30f;
            while (GameClock.Current.State != GameState.Playing || IsCutscenePlaying)
            {
                if (IsCutscenePlaying)
                    CutsceneDirector.Current.Skip();
                if (Time.realtimeSinceStartup > until)
                    Assert.Fail("Timed out waiting for the cutscene to end");
                yield return null;
            }
        }

        static bool IsCutscenePlaying => CutsceneDirector.Current != null && CutsceneDirector.Current.IsPlaying;

        static IEnumerator JumpTo(int chapter)
        {
            ChapterJump jump = Object.FindAnyObjectByType<ChapterJump>();
            jump.JumpTo(chapter);
            yield return Until(() => !jump.IsJumping, 30f, "the jump to Chapter " + chapter);
            yield return SkipCutscenes();
        }

        // Keeps the player alive while the agents attack it: the evidence is about them, not the run.
        void TopUp() => _setHealth.Invoke(_health, new object[] { 100f });

        // Waits in game time, keeping the player topped up.
        IEnumerator Wait(float seconds)
        {
            float until = Time.time + seconds;
            while (Time.time < until)
            {
                TopUp();
                yield return null;
            }
        }

        static AgentController Agent(AgentType type, char squad = 'B') =>
            AgentSpawner.Instance.SpawnedAgents.First(a => a.Type == type && (type != AgentType.Saboteur || a.Identity.SquadLetter == squad));

        static void Place(Component body, Vector3 feet, float yaw)
        {
            var controller = body.GetComponent<CharacterController>();
            if (controller != null)
                controller.enabled = false;
            body.transform.SetPositionAndRotation(feet, Quaternion.Euler(0f, yaw, 0f));
            if (controller != null)
                controller.enabled = true;
            Physics.SyncTransforms();
        }

        static void PlacePlayer(Vector3 feet, float yaw) => Place((Component)PlayerState.Current, feet, yaw);

        static void ShowOverlay(bool shown)
        {
            AgentDebugOverlay overlay = Object.FindAnyObjectByType<AgentDebugOverlay>();
            if (overlay != null)
                overlay.Shown = shown;
        }

        Camera NewCamera(string name)
        {
            var go = new GameObject(name);
            _created.Add(go);
            var camera = go.AddComponent<Camera>();
            camera.enabled = false;
            camera.fieldOfView = 50f;
            camera.nearClipPlane = 0.05f;
            camera.farClipPlane = 200f;
            camera.useOcclusionCulling = false;   // the evidence cameras stand outside the baked occlusion volume
            return camera;
        }

        void Aim(Vector3 from, Vector3 at, float fov = 50f)
        {
            _camera.transform.position = from;
            _camera.transform.LookAt(at);
            _camera.fieldOfView = fov;
        }

        // ---------------------------------------------------------------- capture

        // The evidence camera into a 1280 x 720 picture, with the agents' rigs drawn over it or not.
        Texture2D Render(bool rig)
        {
            var target = RenderTexture.GetTemporary(Width, Height, 24);
            _camera.targetTexture = target;
            _rig.Drawing = rig;
            _camera.Render();
            _rig.Drawing = false;
            _camera.targetTexture = null;

            var picture = new Texture2D(Width, Height, TextureFormat.RGB24, false);
            RenderTexture previous = RenderTexture.active;
            RenderTexture.active = target;
            picture.ReadPixels(new Rect(0, 0, Width, Height), 0, 0);
            picture.Apply();
            RenderTexture.active = previous;
            RenderTexture.ReleaseTemporary(target);
            return picture;
        }

        // The rig view of this frame: the agents' renderers off and the game paused for one frame, because the GPU
        // Resident Drawer only takes a renderer out of the picture on the frame after it is switched off.
        IEnumerator RenderRig(Action<Texture2D> done)
        {
            var hidden = new List<Renderer>();
            foreach (AgentController agent in AgentSpawner.Instance.SpawnedAgents)
            {
                if (agent == null)
                    continue;
                foreach (Renderer renderer in agent.GetComponentsInChildren<Renderer>())
                {
                    if (renderer.enabled)
                    {
                        renderer.enabled = false;
                        hidden.Add(renderer);
                    }
                }
            }
            float timeScale = Time.timeScale;
            Time.timeScale = 0f;
            yield return null;
            yield return new WaitForEndOfFrame();
            done(Render(true));
            foreach (Renderer renderer in hidden)
                if (renderer != null)
                    renderer.enabled = true;
            Time.timeScale = timeScale;
        }

        static void Save(Texture2D picture, string path, bool jpg = false)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllBytes(path, jpg ? picture.EncodeToJPG(92) : picture.EncodeToPNG());
            Object.Destroy(picture);
        }

        static void SaveStill(Texture2D picture, string name)
        {
            File.WriteAllBytes(Path.Combine(Folder, name + ".png"), picture.EncodeToPNG());
            Debug.Log($"EVIDENCE_AGENTS still {name} {picture.width}x{picture.height}");
        }

        // A still from the evidence camera, with no HUD.
        IEnumerator Still(string name)
        {
            yield return new WaitForEndOfFrame();
            Texture2D picture = Render(false);
            SaveStill(picture, name);
            Object.Destroy(picture);
        }

        // A still of the screen as the player sees it: the HUD, the cutscene letterbox and subtitles, the overlay's labels.
        static IEnumerator ScreenStill(string name)
        {
            yield return new WaitForEndOfFrame();
            Texture2D picture = ScreenCapture.CaptureScreenshotAsTexture();
            SaveStill(picture, name);
            Object.Destroy(picture);
        }

        static (string name, Func<float, bool> when) At(string name, float seconds) => (name, t => t >= seconds);

        /// <summary>
        /// Records <paramref name="seconds"/> of game time at 30 frames a second. Side by side: the evidence camera
        /// and its rig view of the same moment. Otherwise the screen as the player sees it. <paramref name="eachFrame"/>
        /// runs before every frame with the time into the clip (to move the player, to knock an agent out). Each of
        /// <paramref name="stills"/> is saved as a PNG file the first frame its condition holds (with a <c>_rig</c>
        /// twin when side by side).
        /// </summary>
        IEnumerator Clip(string name, float seconds, bool sideBySide, Action<float> eachFrame,
            params (string name, Func<float, bool> when)[] stills)
        {
            var stillsLeft = new List<(string name, Func<float, bool> when)>(stills);
            string frames = Path.Combine(FrameFolder, name);
            if (Directory.Exists(frames))
                Directory.Delete(frames, true);
            Directory.CreateDirectory(frames);

            Time.captureFramerate = Fps;
            int count = Mathf.RoundToInt(seconds * Fps);
            float start = Time.time;
            for (int i = 0; i < count; i++)
            {
                TopUp();
                eachFrame?.Invoke(Time.time - start);
                yield return new WaitForEndOfFrame();
                float t = Time.time - start;
                var due = stillsLeft.Where(s => s.when(t)).ToList();
                foreach (var still in due)
                    stillsLeft.Remove(still);

                Texture2D picture = sideBySide ? Render(false) : ScreenCapture.CaptureScreenshotAsTexture();
                foreach (var still in due)
                    SaveStill(picture, still.name);
                Save(picture, Path.Combine(frames, $"a{i:0000}.jpg"), true);

                if (sideBySide)
                {
                    Texture2D rig = null;
                    yield return RenderRig(r => rig = r);
                    foreach (var still in due)
                        SaveStill(rig, still.name + "_rig");
                    Save(rig, Path.Combine(frames, $"b{i:0000}.jpg"), true);
                }
                yield return null;
            }
            Time.captureFramerate = 0;
            foreach (var still in stillsLeft)
                Debug.LogWarning("EVIDENCE_AGENTS still not taken: " + still.name);
            Encode(name, frames, sideBySide);
        }

        static void Encode(string name, string frames, bool sideBySide)
        {
            string ffmpeg = Environment.GetEnvironmentVariable("FR_FFMPEG");
            if (string.IsNullOrEmpty(ffmpeg))
                ffmpeg = "ffmpeg";
            string output = Path.GetFullPath(Path.Combine(Folder, name + ".mp4"));
            string input = Path.GetFullPath(frames);
            string arguments = sideBySide
                ? $"-y -loglevel error -framerate {Fps} -i \"{input}/a%04d.jpg\" -framerate {Fps} -i \"{input}/b%04d.jpg\" " +
                  "-filter_complex \"[0]scale=960:540[l];[1]scale=960:540[r];[l][r]hstack\" " +
                  $"-c:v libx264 -preset slow -crf 24 -pix_fmt yuv420p -movflags +faststart \"{output}\""
                : $"-y -loglevel error -framerate {Fps} -i \"{input}/a%04d.jpg\" -vf \"scale=1280:720\" " +
                  $"-c:v libx264 -preset slow -crf 24 -pix_fmt yuv420p -movflags +faststart \"{output}\"";
            try
            {
                using (Process process = Process.Start(new ProcessStartInfo(ffmpeg, arguments)
                       { UseShellExecute = false, CreateNoWindow = true, RedirectStandardError = true }))
                {
                    string errors = process.StandardError.ReadToEnd();
                    process.WaitForExit();
                    if (process.ExitCode != 0)
                        Debug.LogWarning("EVIDENCE_AGENTS ffmpeg failed for " + name + ": " + errors);
                    else
                        Debug.Log("EVIDENCE_AGENTS clip " + output);
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning($"EVIDENCE_AGENTS no ffmpeg ({e.Message}); the frames of {name} are in {input}");
            }
        }

        // ---------------------------------------------------------------- Chapter 1

        // The Env ceilings, hidden while a camera looks down from above them.
        static List<GameObject> HideCeilings()
        {
            var hidden = new List<GameObject>();
            foreach (string name in new[] { "Ceilings", "CeilingLights_Baked" })
            {
                GameObject ceiling = GameObject.Find(name);
                if (ceiling != null)
                {
                    ceiling.SetActive(false);
                    hidden.Add(ceiling);
                }
            }
            return hidden;
        }

        /// <summary>
        /// The Assembly Floor: the reframed Tracker shot of the intro; the Tracker hunting along its route with the
        /// overlay on; the Tracker running to a wind-up toy; the Tracker, a Saboteur and the Guard powering down
        /// and rebooting side by side, and slumping at a wall; then the pause and recall screens.
        /// </summary>
        [UnityTest]
        public IEnumerator Evidence_ChapterOneTrackerSlumpsAndScreens()
        {
            yield return LoadGame(keepIntro: true);

            // The intro's second shot frames the Tracker powering on.
            bool trackerShot = false;
            void OnShot(string id, int shot) => trackerShot |= id == "intro" && shot == 1;
            CutsceneDirector.Current.ShotStarted += OnShot;
            yield return Until(() => trackerShot, 60f, "the intro's Tracker shot");
            CutsceneDirector.Current.ShotStarted -= OnShot;
            yield return Wait(1.6f);
            yield return ScreenStill("cutscene_intro_tracker");
            yield return SkipCutscenes();

            AgentController tracker = Agent(AgentType.Tracker);
            AgentController saboteur = Agent(AgentType.Saboteur, 'B');
            AgentController guard = Agent(AgentType.Guard);

            // The Tracker hunting the player across the floor, its route drawn, from above the ceiling.
            List<GameObject> ceilings = HideCeilings();
            ShowOverlay(true);
            Aim(new Vector3(10.5f, 17f, 0.5f), new Vector3(10.5f, 0f, 10.5f), 62f);
            Vector3 walkFrom = new Vector3(17f, 0.1f, 4f), walkTo = new Vector3(4f, 0.1f, 5f);
            yield return Clip("clip_tracker_path_following", 10f, true,
                t => PlacePlayer(Vector3.Lerp(walkFrom, walkTo, Mathf.PingPong(t / 5f, 1f)), -90f),
                At("tracker_path_following", 4f));
            ShowOverlay(false);
            foreach (GameObject ceiling in ceilings)
                ceiling.SetActive(true);

            // A wind-up toy thrown beside the Tracker: it runs to it, hops and wags.
            Place(tracker, new Vector3(5f, 0f, 12f), 90f);
            PlacePlayer(new Vector3(12f, 0.1f, 6f), -55f);
            yield return Wait(0.5f);
            Component thrower = ((Component)PlayerState.Current).GetComponent(Type.GetType("ToyFactory.Player.PlayerToyThrower, Assembly-CSharp"));
            Aim(new Vector3(10f, 2.6f, 6.5f), new Vector3(5.5f, 0.5f, 11f), 45f);
            bool thrown = false;
            yield return Clip("clip_tracker_windup_lure", 8f, true, t =>
            {
                if (!thrown && t >= 0.5f)
                {
                    thrown = true;
                    thrower.GetType().GetMethod("Throw").Invoke(thrower, null);
                }
            }, At("tracker_windup_lure", 4.5f));

            // Three robots side by side, knocked out for 4 s: each slumps its own way and reboots.
            PlacePlayer(new Vector3(18f, 0.1f, 18f), 180f);
            AgentController[] robots = { tracker, saboteur, guard };
            float[] xs = { 10.2f, 12f, 13.9f };
            yield return Clip("clip_slump_and_reboot", 7f, true, t =>
            {
                if (t < 0.4f)
                {
                    for (int i = 0; i < robots.Length; i++)
                        Place(robots[i], new Vector3(xs[i], 0f, 11f), 180f);
                    Aim(new Vector3(12f, 1.6f, 6.4f), new Vector3(12f, 0.75f, 11f), 50f);
                }
                else if (!tracker.IsDisabled && t < 1f)
                {
                    foreach (AgentController robot in robots)
                        robot.Disable(4f);
                }
            }, At("agent_slumps", 2.6f));

            // At the south wall the lean shrinks so nothing pokes through.
            Place(guard, new Vector3(6f, 0f, 0.95f), 180f);
            Place(saboteur, new Vector3(8.2f, 0f, 0.85f), 180f);
            Place(tracker, new Vector3(16f, 0f, 16f), 0f);
            yield return Wait(0.3f);
            guard.Disable(6f);
            saboteur.Disable(6f);
            tracker.Disable(30f);
            yield return Wait(2.2f);
            Aim(new Vector3(10.8f, 1.6f, 3.4f), new Vector3(7f, 0.6f, 0.6f), 50f);
            yield return Still("agent_slumps_wall");

            // The pause screen, then a recall with restarts left.
            Place(tracker, new Vector3(18f, 0f, 18f), 0f);
            PlacePlayer(new Vector3(10.5f, 0.1f, 3f), 0f);
            yield return Wait(0.5f);
            GameClock.Current.RequestState(GameState.Paused);
            yield return Wait(0.5f);
            yield return ScreenStill("screen_pause");
            RunControl.Current.Resume();
            yield return Until(() => GameClock.Current.State == GameState.Playing, 10f, "resume");
            PlayerState.Current.TakeDamage(10000f, -1);
            yield return Until(() => RunControl.Current.IsRecalled && GameClock.Current.State == GameState.Paused, 15f, "the recall screen");
            yield return new WaitForSecondsRealtime(0.8f);
            yield return ScreenStill("screen_recall");
        }

        // ---------------------------------------------------------------- Chapter 3

        /// <summary>
        /// The Captain waking in the ch3 cutscene, then the Storage Area: the "checkpoint saved" note; the Captain's
        /// prediction marker as the player runs from the Painting Room to the Storage task; the same run from above
        /// with a chaser beside the Captain; and the overlay close up.
        /// </summary>
        [UnityTest]
        public IEnumerator Evidence_ChapterThreeInterceptAndWake()
        {
            yield return LoadGame();
            yield return JumpTo(2);

            // Chapter 2's "checkpoint saved" note goes first: in a run it is long gone before the next cutscene.
            CheckpointToastView toast = Object.FindAnyObjectByType<ScreensPresenter>().CheckpointToast;
            yield return Until(() => { TopUp(); return !toast.IsShowing; }, CheckpointToastView.TotalSeconds + 5f, "the checkpoint note to go");

            // Chapter 2 finished as the jump does, but the ch3 cutscene is left to play: the dormant Captain wakes.
            ChapterFlow flow = ChapterManager.Current.Flow;
            ChapterDefinition painting = flow.CurrentDefinition;
            foreach (TaskDefinition task in painting.Tasks)
                flow.CompleteTask(task.TaskId);
            flow.CompleteTask(ChapterFlow.SwitchTaskPrefix + painting.SwitchNumber);
            yield return Until(() => IsCutscenePlaying && CutsceneDirector.Current.CurrentCutsceneId == "ch3", 20f, "the ch3 cutscene");
            yield return Clip("clip_cutscene_ch3_captain_wakes", 13f, false, null, At("cutscene_ch3_captain_wakes", 10f));
            yield return SkipCutscenes();
            yield return Until(() => flow.CurrentChapter == 3, 20f, "Chapter 3");
            ChapterJump jump = Object.FindAnyObjectByType<ChapterJump>();
            jump.PlacePlayer(3);
            yield return Wait(0.6f);
            yield return ScreenStill("screen_checkpoint_saved");

            AgentController captain = Agent(AgentType.Captain);
            CaptainCallout callout = captain.GetComponent<CaptainCallout>();
            Vector3 captainSpawn = captain.Position;

            // The player runs from the Painting Room to the Storage task.
            Vector3[] route =
            {
                new Vector3(26f, 0.1f, 6f), new Vector3(31f, 0.1f, 17f), new Vector3(31f, 0.1f, 23.5f), new Vector3(36f, 0.1f, 30f)
            };
            float[] legs = { 0f, 2.6f, 4.0f, 6.2f };
            void RunTheRoute(float t)
            {
                float walk = Mathf.Min(t * 0.55f, legs[legs.Length - 1]);
                int leg = 0;
                while (leg < legs.Length - 2 && walk > legs[leg + 1])
                    leg++;
                Vector3 at = Vector3.Lerp(route[leg], route[leg + 1], Mathf.InverseLerp(legs[leg], legs[leg + 1], walk));
                Vector3 ahead = route[leg + 1] - route[leg];
                PlacePlayer(at, Mathf.Atan2(ahead.x, ahead.z) * Mathf.Rad2Deg);
            }

            // The prediction marker: on that run the Captain commits to a goal and flashes it.
            PlacePlayer(route[0], 0f);
            float started = Time.time;
            yield return Until(() =>
            {
                TopUp();
                RunTheRoute(Time.time - started);
                return callout.IsMarking;
            }, 40f, "the Captain to flash a goal");
            yield return Wait(0.3f);
            Vector3 mark = callout.MarkPoint;
            Vector3 view = mark - captain.Position;
            view.y = 0f;
            view = view.sqrMagnitude > 0.01f ? view.normalized : Vector3.forward;
            Aim(mark - view * 6f + Vector3.Cross(Vector3.up, view) * 2.5f + Up * 3f, mark + Up * 0.6f, 55f);
            yield return Still("captain_prediction_marker");

            // The same run again from above, the Captain back at its post and a chaser beside it.
            Place(captain, captainSpawn, 180f);
            PlacePlayer(route[0], 0f);
            yield return Wait(0.5f);
            AgentController chaser = InterceptDemo.Start(out string problem);
            Assert.IsNotNull(chaser, problem);
            Assert.IsTrue(InterceptDemo.ToggleTopDownView(), "the top-down camera");
            yield return Wait(0.5f);
            yield return Clip("clip_intercept_vs_chase_top", 12f, false, RunTheRoute, At("intercept_vs_chase_top", 7f));

            // The same moment, closer: the overlay's labels over the Storage doorway.
            Camera top = GameObject.Find("Demo Top-Down Camera")?.GetComponent<Camera>();
            Assert.IsNotNull(top, "the top-down camera");
            top.transform.position = new Vector3(28f, 18f, 24f);
            top.fieldOfView = 55f;
            yield return Wait(0.2f);
            yield return ScreenStill("intercept_vs_chase_overlay");
            InterceptDemo.ToggleTopDownView();
            ShowOverlay(false);
            chaser.Scrap();
        }

        // ---------------------------------------------------------------- Chapter 4

        /// <summary>
        /// The Control Room: the Captain aiming its arm cannon with the charge glow and firing a bolt; knocked out
        /// (the burst and the word, as the player sees it); kneeling and standing back up; and the ending's
        /// "SHUTDOWN" and "NOT DEFECTIVE!" shots.
        /// </summary>
        [UnityTest]
        public IEnumerator Evidence_ChapterFourCaptainAndEnding()
        {
            yield return LoadGame();
            yield return JumpTo(4);
            AgentController captain = Agent(AgentType.Captain);
            CaptainCannon cannon = captain.GetComponentInChildren<CaptainCannon>();
            AgentCannonAim aim = captain.GetComponent<AgentCannonAim>();
            Vector3 spot = new Vector3(10.5f, 0f, 33f);

            // Aim and shoot: the player 9 m in front, a camera from the side.
            Aim(spot + new Vector3(-4.2f, 1.9f, -2.4f), spot + new Vector3(0f, 1.5f, -1.8f), 55f);
            yield return Clip("clip_captain_aim_and_shot", 8f, true, t =>
                {
                    Place(captain, spot, 180f);
                    PlacePlayer(spot + new Vector3(0f, 0.1f, -9f), 0f);
                },
                ("captain_cannon_charged", t => cannon != null && cannon.Charge > 0.85f),
                ("captain_aim_pose", t => aim != null && aim.Weight > 0.95f));

            // Knocked out as the player sees it: the burst and the comic word.
            PlacePlayer(spot + new Vector3(0f, 0.1f, -6f), 0f);
            Place(captain, spot, 180f);
            yield return Wait(0.5f);
            for (int i = 0; i < 50 && !captain.IsDisabled; i++)
                captain.TakeHit();
            Assert.IsTrue(captain.IsDisabled, "the Captain is knocked out");
            yield return Wait(0.35f);
            yield return ScreenStill("captain_knockout_word");
            yield return Until(() => { TopUp(); return !captain.IsDisabled; }, 15f, "the Captain to reboot");
            yield return Wait(2f);

            // Kneel and stand, side by side with its rig.
            PlacePlayer(spot + new Vector3(6f, 0.1f, -7f), -40f);
            Aim(spot + new Vector3(2.4f, 1.4f, -4.2f), spot + new Vector3(0f, 1.2f, 0f), 55f);
            yield return Clip("clip_captain_kneel_and_wake", 9f, true, t =>
            {
                if (t < 0.6f || captain.IsDisabled)
                    Place(captain, spot, 180f);
                if (t >= 0.6f && t < 1f && !captain.IsDisabled)
                    captain.Disable(4f);
            }, At("captain_kneel", 2.5f));

            // The ending: "SHUTDOWN" over the kneeling Captain, then 047's "NOT DEFECTIVE!".
            var shots = new List<int>();
            void OnShot(string id, int shot)
            {
                if (id == "ending")
                    shots.Add(shot);
            }
            CutsceneDirector.Current.ShotStarted += OnShot;
            CutsceneDirector.Current.Play("ending");
            yield return Until(() => shots.Contains(0), 20f, "the ending's first shot");
            yield return new WaitForSeconds(1.4f);
            yield return ScreenStill("cutscene_ending_shutdown");
            yield return Until(() => shots.Contains(3), 40f, "the quality check shot");
            yield return new WaitForSeconds(3.4f);
            yield return ScreenStill("cutscene_ending_not_defective");
            CutsceneDirector.Current.ShotStarted -= OnShot;
        }
    }

    /// <summary>
    /// Draws the agents' rigs over the evidence camera's picture while <see cref="Drawing"/> is on: each pivot
    /// joined to its parent pivot (yellow) and to the centre of every part it carries (orange), a cross on each
    /// pivot, the CharacterController capsule (cyan) and the facing on the floor (green).
    /// </summary>
    public sealed class RigLines : MonoBehaviour
    {
        static readonly Color Bone = new Color(1f, 0.85f, 0.1f);
        static readonly Color Part = new Color(1f, 0.5f, 0.15f);
        static readonly Color Capsule = new Color(0.2f, 0.9f, 1f);
        static readonly Color Facing = new Color(0.3f, 1f, 0.4f);

        Material _material;
        Camera _camera;

        public bool Drawing { get; set; }

        void Awake() => _camera = GetComponent<Camera>();

        void OnEnable() => RenderPipelineManager.endCameraRendering += Draw;

        void OnDisable() => RenderPipelineManager.endCameraRendering -= Draw;

        void OnDestroy()
        {
            if (_material != null)
                Destroy(_material);
        }

        static bool IsJoint(Transform t) => t.name.EndsWith("_Pivot") || t.name.EndsWith("_Root");

        void Draw(ScriptableRenderContext context, Camera camera)
        {
            if (!Drawing || camera != _camera || AgentSpawner.Instance == null)
                return;
            if (_material == null)
            {
                _material = new Material(Shader.Find("Hidden/Internal-Colored")) { hideFlags = HideFlags.HideAndDontSave };
                _material.SetInt("_ZTest", (int)CompareFunction.Always);
                _material.SetInt("_ZWrite", 0);
                _material.SetInt("_Cull", (int)CullMode.Off);
            }

            // Lines as camera-facing quads LineWidth pixels wide, so the rigs read at half size in a
            // side-by-side clip (GL lines are one pixel).
            _eye = camera.transform.position;
            _metresPerPixelAtOne = 2f * Mathf.Tan(camera.fieldOfView * 0.5f * Mathf.Deg2Rad) / camera.pixelHeight;
            GL.PushMatrix();
            GL.LoadProjectionMatrix(camera.projectionMatrix);
            GL.modelview = camera.worldToCameraMatrix;
            _material.SetPass(0);
            GL.Begin(GL.QUADS);
            foreach (AgentController agent in AgentSpawner.Instance.SpawnedAgents)
            {
                if (agent == null || !agent.gameObject.activeInHierarchy)
                    continue;
                DrawRig(agent.transform);
                DrawCapsule(agent);
            }
            GL.End();
            GL.PopMatrix();
        }

        const float LineWidth = 3f;
        Vector3 _eye;
        float _metresPerPixelAtOne;

        void Line(Vector3 a, Vector3 b, Color colour, float pixels = LineWidth)
        {
            Vector3 middle = (a + b) * 0.5f;
            Vector3 side = Vector3.Cross(b - a, middle - _eye);
            if (side.sqrMagnitude < 1e-10f)
                return;
            side = side.normalized * (0.5f * pixels * _metresPerPixelAtOne * Vector3.Distance(middle, _eye));
            GL.Color(colour);
            GL.Vertex(a - side);
            GL.Vertex(a + side);
            GL.Vertex(b + side);
            GL.Vertex(b - side);
        }

        void DrawRig(Transform agent)
        {
            foreach (Transform t in agent.GetComponentsInChildren<Transform>())
            {
                if (!IsJoint(t))
                    continue;
                Transform parent = t.parent;
                while (parent != null && parent != agent && !IsJoint(parent))
                    parent = parent.parent;
                if (parent != null && parent != agent)
                    Line(parent.position, t.position, Bone);

                const float s = 0.035f;
                Line(t.position - t.right * s, t.position + t.right * s, Bone);
                Line(t.position - t.up * s, t.position + t.up * s, Bone);
                Line(t.position - t.forward * s, t.position + t.forward * s, Bone);

                foreach (Transform child in t)
                {
                    if (IsJoint(child))
                        continue;
                    Renderer part = child.GetComponent<Renderer>();
                    if (part != null && !(part is ParticleSystemRenderer))
                        Line(t.position, part.bounds.center, Part, 2f);
                }
            }
        }

        void DrawCapsule(AgentController agent)
        {
            var body = agent.GetComponent<CharacterController>();
            if (body == null)
                return;
            Transform t = agent.transform;
            Vector3 centre = t.TransformPoint(body.center);
            float r = body.radius;
            float half = Mathf.Max(0f, body.height * 0.5f - r);
            Vector3 low = centre - Vector3.up * half, high = centre + Vector3.up * half;
            const int segments = 20;
            for (int i = 0; i < segments; i++)
            {
                float a0 = i * Mathf.PI * 2f / segments, a1 = (i + 1) * Mathf.PI * 2f / segments;
                Vector3 p0 = new Vector3(Mathf.Cos(a0), 0f, Mathf.Sin(a0)) * r;
                Vector3 p1 = new Vector3(Mathf.Cos(a1), 0f, Mathf.Sin(a1)) * r;
                Line(low + p0, low + p1, Capsule, 2f);
                Line(high + p0, high + p1, Capsule, 2f);
                if (i % 5 == 0)
                    Line(low + p0, high + p0, Capsule, 2f);
            }
            // The domes, as two arcs each way.
            for (int i = 0; i < 8; i++)
            {
                float a0 = i * Mathf.PI / 16f, a1 = (i + 1) * Mathf.PI / 16f;
                foreach (Vector3 axis in new[] { t.right, t.forward })
                {
                    Line(high + axis * Mathf.Cos(a0) * r + Vector3.up * Mathf.Sin(a0) * r, high + axis * Mathf.Cos(a1) * r + Vector3.up * Mathf.Sin(a1) * r, Capsule, 2f);
                    Line(high - axis * Mathf.Cos(a0) * r + Vector3.up * Mathf.Sin(a0) * r, high - axis * Mathf.Cos(a1) * r + Vector3.up * Mathf.Sin(a1) * r, Capsule, 2f);
                    Line(low + axis * Mathf.Cos(a0) * r - Vector3.up * Mathf.Sin(a0) * r, low + axis * Mathf.Cos(a1) * r - Vector3.up * Mathf.Sin(a1) * r, Capsule, 2f);
                    Line(low - axis * Mathf.Cos(a0) * r - Vector3.up * Mathf.Sin(a0) * r, low - axis * Mathf.Cos(a1) * r - Vector3.up * Mathf.Sin(a1) * r, Capsule, 2f);
                }
            }
            Vector3 feet = t.position + Vector3.up * 0.02f;
            Vector3 tip = feet + t.forward * (r + 0.5f);
            Line(feet, tip, Facing);
            Line(tip, tip - t.forward * 0.18f + t.right * 0.12f, Facing);
            Line(tip, tip - t.forward * 0.18f - t.right * 0.12f, Facing);
        }
    }
}
