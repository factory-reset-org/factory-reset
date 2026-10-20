using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using ToyFactory.Interfaces;
using ToyFactory.Journey.Chapters;
using ToyFactory.Journey.Cutscenes;
using ToyFactory.UI;
using Object = UnityEngine.Object;

namespace ToyFactory.Tests
{
    /// <summary>
    /// Pictures of every S3 screen in the running game, for <c>Docs/Evidence/UI/</c>: the title (with its Quit
    /// button) and the credits, the HUD in Chapter 1 with everything up and folded, the pause screen, and the
    /// Results screen with a leaderboard. Each is rendered at 16:9 (1920 x 1080) and 21:9 (2560 x 1080) by sending
    /// the game camera and the screen canvases into a render texture, so the file is exactly what a player's
    /// screen of that shape would show.
    /// </summary>
    /// <remarks>
    /// Needs a graphics device (not <c>-nographics</c>), so it is explicit. The run is driven by the test, not played: the names on the
    /// leaderboard are samples and the score is whatever the idle run had when the Results screen was asked for.
    /// The files go to <c>Docs/Evidence/UI/</c> next to the project, or to the folder named by the environment
    /// variable <c>FR_UI_SHOTS</c>.
    /// </remarks>
    [Explicit("Evidence run: about 60 s, needs a graphics device. Writes PNG files to Docs/Evidence/UI.")]
    [Category("Evidence")]
    public sealed class UiScreenshotEvidenceTests
    {
        static readonly (int w, int h, string tag)[] Shapes = { (1920, 1080, "16x9"), (2560, 1080, "21x9") };

        readonly List<string> _written = new List<string>();
        string _folder;

