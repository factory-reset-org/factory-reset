using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UI;
using ToyFactory.Interfaces;
using ToyFactory.Journey.Chapters;
using ToyFactory.Journey.Cutscenes;
using ToyFactory.UI;

namespace ToyFactory.Tests
{
    /// <summary>
    /// The title and Results screens: with a fake game clock for the rules (which screen shows in which
    /// state, what Start asks for, what Results reads from the score manager, saving to the board),
    /// and in the real scenes for the wiring (the HUD builds the screens, the event system exists).
    /// </summary>
    public sealed class ScreensTests
    {
        sealed class FakeClock : IGameClock
        {
            readonly List<IGameStateListener> _listeners = new List<IGameStateListener>();
            public GameState State { get; private set; } = GameState.Playing;
            public float GameTime { get; set; }
            public readonly List<GameState> Requested = new List<GameState>();

            public void AddListener(IGameStateListener listener) => _listeners.Add(listener);
            public void RemoveListener(IGameStateListener listener) => _listeners.Remove(listener);

            public void RequestState(GameState state)
            {
                Requested.Add(state);
                Set(state);
            }

            public void Set(GameState state)
            {
                GameState previous = State;
                State = state;
                foreach (IGameStateListener listener in _listeners.ToArray())
                    listener.OnGameStateChanged(previous, state);
            }
        }

        FakeClock _clock;
        GameObject _host;
        ScreensPresenter _screens;
        ScoreManager _score;
        string _directory;

