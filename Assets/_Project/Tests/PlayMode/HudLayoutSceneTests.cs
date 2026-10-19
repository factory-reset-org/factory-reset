using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using ToyFactory.Interfaces;
using ToyFactory.Journey.Chapters;
using ToyFactory.Journey.Cutscenes;
using ToyFactory.UI;

namespace ToyFactory.Tests
{
    /// <summary>
    /// The HUD's layout in the real scenes: with every panel and line up at once, at several screen shapes,
    /// nothing overlaps and nothing leaves the screen; the objectives panel folds with Tab and the key
    /// hints go away with H; the charge panel is only as tall as the rows it shows.
    /// </summary>
    public sealed class HudLayoutSceneTests
    {
        const float LoadTimeout = 60f;

        static readonly (int w, int h)[] ScreenShapes =
        {
            (1920, 1080), (1280, 720), (1366, 768), (2560, 1080), (3440, 1440), (1280, 960), (1024, 768)
        };

        readonly List<UnityEngine.Object> _made = new List<UnityEngine.Object>();
        Keyboard _keyboard;
        InputSettings.BackgroundBehavior? _backgroundBehavior;
#if UNITY_EDITOR
        InputSettings.EditorInputBehaviorInPlayMode? _editorBehavior;
#endif

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            Time.timeScale = 1f;
            PlayerPrefs.DeleteKey(HudSections.ObjectivesKey);
            PlayerPrefs.DeleteKey(HudSections.HintsKey);
            if (_keyboard != null)
            {
                InputSystem.RemoveDevice(_keyboard);
                _keyboard = null;
            }

            if (_backgroundBehavior.HasValue)
            {
                InputSystem.settings.backgroundBehavior = _backgroundBehavior.Value;
                _backgroundBehavior = null;
            }

#if UNITY_EDITOR
            if (_editorBehavior.HasValue)
            {
                InputSystem.settings.editorInputBehaviorInPlayMode = _editorBehavior.Value;
                _editorBehavior = null;
            }
#endif

            foreach (UnityEngine.Object made in _made)
            {
                if (made != null)
                    UnityEngine.Object.Destroy(made);
            }

            _made.Clear();

            Scene empty = SceneManager.CreateScene("Empty after HUD layout " + Guid.NewGuid());
            SceneManager.SetActiveScene(empty);
            for (int i = SceneManager.sceneCount - 1; i >= 0; i--)
            {
                Scene scene = SceneManager.GetSceneAt(i);
                if (scene != empty && scene.isLoaded)
                    yield return SceneManager.UnloadSceneAsync(scene);
            }

            if (GameClock.Current is UnityEngine.Object clock && clock == null)
                GameClock.Publish(null);
            if (PlayerState.Current is UnityEngine.Object player && player == null)
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

        // Bootstrap, then the intro skipped: the game is playing and Chapter 1 has started.
        static IEnumerator StartPlaying()
        {
            PlayerPrefs.DeleteKey(HudSections.ObjectivesKey);
            PlayerPrefs.DeleteKey(HudSections.HintsKey);
            SceneManager.LoadScene("Bootstrap");
            yield return Until(() => HudPresenter.Current != null && ChapterManager.Current != null && ChapterManager.Current.Flow != null
                && ChapterManager.Current.Flow.HasBegun && CutsceneDirector.Current != null && PlayerState.Current != null,
                LoadTimeout, "Bootstrap to load the scenes and the HUD");
            GameClock.Current?.RequestState(GameState.Playing);
            yield return Until(() => CutsceneDirector.Current.IsPlaying, 10f, "the intro");
            CutsceneDirector.Current.Skip();
            yield return Until(() => GameClock.Current != null && GameClock.Current.State == GameState.Playing, 10f, "Playing");
            yield return Until(() => ChapterManager.Current.Flow.CurrentChapter == 1, 10f, "Chapter 1");
            yield return null;
            yield return null;
        }

        // Everything the HUD can put up at once: the room banner, the chapter card, the checkpoint note, the
        // prompt, the hold bar and the toy dots, besides the panels that are always there.
        // A virtual keyboard that keeps working when the editor or the test runner does not have the focus.
        void AddKeyboard()
        {
            _backgroundBehavior = InputSystem.settings.backgroundBehavior;
            InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
#if UNITY_EDITOR
            _editorBehavior = InputSystem.settings.editorInputBehaviorInPlayMode;
            InputSystem.settings.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
#endif
            _keyboard = InputSystem.AddDevice<Keyboard>();
        }