        [SetUp]
        public void SetUp()
        {
            _folder = Environment.GetEnvironmentVariable("FR_UI_SHOTS");
            if (string.IsNullOrEmpty(_folder))
                _folder = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Docs", "Evidence", "UI"));
            Directory.CreateDirectory(_folder);
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            Time.timeScale = 1f;
            Scene empty = SceneManager.CreateScene("Empty after UI screenshots");
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

        static IEnumerator Until(Func<bool> condition, float timeout, string what)
        {
            float until = Time.realtimeSinceStartup + timeout;
            while (!condition())
            {
                if (Time.realtimeSinceStartup > until)
                    Assert.Fail("Timed out waiting for " + what);
                yield return null;
            }
        }

        static IEnumerator Frames(int count)
        {
            for (int i = 0; i < count; i++)
                yield return null;
        }

        // Sends every enabled scene camera and every screen canvas into a render texture of the given size, waits
        // for the scaler and the end of the frame, and writes the picture. Everything is put back afterwards.
        IEnumerator Shoot(string name, int width, int height)
        {
            var target = new RenderTexture(width, height, 24);
            var cameras = Camera.allCameras.Where(c => c.targetTexture == null).ToList();
            foreach (Camera camera in cameras)
                camera.targetTexture = target;

            // The screen canvases are drawn by the camera that has the highest depth, on top of what it renders: an extra
            // camera for them would clear the picture of the world first.
            Camera top = cameras.OrderByDescending(c => c.depth).First();

            var canvases = Object.FindObjectsByType<Canvas>(FindObjectsInactive.Include, FindObjectsSortMode.None)
                .Where(c => c.isRootCanvas && c.renderMode == RenderMode.ScreenSpaceOverlay).ToList();
            foreach (Canvas canvas in canvases)
            {
                canvas.renderMode = RenderMode.ScreenSpaceCamera;
                canvas.worldCamera = top;
                canvas.planeDistance = top.nearClipPlane + 0.05f;
            }

            // The cameras draw into the texture by themselves each frame, so a few frames later it holds the picture.
            // Camera.Render by hand and WaitForEndOfFrame both gave nothing usable in a batch-mode editor run.
            yield return null;
            yield return null;
            Canvas.ForceUpdateCanvases();
            yield return null;
            yield return null;

            var picture = new Texture2D(width, height, TextureFormat.RGB24, false);
            RenderTexture previous = RenderTexture.active;
            RenderTexture.active = target;
            picture.ReadPixels(new Rect(0, 0, width, height), 0, 0);
            picture.Apply();
            RenderTexture.active = previous;

            string path = Path.Combine(_folder, name + ".png");
            File.WriteAllBytes(path, picture.EncodeToPNG());
            _written.Add(path);

            Object.Destroy(picture);
            foreach (Canvas canvas in canvases)
            {
                if (canvas == null)
                    continue;
                canvas.worldCamera = null;
                canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            }

            foreach (Camera camera in cameras)
            {
                if (camera != null)
                    camera.targetTexture = null;
            }

            target.Release();
            Object.Destroy(target);
            yield return null;
        }

        IEnumerator ShootAllShapes(string name)
        {
            foreach ((int w, int h, string tag) shape in Shapes)
                yield return Shoot(name + "_" + shape.tag, shape.w, shape.h);
        }

        [UnityTest]
        public IEnumerator Evidence_EveryScreenAtTwoShapes()
        {
            Assert.IsTrue(SystemInfo.graphicsDeviceType != UnityEngine.Rendering.GraphicsDeviceType.Null, "Run this without -nographics.");

            PlayerPrefs.DeleteKey(HudSections.ObjectivesKey);
            PlayerPrefs.DeleteKey(HudSections.HintsKey);
            SceneManager.LoadScene("Bootstrap");
            yield return Until(() => HudPresenter.Current != null && HudPresenter.Current.Screens != null && GameClock.Current != null
                && ChapterManager.Current != null && ChapterManager.Current.Flow != null && ChapterManager.Current.Flow.HasBegun
                && CutsceneDirector.Current != null && PlayerState.Current != null, 60f, "Bootstrap to load the scenes and the HUD");

            HudPresenter hud = HudPresenter.Current;
            ScreensPresenter screens = hud.Screens;
            string boardPath = Path.Combine(Path.GetTempPath(), "FactoryResetUiShots_" + Guid.NewGuid().ToString("N") + ".json");
            var board = new Leaderboard(boardPath);
            screens.UseLeaderboard(board);
            screens.UseQuit(() => { });

            // ---- Title and credits
            yield return Until(() => screens.Title.IsShowing, 10f, "the title screen");
            yield return Frames(10);
            yield return ShootAllShapes("title");

            screens.Title.RequestCredits();
            yield return Until(() => screens.Credits.IsShowing, 5f, "the credits");
            yield return Frames(5);
            screens.Credits.Advance(6f);
            yield return ShootAllShapes("credits_menu");
            screens.Credits.Close();
            yield return Frames(3);

            // ---- Chapter 1: intro cutscene, chapter card, HUD
            GameClock.Current.RequestState(GameState.Playing);
            yield return Until(() => CutsceneDirector.Current.IsPlaying, 10f, "the intro");
            yield return Frames(120);
            yield return ShootAllShapes("intro_cutscene");
            CutsceneDirector.Current.Skip();
            yield return Until(() => GameClock.Current.State == GameState.Playing, 10f, "Playing");
            yield return Until(() => ChapterManager.Current.Flow.CurrentChapter == 1, 10f, "Chapter 1");
            yield return Frames(20);
            yield return ShootAllShapes("hud_chapter1_card");

            yield return new WaitForSecondsRealtime(7f);
            yield return ShootAllShapes("hud_chapter1");

            // Everything the HUD can put up at once.
            hud.SetToys(3, 5);
            hud.SetPrompt("Press E to use the lever");
            hud.SetRoom("Assembly Floor");
            hud.Screens.CheckpointToast.Show("CHECKPOINT SAVED   2 RESTARTS LEFT");
            string rowId = hud.Model.Rows[0].Id;
            var holdIds = (HashSet<string>)typeof(HudPresenter).GetField("_holdIds", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(hud);
            holdIds.Add(rowId);
            typeof(HudPresenter).GetMethod("HandleProgress", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(hud, new object[] { rowId, 0.4f });
            hud.ReportHit();
            yield return Frames(3);
            yield return ShootAllShapes("hud_everything");

            hud.ToggleObjectives();
            hud.ToggleHints();
            yield return Frames(3);
            yield return ShootAllShapes("hud_folded");
            hud.ToggleObjectives();
            hud.ToggleHints();
            PlayerPrefs.DeleteKey(HudSections.ObjectivesKey);
            PlayerPrefs.DeleteKey(HudSections.HintsKey);

            // ---- Pause
            GameClock.Current.RequestState(GameState.Paused);
            yield return Until(() => screens.Pause.IsShowing, 5f, "the pause screen");
            yield return Frames(5);
            yield return ShootAllShapes("pause");
            GameClock.Current.RequestState(GameState.Playing);
            yield return Frames(3);

            // ---- Results: a sample board of ten, then this run's name entry
            for (int i = 0; i < 9; i++)
                board.Add("Sample " + (char)('A' + i), 14500 - i * 1100, ScoreGrade.A, 900 + i * 40, RunOutcome.Won, DateTime.Today);
            screens.CreditsAfterWin = false;
            CutsceneEvents.RaiseCriticalSignal(CutsceneSignals.FactoryShutdown);
            GameClock.Current.RequestState(GameState.Results);
            yield return Until(() => screens.Results.IsShowing, 5f, "the Results screen");
            yield return Frames(5);
            yield return ShootAllShapes("results_name_entry");

            screens.Results.TrySave("Mia");
            yield return Frames(5);
            yield return ShootAllShapes("results_saved");

            screens.Credits.Show(CreditsMode.Ending);
            yield return Frames(5);
            screens.Credits.Advance(14f);
            yield return ShootAllShapes("credits_ending");

            if (File.Exists(boardPath))
                File.Delete(boardPath);
            Debug.Log("EVIDENCE_UI wrote " + _written.Count + " pictures to " + _folder + ":\n" + string.Join("\n", _written));
            Assert.AreEqual(Shapes.Length * 11, _written.Count);
        }
    }
}
