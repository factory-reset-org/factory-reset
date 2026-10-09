using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using ToyFactory.Interfaces;
using ToyFactory.Journey.Chapters;
using ToyFactory.Journey.Cutscenes;

namespace ToyFactory.Tests
{
    /// <summary>
    /// The chapter card shows "CHAPTER n OF 4", the chapter's title and subtitle and the route
    /// for 4.5 s when a chapter starts; never over a cutscene (it waits for the cutscene to end);
    /// and it steps aside for a cutscene that starts while it is up, then comes back.
    /// </summary>
    public sealed class ChapterCardTests
    {
        readonly List<Object> _created = new List<Object>();

        [TearDown]
        public void TearDown()
        {
            for (int i = _created.Count - 1; i >= 0; i--)
                if (_created[i] != null)
                    Object.DestroyImmediate(_created[i]);
            _created.Clear();
        }

        static void SetField(object target, string name, object value) =>
            target.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Instance).SetValue(target, value);

        static IEnumerator Seconds(float seconds)
        {
            float until = Time.realtimeSinceStartup + seconds;
            while (Time.realtimeSinceStartup < until)
                yield return null;
        }

        T Track<T>(T asset) where T : Object
        {
            _created.Add(asset);
            return asset;
        }

        ChapterCard Card()
        {
            var go = Track(new GameObject("Chapter Card"));
            return go.AddComponent<ChapterCard>();
        }

        // Two chapters with Story.md's card text.
        void Chapters()
        {
            TaskDefinition task = Track(TaskDefinition.Create("a", 1, "A", TaskType.Interact, "a"));
            TaskDefinition console = Track(TaskDefinition.Create(ChapterEvents.ConsoleTaskId, 2, "Console", TaskType.HoldAt, "con"));
            ChapterDefinition one = Track(ChapterDefinition.Create(1, "The Assembly Line", "Restart the assembly line", "Assembly Floor", new[] { task }, 1, "ch2"));
            ChapterDefinition two = Track(ChapterDefinition.Create(2, "The Painting Room", "Calibrate the paint line", "Painting Room", new[] { console }, 0, ""));
            var go = Track(new GameObject("Chapters"));
            go.SetActive(false);
            var manager = go.AddComponent<ChapterManager>();
            SetField(manager, "chapters", new[] { one, two });
            go.SetActive(true);
        }

        [UnityTest]
        public IEnumerator AChapterStartShowsItsCardForFourAndAHalfSeconds()
        {
            Chapters();
            ChapterCard card = Card();
            yield return null;

            ChapterEvents.RaiseChapterStarted(2);
            Assert.IsTrue(card.IsShowing);
            Assert.AreEqual("CHAPTER 2 OF 2", card.ChapterLine);
            Assert.AreEqual("The Painting Room", card.Title);
            Assert.AreEqual("Calibrate the paint line", card.Subtitle);
            Assert.AreEqual(2, card.RouteDone, "The route marks chapter 2 as the current node.");

            yield return Seconds(3.5f);
            Assert.IsTrue(card.IsShowing, "Still up after 3.5 s.");
            yield return Seconds(1.3f);
            Assert.IsFalse(card.IsShowing, "Gone after 4.5 s.");
        }

        [UnityTest]
        public IEnumerator ItWaitsForTheCutsceneThatEndsAChapterAndNeverShowsOverOne()
        {
            ChapterCard card = Card();
            yield return null;

            CutsceneEvents.RaiseCutsceneStarted("ch2");
            ChapterEvents.RaiseChapterStarted(2);
            yield return Seconds(0.3f);
            Assert.IsFalse(card.IsShowing, "Not over the cutscene.");
            Assert.AreEqual("", card.ChapterLine);

            CutsceneEvents.RaiseCutsceneEnded("ch2");
            yield return Seconds(0.2f);
            Assert.IsFalse(card.IsShowing, "Waits for the letterbox to open.");
            yield return Seconds(0.5f);
            Assert.IsTrue(card.IsShowing, "Then shows.");
            Assert.AreEqual("CHAPTER 2 OF 4", card.ChapterLine, "Without chapter data it still counts the four chapters.");
        }

        [UnityTest]
        public IEnumerator ACutsceneThatStartsWhileItIsUpHidesItUntilTheCutsceneEnds()
        {
            ChapterCard card = Card();
            yield return null;

            ChapterEvents.RaiseChapterStarted(1);
            Assert.IsTrue(card.IsShowing);
            yield return Seconds(1f);

            CutsceneEvents.RaiseCutsceneStarted("intro");
            Assert.IsFalse(card.IsShowing, "Steps aside for the cutscene.");
            CutsceneEvents.RaiseCutsceneEnded("intro");
            yield return Seconds(0.7f);
            Assert.IsTrue(card.IsShowing, "Back once the cutscene is over.");
            Assert.AreEqual("CHAPTER 1 OF 4", card.ChapterLine);
            yield return Seconds(3.5f);
            Assert.IsTrue(card.IsShowing, "And shown in full again.");
        }
    }
}