        static void ShowEverything(HudPresenter hud)
        {
            hud.SetToys(3, 5);
            hud.SetPrompt("Press E to use the lever");
            hud.SetRoom("Storage Area");
            hud.Card.Show(1, "Assembly Floor", "Wake the line and restore the switch");
            hud.Screens.CheckpointToast.Show("CHECKPOINT SAVED   2 RESTARTS LEFT");

            string rowId = hud.Model.Rows[0].Id;
            var holdIds = (HashSet<string>)typeof(HudPresenter).GetField("_holdIds", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(hud);
            holdIds.Add(rowId);
            typeof(HudPresenter).GetMethod("HandleProgress", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(hud, new object[] { rowId, 0.4f });
        }

        // The longest list the panel can hold: eight rows, each as long as the longest task, and the relay chips.
        static void FillTheObjectivesPanel(HudPresenter hud)
        {
            const string longTask = "Push a crate onto the top belt so it rides onto the pressure plate";
            var rows = new List<HudTaskRow>();
            for (int i = 0; i < 7; i++)
                rows.Add(new HudTaskRow("test." + i, longTask, false, i == 3 ? 3 : 1, 0));
            hud.Model.StartChapter(hud.Model.Chapter, hud.Model.Title, hud.Model.Subtitle, rows, 1);
            hud.Model.SetSequence("test.3", new[] { 2, 3, 1, 4, 5, 6, 7, 8 });
        }

        // Lays the HUD out for a screen of the given shape, the way the player's screen would, and hands back where
        // each panel is in pixels. The canvas scaler reads the screen size once a frame, so it gets two.
        IEnumerator Layout(HudPresenter hud, int width, int height, Action<Dictionary<string, Rect>> done)
        {
            var camObject = new GameObject("HUD layout camera");
            _made.Add(camObject);
            Camera cam = camObject.AddComponent<Camera>();
            cam.orthographic = true;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.cullingMask = 0;
            cam.enabled = false;   // only its size matters here; nothing is drawn
            var target = new RenderTexture(width, height, 16);
            _made.Add(target);
            cam.targetTexture = target;

            foreach (Canvas canvas in hud.GetComponentsInChildren<Canvas>(true))
            {
                if (!canvas.isRootCanvas)
                    continue;
                canvas.renderMode = RenderMode.ScreenSpaceCamera;
                canvas.worldCamera = cam;
                canvas.planeDistance = 1f;
            }

            yield return null;
            yield return null;
            Canvas.ForceUpdateCanvases();
            done(hud.LayoutRects().ToDictionary(pair => pair.Key, pair => Pixels(pair.Value, cam)));
        }

        static Rect Pixels(RectTransform rect, Camera cam)
        {
            var corners = new Vector3[4];
            rect.GetWorldCorners(corners);
            Vector2 a = cam.WorldToScreenPoint(corners[0]);
            Vector2 b = cam.WorldToScreenPoint(corners[2]);
            return Rect.MinMaxRect(Mathf.Min(a.x, b.x), Mathf.Min(a.y, b.y), Mathf.Max(a.x, b.x), Mathf.Max(a.y, b.y));
        }

        static void AssertNothingOverlapsOrLeavesTheScreen(Dictionary<string, Rect> rects, int width, int height, string when)
        {
            string[] names = rects.Keys.ToArray();
            foreach (string name in names)
            {
                Rect r = rects[name];
                Assert.GreaterOrEqual(r.xMin, -1f, $"{when}: {name} leaves the screen on the left");
                Assert.GreaterOrEqual(r.yMin, -1f, $"{when}: {name} leaves the screen at the bottom");
                Assert.LessOrEqual(r.xMax, width + 1f, $"{when}: {name} leaves the screen on the right");
                Assert.LessOrEqual(r.yMax, height + 1f, $"{when}: {name} leaves the screen at the top");
            }

            for (int i = 0; i < names.Length; i++)
            {
                for (int j = i + 1; j < names.Length; j++)
                    Assert.IsFalse(rects[names[i]].Overlaps(rects[names[j]]), $"{when}: {names[i]} {rects[names[i]]} overlaps {names[j]} {rects[names[j]]}");
            }
        }

        [UnityTest]
        public IEnumerator NothingOverlapsWithEveryPanelUpAtEveryScreenShape()
        {
            yield return StartPlaying();
            HudPresenter hud = HudPresenter.Current;
            ShowEverything(hud);
            yield return null;
            yield return null;

            foreach ((int w, int h) in ScreenShapes)
            {
                Dictionary<string, Rect> rects = null;
                yield return Layout(hud, w, h, result => rects = result);
                foreach (string expected in new[] { "Objectives", "Integrity", "Charge", "Key hints", "Score", "Room", "Prompt", "Hold bar", "Chapter card", "Checkpoint toast", "Crosshair" })
                    Assert.IsTrue(rects.ContainsKey(expected), $"{w}x{h}: {expected} is on screen");
                AssertNothingOverlapsOrLeavesTheScreen(rects, w, h, $"{w}x{h}");
            }
        }

        [UnityTest]
        public IEnumerator TheLongestTaskListStillClearsTheOtherPanelsAtEveryScreenShape()
        {
            yield return StartPlaying();
            HudPresenter hud = HudPresenter.Current;
            ShowEverything(hud);
            FillTheObjectivesPanel(hud);
            yield return null;
            yield return null;

            foreach ((int w, int h) in ScreenShapes)
            {
                Dictionary<string, Rect> rects = null;
                yield return Layout(hud, w, h, result => rects = result);
                AssertNothingOverlapsOrLeavesTheScreen(rects, w, h, $"{w}x{h} with the longest list");
            }

            // And folded, it is much shorter.
            float full = hud.ObjectivesHeight;
            hud.ToggleObjectives();
            yield return null;
            yield return null;
            Assert.Less(hud.ObjectivesHeight, full * 0.5f, "Folded, the longest list takes less than half the height.");
        }

        [UnityTest]
        public IEnumerator TheObjectivesPanelFoldsToTheTaskInHandAndOpensAgain()
        {
            yield return StartPlaying();
            HudPresenter hud = HudPresenter.Current;
            int rows = hud.Model.Rows.Count;
            Assert.Greater(rows, 3, "Chapter 1 has several tasks.");
            Assert.IsFalse(hud.ObjectivesCollapsed);
            Assert.AreEqual("TAB  LESS", hud.ObjectivesChipText);
            float full = hud.ObjectivesHeight;
            int shownFull = Enumerable.Range(0, rows).Count(i => hud.RowText(i) != null);
            Assert.AreEqual(rows, shownFull);

            hud.ToggleObjectives();
            yield return null;
            yield return null;

            Assert.IsTrue(hud.ObjectivesCollapsed);
            Assert.AreEqual("TAB  MORE", hud.ObjectivesChipText);
            Assert.AreEqual(1, Enumerable.Range(0, rows).Count(i => hud.RowText(i) != null), "One row is kept.");
            Assert.Less(hud.ObjectivesHeight, full - 100f);
            StringAssert.Contains("DONE", hud.ChapterText, "The folded panel says how many tasks are done.");
            Assert.IsNotNull(hud.RowText(0), "The first task is the one in hand at the start.");

            // Finishing the task in hand moves the folded panel on to the next one.
            ChapterManager.Current.Flow.CompleteTask(hud.Model.Rows[0].Id);
            yield return null;
            yield return null;
            Assert.IsNull(hud.RowText(0), "The finished task is no longer the one kept.");
            Assert.AreEqual(1, Enumerable.Range(0, rows).Count(i => hud.RowText(i) != null), "Still one row.");

            hud.ToggleObjectives();
            yield return null;
            yield return null;

            Assert.IsFalse(hud.ObjectivesCollapsed);
            Assert.AreEqual(full, hud.ObjectivesHeight, 40f, "Open again, the panel is back to its height.");
            Assert.AreEqual(rows, Enumerable.Range(0, rows).Count(i => hud.RowText(i) != null));
        }

        [UnityTest]
        public IEnumerator HPutsTheKeyHintsAwayAndBringsThemBack()
        {
            yield return StartPlaying();
            HudPresenter hud = HudPresenter.Current;
            Assert.IsFalse(hud.HintsHidden);
            Assert.IsTrue(hud.LayoutRects().Any(p => p.Key == "Key hints"));

            hud.ToggleHints();
            Assert.IsTrue(hud.HintsHidden);
            Assert.IsFalse(hud.LayoutRects().Any(p => p.Key == "Key hints"), "The hints are not on screen.");

            hud.ToggleHints();
            Assert.IsFalse(hud.HintsHidden);
            Assert.IsTrue(hud.LayoutRects().Any(p => p.Key == "Key hints"));
        }

        [UnityTest]
        public IEnumerator TheChoicesAreRememberedForTheNextRun()
        {
            yield return StartPlaying();
            HudPresenter.Current.ToggleObjectives();
            HudPresenter.Current.ToggleHints();

            Assert.AreEqual(1, PlayerPrefs.GetInt(HudSections.ObjectivesKey, 0));
            Assert.AreEqual(1, PlayerPrefs.GetInt(HudSections.HintsKey, 0));
            Assert.IsTrue(new HudSections().ObjectivesCollapsed, "A new HUD reads them back.");
        }

        [UnityTest]
        public IEnumerator TabAndHDoTheSameAsTheirMethodsWhilePlaying()
        {
            yield return StartPlaying();
            HudPresenter hud = HudPresenter.Current;
            AddKeyboard();

            InputSystem.QueueStateEvent(_keyboard, new KeyboardState(Key.Tab));
            yield return null;
            yield return null;
            InputSystem.QueueStateEvent(_keyboard, new KeyboardState());
            yield return null;
            Assert.IsTrue(hud.ObjectivesCollapsed, "Tab folds the panel.");
            Assert.IsFalse(hud.HintsHidden);

            InputSystem.QueueStateEvent(_keyboard, new KeyboardState(Key.H));
            yield return null;
            yield return null;
            InputSystem.QueueStateEvent(_keyboard, new KeyboardState());
            yield return null;
            Assert.IsTrue(hud.HintsHidden, "H puts the hints away.");

            InputSystem.QueueStateEvent(_keyboard, new KeyboardState(Key.Tab));
            yield return null;
            yield return null;
            InputSystem.QueueStateEvent(_keyboard, new KeyboardState());
            yield return null;
            Assert.IsFalse(hud.ObjectivesCollapsed, "Tab again opens it.");
        }

        [UnityTest]
        public IEnumerator TabDoesNothingOutsidePlay()
        {
            yield return StartPlaying();
            HudPresenter hud = HudPresenter.Current;
            AddKeyboard();

            GameClock.Current.RequestState(GameState.Paused);
            yield return null;
            yield return null;
            InputSystem.QueueStateEvent(_keyboard, new KeyboardState(Key.Tab));
            yield return null;
            yield return null;
            InputSystem.QueueStateEvent(_keyboard, new KeyboardState());
            yield return null;

            Assert.IsFalse(hud.ObjectivesCollapsed, "Paused, Tab leaves the HUD alone.");
            GameClock.Current.RequestState(GameState.Playing);
        }

        [UnityTest]
        public IEnumerator TheChargePanelIsOnlyAsTallAsTheRowsItShows()
        {
            yield return StartPlaying();
            HudPresenter hud = HudPresenter.Current;
            hud.SetToys(0, 0);
            Assert.AreEqual(66f, hud.ChargePanelHeight, 0.5f, "The bar alone is one row, as tall as the integrity panel.");

            hud.SetToys(2, 5);
            Assert.AreEqual(112f, hud.ChargePanelHeight, 0.5f, "The toy dots add a row.");

            hud.SetToys(0, 0);
            Assert.AreEqual(66f, hud.ChargePanelHeight, 0.5f, "No toys, no row.");
        }

        [UnityTest]
        public IEnumerator TheKeyHintsFitTheirPlateOnOneLine()
        {
            yield return StartPlaying();
            HudPresenter hud = HudPresenter.Current;
            RectTransform plate = hud.LayoutRects().First(p => p.Key == "Key hints").Value;
            UnityEngine.UI.Text text = plate.GetComponentInChildren<UnityEngine.UI.Text>();
            Assert.AreEqual(HudMath.KeyHints, text.text);
            Assert.LessOrEqual(text.preferredWidth, plate.rect.width - 40f, "The hints, with room to spare at each end.");
            Assert.LessOrEqual(text.preferredHeight, text.fontSize * 1.5f, "One line.");
        }

        [UnityTest]
        public IEnumerator EveryRowOfTheObjectivesPanelKeepsItsFullTextSize()
        {
            yield return StartPlaying();
            HudPresenter hud = HudPresenter.Current;
            FillTheObjectivesPanel(hud);
            yield return null;
            yield return null;

            // Long tasks wrap onto a second line at the normal size; none is shrunk to fit one line.
            foreach (UnityEngine.UI.Text text in hud.GetComponentsInChildren<UnityEngine.UI.Text>(false).Where(t => t.name.StartsWith("Task ")))
            {
                Assert.IsFalse(text.resizeTextForBestFit, text.name + " is not shrunk to fit.");
                Assert.GreaterOrEqual(text.fontSize, 22, text.name + " is not smaller than body text.");
            }
        }
    }
}