        [SetUp]
        public void SetUp()
        {
            _clock = new FakeClock();
            GameClock.Publish(_clock);
            _directory = Path.Combine(Path.GetTempPath(), "FactoryResetScreens_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_directory);

            _host = new GameObject("Screens test");
            _score = _host.AddComponent<ScoreManager>();
            var screensObject = new GameObject("Screens");
            screensObject.transform.SetParent(_host.transform, false);
            _screens = screensObject.AddComponent<ScreensPresenter>();
            _screens.UseLeaderboard(new Leaderboard(Path.Combine(_directory, "board.json")));

            // The tests below read the Results screen as soon as a run ends; the credits that roll before it
            // have their own tests.
            _screens.CreditsAfterWin = false;
        }

        [TearDown]
        public void TearDown()
        {
            if (_host != null)
                UnityEngine.Object.Destroy(_host);
            GameClock.Publish(null);
            PlayerState.Publish(null);
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
            if (Directory.Exists(_directory))
                Directory.Delete(_directory, true);
        }

        // The presenter polls in Update, and holds the title back for a few frames.
        static IEnumerator Frame()
        {
            for (int i = 0; i < 4; i++)
                yield return null;
        }

        void FinishRun(bool shutDown, int tasks = 1)
        {
            _clock.GameTime = 0f;
            ChapterEvents.RaiseChapterStarted(1);
            for (int i = 0; i < tasks; i++)
                ChapterEvents.RaiseTaskCompleted("ch1.task" + i);
            _clock.GameTime = 125f;
            if (shutDown)
                CutsceneEvents.RaiseCriticalSignal(CutsceneSignals.FactoryShutdown);
            _clock.Set(GameState.Results);
        }

        // ---- The title screen

        [UnityTest]
        public IEnumerator TitleDoesNotFlashOnAStateThatChangesRightAway()
        {
            _clock.Set(GameState.Title);
            yield return null;
            _clock.Set(GameState.Playing);
            yield return Frame();

            Assert.IsFalse(_screens.Title.IsShowing);
        }

        [UnityTest]
        public IEnumerator TitleShowsInTheTitleStateAndNowhereElse()
        {
            yield return Frame();
            Assert.IsFalse(_screens.Title.IsShowing, "Playing: no screen.");

            _clock.Set(GameState.Title);
            yield return Frame();
            Assert.IsTrue(_screens.Title.IsShowing);
            Assert.IsFalse(_screens.Results.IsShowing);

            _clock.Set(GameState.Playing);
            yield return Frame();
            Assert.IsFalse(_screens.Title.IsShowing);
        }

        // ---- The quit buttons

        static Button QuitButton(Component screen) =>
            screen.GetComponentsInChildren<Button>(true).First(b => b.name == "Quit");

        [UnityTest]
        public IEnumerator TitleHasAQuitButtonLabelledQuitInTheBottomRightCorner()
        {
            _clock.Set(GameState.Title);
            yield return Frame();
            Assert.IsTrue(_screens.Title.IsShowing);

            Button quit = QuitButton(_screens.Title);
            Assert.AreEqual("QUIT", quit.GetComponentInChildren<Text>().text);
            Assert.IsTrue(quit.interactable);

            var rect = (RectTransform)quit.transform;
            Assert.AreEqual(new Vector2(1f, 0f), rect.anchorMin, "Anchored to the bottom right, opposite Credits.");
            Assert.AreEqual(new Vector2(1f, 0f), rect.anchorMax);

            var credits = (RectTransform)TitleCreditsButton(_screens).transform;
            Assert.AreEqual(credits.sizeDelta, rect.sizeDelta, "Same size as the Credits button.");
        }

        [UnityTest]
        public IEnumerator QuitOnTheTitleLeavesTheGameOnceAndDoesNotStartIt()
        {
            int quits = 0;
            _screens.UseQuit(() => quits++);
            _clock.Set(GameState.Title);
            yield return Frame();

            QuitButton(_screens.Title).onClick.Invoke();

            Assert.AreEqual(1, quits);
            Assert.IsEmpty(_clock.Requested, "Quitting does not ask the clock for Playing.");
            Assert.AreEqual(GameState.Title, _clock.State);
        }

        [UnityTest]
        public IEnumerator EnterSpaceAndTheOtherTitleButtonsDoNotQuit()
        {
            int quits = 0;
            _screens.UseQuit(() => quits++);
            _clock.Set(GameState.Title);
            yield return Frame();

            _screens.Title.RequestStart();
            TitleCreditsButton(_screens).onClick.Invoke();

            Assert.AreEqual(0, quits, "Only the Quit button leaves the game.");
        }

        [UnityTest]
        public IEnumerator QuitOnTheResultsScreenLeavesThroughTheSameExit()
        {
            int quits = 0;
            _screens.UseQuit(() => quits++);
            FinishRun(true);
            yield return Frame();
            Assert.IsTrue(_screens.Results.IsShowing);

            QuitButton(_screens.Results).onClick.Invoke();

            Assert.AreEqual(1, quits);
        }

        // ---- The credits

        static Button TitleCreditsButton(ScreensPresenter screens) =>
            screens.Title.GetComponentsInChildren<Button>(true).First(b => b.name == "Credits");

        // Steps the credits by unscaled seconds, as the frames would.
        static void Roll(CreditsView credits, float seconds, float step = 0.1f)
        {
            for (float t = 0f; t < seconds && credits.IsShowing; t += step)
                credits.Advance(step);
        }

        [UnityTest]
        public IEnumerator TitleHasACreditsButtonInsteadOfTheThirdPartyLines()
        {
            _clock.Set(GameState.Title);
            yield return Frame();
            Assert.IsTrue(_screens.Title.IsShowing);

            string shown = string.Join("\n", _screens.Title.GetComponentsInChildren<Text>(true).Select(t => t.text));
            StringAssert.Contains("CREDITS", shown);
            foreach (ModelCredit credit in ThirdPartyCredits.Attribution)
                StringAssert.DoesNotContain(credit.Line, shown, "The model credits moved to the credits screen.");
            StringAssert.DoesNotContain(ThirdPartyCredits.MusicLine, shown);
            Assert.IsFalse(_screens.Credits.IsShowing);

            // At the bottom left of the screen.
            RectTransform rect = (RectTransform)TitleCreditsButton(_screens).transform;
            Assert.AreEqual(Vector2.zero, rect.anchorMin);
            Assert.AreEqual(Vector2.zero, rect.anchorMax);
        }

        [UnityTest]
        public IEnumerator TheCreditsButtonOpensTheCreditsAndBackReturnsToTheTitle()
        {
            _clock.Set(GameState.Title);
            yield return Frame();

            TitleCreditsButton(_screens).onClick.Invoke();
            yield return null;

            Assert.IsTrue(_screens.Credits.IsShowing);
            Assert.AreEqual(CreditsMode.Menu, _screens.Credits.Mode);
            Assert.IsFalse(_screens.Title.IsShowing, "The title is covered, so Enter and Space cannot start the game from behind.");
            Assert.AreEqual("BACK", _screens.Credits.ButtonText);

            string shown = string.Join("\n", _screens.Credits.Lines);
            foreach (ModelCredit credit in ThirdPartyCredits.Attribution)
                StringAssert.Contains(credit.Line, shown);
            StringAssert.Contains(CreditsContent.IsModule, shown, "The module line is in the intro.");

            _screens.Credits.Close();
            yield return null;

            Assert.IsFalse(_screens.Credits.IsShowing);
            Assert.IsTrue(_screens.Title.IsShowing);
            Assert.IsEmpty(_clock.Requested, "Opening and closing the credits asks the clock for nothing.");
            Assert.AreEqual(GameState.Title, _clock.State);
        }

        [UnityTest]
        public IEnumerator TheCreditsRollSmoothlyFromTheStartToTheLastLineAndStayThere()
        {
            _clock.Set(GameState.Title);
            yield return Frame();
            TitleCreditsButton(_screens).onClick.Invoke();
            yield return null;
            CreditsView credits = _screens.Credits;

            Assert.AreEqual(0f, credits.ScrollOffset);
            Assert.Greater(credits.MaxScroll, 1080f, "More to read than fits on a screen.");

            // 1/60 s steps: rests for the first moment, then never moves backwards or faster than the pace.
            float previous = 0f;
            float fastest = 0f;
            for (int i = 0; i < 60 * 12; i++)
            {
                credits.Advance(1f / 60f);
                float step = credits.ScrollOffset - previous;
                Assert.GreaterOrEqual(step, 0f, "Never backwards.");
                fastest = Mathf.Max(fastest, step * 60f);
                previous = credits.ScrollOffset;
                if (i == (int)(CreditsView.StartDelay * 60f) - 2)
                    Assert.AreEqual(0f, credits.ScrollOffset, 0.01f, "It rests while the screen fades in.");
            }

            Assert.Greater(credits.ScrollOffset, 500f, "It is moving.");
            Assert.LessOrEqual(fastest, CreditsView.BaseSpeed * 1.05f, "Easing never overshoots the pace.");

            // The roll up to the end, in larger steps.
            Roll(credits, 120f);
            Assert.IsTrue(credits.AtEnd);
            Assert.AreEqual(credits.MaxScroll, credits.ScrollOffset, 0.01f);
            Roll(credits, 10f);
            Assert.IsTrue(credits.IsShowing, "The menu credits wait for the player at the end.");
            Assert.AreEqual(credits.MaxScroll, credits.ScrollOffset, 0.01f);

            // Up scrolls back, and the screen is not closed by that.
            credits.Advance(0.5f, -1f);
            Assert.Less(credits.ScrollOffset, credits.MaxScroll);
            Assert.IsTrue(credits.IsShowing);
        }

        [UnityTest]
        public IEnumerator DownSpeedsTheRollUpAndTheWheelPushesIt()
        {
            _clock.Set(GameState.Title);
            yield return Frame();
            TitleCreditsButton(_screens).onClick.Invoke();
            yield return null;
            CreditsView credits = _screens.Credits;
            Roll(credits, 3f);

            float before = credits.ScrollOffset;
            for (int i = 0; i < 60; i++)
                credits.Advance(1f / 60f);
            float normal = credits.ScrollOffset - before;

            before = credits.ScrollOffset;
            for (int i = 0; i < 60; i++)
                credits.Advance(1f / 60f, 1f);
            float fast = credits.ScrollOffset - before;
            Assert.Greater(fast, normal * 2f, "Down is faster.");

            before = credits.ScrollOffset;
            credits.Nudge(1f);
            credits.Advance(1f / 60f);
            Assert.Greater(credits.ScrollOffset - before, CreditsView.BaseSpeed / 60f * 1.5f, "A turn of the wheel pushes it on.");
        }

        [UnityTest]
        public IEnumerator AWonRunRollsTheCreditsBeforeTheResultsAndHandsOverWhenTheyEnd()
        {
            _screens.CreditsAfterWin = true;
            FinishRun(shutDown: true);
            yield return Frame();

            Assert.IsTrue(_screens.Credits.IsShowing);
            Assert.AreEqual(CreditsMode.Ending, _screens.Credits.Mode);
            Assert.AreEqual("SKIP", _screens.Credits.ButtonText);
            Assert.IsFalse(_screens.Results.IsShowing, "The results wait for the credits, so no name field takes the keys.");
            Assert.IsFalse(_screens.Title.IsShowing);

            Roll(_screens.Credits, 300f);
            yield return null;

            Assert.IsFalse(_screens.Credits.IsShowing, "It closes itself after holding on the last line.");
            Assert.IsTrue(_screens.Results.IsShowing);
            Assert.AreEqual("FACTORY SHUT DOWN", _screens.Results.HeadlineText);
            Assert.IsTrue(_screens.Results.CanEnterName, "The name field is offered as before.");
            Assert.IsEmpty(_clock.Requested);
        }

        [UnityTest]
        public IEnumerator SkippingTheEndingCreditsShowsTheResultsAtOnce()
        {
            _screens.CreditsAfterWin = true;
            FinishRun(shutDown: true);
            yield return Frame();
            Assert.IsTrue(_screens.Credits.IsShowing);

            _screens.Credits.Close();
            yield return null;

            Assert.IsFalse(_screens.Credits.IsShowing);
            Assert.IsTrue(_screens.Results.IsShowing);
            Assert.AreEqual(GameState.Results, _clock.State);
        }

        [UnityTest]
        public IEnumerator ARecalledRunGoesStraightToTheResultsWithNoCredits()
        {
            _screens.CreditsAfterWin = true;
            FinishRun(shutDown: false);
            yield return Frame();

            Assert.IsFalse(_screens.Credits.IsShowing);
            Assert.IsTrue(_screens.Results.IsShowing);
            Assert.AreEqual("RECALLED", _screens.Results.HeadlineText);
        }

        [UnityTest]
        public IEnumerator LeavingTheResultsStateTakesTheEndingCreditsAway()
        {
            _screens.CreditsAfterWin = true;
            FinishRun(shutDown: true);
            yield return Frame();
            Assert.IsTrue(_screens.Credits.IsShowing);

            _clock.Set(GameState.Title);
            yield return Frame();

            Assert.IsFalse(_screens.Credits.IsShowing);
            Assert.IsFalse(_screens.Results.IsShowing);
            Assert.IsTrue(_screens.Title.IsShowing);
        }

        [UnityTest]
        public IEnumerator StartRequestsPlayingAndHidesTheTitle()
        {
            _clock.Set(GameState.Title);
            yield return Frame();

            _screens.Title.RequestStart();

            CollectionAssert.AreEqual(new[] { GameState.Playing }, _clock.Requested);
            Assert.AreEqual(GameState.Playing, _clock.State);
            Assert.IsFalse(_screens.Title.IsShowing);
        }

        [UnityTest]
        public IEnumerator TitleCreatesAnEventSystemSoTheButtonCanBeClicked()
        {
            Assert.IsNull(UnityEngine.Object.FindAnyObjectByType<EventSystem>(), "None in the test scene.");

            _clock.Set(GameState.Title);
            yield return Frame();

            Assert.IsNotNull(UnityEngine.Object.FindAnyObjectByType<EventSystem>());
        }

        [UnityTest]
        public IEnumerator TheStartButtonStartsTheGame()
        {
            _clock.Set(GameState.Title);
            yield return Frame();

            Button start = _screens.Title.GetComponentsInChildren<Button>(true).FirstOrDefault(b => b.name == "Start");
            Assert.IsNotNull(start);
            start.onClick.Invoke();

            Assert.AreEqual(GameState.Playing, _clock.State);
        }

        [Test]
        public void TitleListsEveryControlWithAKeyAndAnAction()
        {
            Assert.GreaterOrEqual(TitleView.Controls.Length, 8);
            foreach (string[] control in TitleView.Controls)
            {
                Assert.AreEqual(2, control.Length);
                Assert.IsNotEmpty(control[0]);
                Assert.IsNotEmpty(control[1]);
            }
        }

        // ---- The results screen

        [UnityTest]
        public IEnumerator ResultsShowsTheWonRunWithItsNumbers()
        {
            FinishRun(shutDown: true, tasks: 2);
            yield return Frame();

            ResultsView results = _screens.Results;
            Assert.IsTrue(results.IsShowing);
            Assert.AreEqual("FACTORY SHUT DOWN", results.HeadlineText);
            Assert.AreEqual("2:05", results.StatText(ResultsStat.Time));
            Assert.AreEqual("4 of 4", results.StatText(ResultsStat.Chapters));
            Assert.AreEqual("0", results.StatText(ResultsStat.Toys));
            Assert.AreEqual("No shots recorded", results.StatText(ResultsStat.Accuracy));

            // 2 tasks x 300 + 2000 + (3000 - 3 x 125) + 0 integrity (no player) + 0 accuracy.
            int expected = 600 + 2000 + 2625;
            Assert.AreEqual(expected, _score.Score);
            Assert.AreEqual(expected.ToString("N0", System.Globalization.CultureInfo.InvariantCulture), results.ScoreText);
            Assert.AreEqual(ScoreRules.GradeFor(expected).ToString(), results.GradeText);
            Assert.AreEqual("Tasks completed  x2", results.BreakdownLabel(0));
        }

        [UnityTest]
        public IEnumerator ResultsForADeathIsRecalledWithTheChaptersBeforeTheOneReached()
        {
            _clock.GameTime = 0f;
            ChapterEvents.RaiseChapterStarted(1);
            ChapterEvents.RaiseChapterStarted(2);
            ChapterEvents.RaiseChapterStarted(3);
            _clock.GameTime = 70f;
            _clock.Set(GameState.Results);
            yield return Frame();

            Assert.AreEqual("RECALLED", _screens.Results.HeadlineText);
            Assert.AreEqual("2 of 4", _screens.Results.StatText(ResultsStat.Chapters));
            Assert.AreEqual("1:10", _screens.Results.StatText(ResultsStat.Time));
        }

        [UnityTest]
        public IEnumerator ResultsWithNoScoreManagerShowsAnEmptyRecallInsteadOfFailing()
        {
            UnityEngine.Object.Destroy(_score);
            yield return Frame();

            _clock.Set(GameState.Results);
            yield return Frame();

            Assert.IsTrue(_screens.Results.IsShowing);
            Assert.AreEqual("RECALLED", _screens.Results.HeadlineText);
            Assert.AreEqual("D", _screens.Results.GradeText);
            Assert.IsFalse(_screens.Results.CanEnterName, "No points, no place on the board.");
        }

        [UnityTest]
        public IEnumerator RunThatMakesTheBoardOffersANameAndSavesIt()
        {
            FinishRun(shutDown: true);
            yield return Frame();
            ResultsView results = _screens.Results;
            Assert.IsTrue(results.CanEnterName);

            int rank = results.TrySave("  Ada  ");

            Assert.AreEqual(1, rank);
            Assert.AreEqual(1, results.SavedRank);
            Assert.IsFalse(results.CanEnterName, "Saved once.");
            Assert.AreEqual("Ada", results.BoardName(0));
            Assert.AreEqual(0, results.TrySave("Again"), "A run is saved once.");

            var reloaded = new Leaderboard(Path.Combine(_directory, "board.json"));
            Assert.IsTrue(reloaded.Load());
            Assert.AreEqual("Ada", reloaded.Entries[0].name);
            Assert.AreEqual(_score.Score, reloaded.Entries[0].score);
            Assert.AreEqual("Won", reloaded.Entries[0].outcome);
        }

        [UnityTest]
        public IEnumerator EmptyNameIsSavedAsThePlayerDefault()
        {
            FinishRun(shutDown: true);
            yield return Frame();

            _screens.Results.TrySave(string.Empty);

            Assert.AreEqual(Leaderboard.DefaultName, _screens.Results.BoardName(0));
        }

        [UnityTest]
        public IEnumerator ResultsShowsTheSavedBoardFromEarlierRuns()
        {
            var earlier = new Leaderboard(Path.Combine(_directory, "board.json"));
            earlier.Add("OLD", 99999, ScoreGrade.S, 300f, RunOutcome.Won, new DateTime(2026, 10, 9));
            Assert.IsTrue(earlier.Save());

            FinishRun(shutDown: true);
            yield return Frame();
            _screens.Results.TrySave("NEW");

            Assert.AreEqual("OLD", _screens.Results.BoardName(0));
            Assert.AreEqual("NEW", _screens.Results.BoardName(1));
            Assert.AreEqual(2, _screens.Results.SavedRank);
        }

        [UnityTest]
        public IEnumerator ACorruptBoardFileDoesNotStopTheResultsScreen()
        {
            File.WriteAllText(Path.Combine(_directory, "board.json"), "{ this is not json");

            FinishRun(shutDown: true);
            yield return Frame();

            Assert.IsTrue(_screens.Results.IsShowing);
            Assert.IsTrue(_screens.Results.CanEnterName);
            Assert.AreEqual(1, _screens.Results.TrySave("OK"));
        }

        [UnityTest]
        public IEnumerator SaveButtonAndTheNameFieldDoTheSameAsTrySave()
        {
            FinishRun(shutDown: true);
            yield return Frame();

            InputField field = _screens.Results.GetComponentInChildren<InputField>(true);
            Assert.IsNotNull(field);
            Assert.AreEqual(Leaderboard.MaxNameLength, field.characterLimit);
            field.text = "Grace";
            Button save = null;
            foreach (Button button in _screens.Results.GetComponentsInChildren<Button>(true))
                if (button.name == "Save")
                    save = button;
            Assert.IsNotNull(save);

            save.onClick.Invoke();

            Assert.AreEqual("Grace", _screens.Results.BoardName(0));
        }

        // ---- The real scenes

        const float LoadTimeout = 60f;

        [UnityTearDown]
        public IEnumerator SceneTearDown()
        {
            Time.timeScale = 1f;
            Scene active = SceneManager.GetActiveScene();
            if (SceneManager.sceneCount > 1 || active.name == "Bootstrap")
            {
                Scene empty = SceneManager.CreateScene("Empty after screens " + Guid.NewGuid());
                SceneManager.SetActiveScene(empty);
                for (int i = SceneManager.sceneCount - 1; i >= 0; i--)
                {
                    Scene scene = SceneManager.GetSceneAt(i);
                    if (scene != empty && scene.isLoaded)
                        yield return SceneManager.UnloadSceneAsync(scene);
                }
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

        static IEnumerator LoadBootstrap()
        {
            SceneManager.LoadScene("Bootstrap");
            yield return Until(() => HudPresenter.Current != null && ChapterManager.Current != null && ChapterManager.Current.Flow != null
                && ChapterManager.Current.Flow.HasBegun && CutsceneDirector.Current != null && GameClock.Current != null,
                LoadTimeout, "Bootstrap to load the scenes and the HUD");
        }

        [UnityTest]
        public IEnumerator TheHudBuildsTheScreensInTheRealScenes()
        {
            // The fake host from SetUp is not wanted here.
            UnityEngine.Object.Destroy(_host);
            GameClock.Publish(null);
            yield return LoadBootstrap();

            Assert.IsNotNull(HudPresenter.Current.Screens);
            Assert.IsNotNull(HudPresenter.Current.Screens.Title);
            Assert.IsNotNull(HudPresenter.Current.Screens.Results);
        }

        [UnityTest]
        public IEnumerator InTheRealScenesTitleStartsTheGameAndResultsAppearsAtTheEnd()
        {
            UnityEngine.Object.Destroy(_host);
            GameClock.Publish(null);
            yield return LoadBootstrap();
            ScreensPresenter screens = HudPresenter.Current.Screens;
            IGameClock clock = GameClock.Current;

            // Skip the intro so the clock is free, then go back to the title as a fresh start would.
            // Bootstrap waits on the title screen; this is the player pressing Start.
            if (GameClock.Current != null)
                GameClock.Current.RequestState(GameState.Playing);
            yield return Until(() => CutsceneDirector.Current.IsPlaying, 10f, "the intro");
            CutsceneDirector.Current.Skip();
            yield return Until(() => clock.State == GameState.Playing, 10f, "Playing");
            clock.RequestState(GameState.Title);
            yield return Frame();
            Assert.IsTrue(screens.Title.IsShowing);
            Assert.IsFalse(HudPresenter.Current.IsVisible, "The HUD stays hidden behind the title.");

            screens.Title.RequestStart();
            yield return null;
            Assert.AreEqual(GameState.Playing, clock.State);
            Assert.IsFalse(screens.Title.IsShowing);

            clock.RequestState(GameState.Results);
            yield return null;
            yield return null;
            Assert.IsTrue(screens.Results.IsShowing);
            Assert.AreEqual("RECALLED", screens.Results.HeadlineText, "The factory was not shut down.");
            Assert.IsFalse(HudPresenter.Current.IsVisible);
        }
    }
}
